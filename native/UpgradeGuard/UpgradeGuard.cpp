// AppWrap selected-destination upgrade guard. MIT; see ../../LICENSE.
// No persistence, process creation, forced termination, elevation, or network.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <tlhelp32.h>
#include <shellapi.h>
#include <string>
#include <vector>
#include <algorithm>
#include <cwctype>

namespace {
struct Handle {
    HANDLE value = INVALID_HANDLE_VALUE;
    explicit Handle(HANDLE h = INVALID_HANDLE_VALUE) : value(h) {}
    ~Handle() { if (value != INVALID_HANDLE_VALUE && value != nullptr) CloseHandle(value); }
    Handle(const Handle&) = delete; Handle& operator=(const Handle&) = delete;
    Handle(Handle&& other) noexcept : value(other.value) { other.value = INVALID_HANDLE_VALUE; }
};
struct Target { DWORD pid; Handle process; FILETIME started; bool payload; };
bool Equal(const std::wstring& a, const std::wstring& b) { return CompareStringOrdinal(a.c_str(), -1, b.c_str(), -1, TRUE) == CSTR_EQUAL; }
bool Relative(const std::wstring& p) {
    if (p.empty() || p.size() > 240 || p.front() == L'\\' || p.back() == L'\\') return false;
    size_t start = 0;
    for (size_t n = 0; n <= p.size(); ++n) {
        if (n < p.size() && p[n] != L'\\') {
            if (p[n] < 32 || wcschr(L"/:*?\"<>|", p[n])) return false;
            continue;
        }
        auto part = p.substr(start, n-start);
        if (part.empty() || part == L"." || part == L".." || part.back() == L'.' || part.back() == L' ') return false;
        start = n+1;
    }
    return true;
}
bool Canonical(const std::wstring& input, std::wstring& result, bool allowMissing) {
    if (input.size() < 3 || input.size() > 240 || !iswalpha(input[0]) || input[1] != L':' || input[2] != L'\\') return false;
    if (!Relative(input.substr(3))) return false;
    wchar_t full[32768]; DWORD count = GetFullPathNameW(input.c_str(), 32768, full, nullptr);
    if (!count || count >= 32768 || !Equal(input, full)) return false;
    // Check every existing component, including ancestors and target. Reject all links.
    for (size_t end = 3; end <= input.size(); ++end) {
        if (end < input.size() && input[end] != L'\\') continue;
        const auto prefix = input.substr(0, end);
        DWORD attr = GetFileAttributesW(prefix.c_str());
        if (attr == INVALID_FILE_ATTRIBUTES) {
            DWORD error = GetLastError();
            if (allowMissing && (error == ERROR_FILE_NOT_FOUND || error == ERROR_PATH_NOT_FOUND)) { result = input; return true; }
            return false;
        }
        if (attr & FILE_ATTRIBUTE_REPARSE_POINT) return false;
        Handle component(CreateFileW(prefix.c_str(),FILE_READ_ATTRIBUTES,FILE_SHARE_READ|FILE_SHARE_WRITE|FILE_SHARE_DELETE,nullptr,OPEN_EXISTING,FILE_FLAG_BACKUP_SEMANTICS,nullptr));
        if(component.value==INVALID_HANDLE_VALUE) return false;
        wchar_t finalPath[32768];
        DWORD finalCount=GetFinalPathNameByHandleW(component.value,finalPath,32768,FILE_NAME_NORMALIZED|VOLUME_NAME_DOS);
        if(!finalCount || finalCount>=32768) return false;
        std::wstring resolved=finalPath;
        if(resolved.rfind(L"\\\\?\\",0)==0) resolved.erase(0,4);
        if(!Equal(prefix,resolved)) return false;
    }
    result = input;
    return true;
}
std::wstring Base(const std::wstring& path) { auto n=path.find_last_of(L'\\'); return path.substr(n==std::wstring::npos?0:n+1); }
bool SameIdentity(const Target& target) {
    if (WaitForSingleObject(target.process.value, 0) == WAIT_OBJECT_0) return true;
    FILETIME created{}, exited{}, kernel{}, user{};
    return GetProcessId(target.process.value) == target.pid && GetProcessTimes(target.process.value,&created,&exited,&kernel,&user) && CompareFileTime(&created,&target.started)==0;
}
int Collect(const std::wstring& launcher, const std::wstring& payload, std::vector<Target>& targets, ULONGLONG deadline) {
    Handle snapshot(CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS,0));
    if (snapshot.value == INVALID_HANDLE_VALUE) return 5;
    PROCESSENTRY32W entry{}; entry.dwSize=sizeof entry;
    if (!Process32FirstW(snapshot.value,&entry)) return GetLastError()==ERROR_NO_MORE_FILES?0:5;
    unsigned count=0;
    do {
        if(GetTickCount64()>=deadline) return 4;
        if (++count>65536) return 5;
        if (!Equal(entry.szExeFile,Base(launcher)) && !Equal(entry.szExeFile,Base(payload))) continue;
        Handle process(OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION|SYNCHRONIZE,FALSE,entry.th32ProcessID));
        if (!process.value) { if (GetLastError()==ERROR_INVALID_PARAMETER) continue; return 3; }
        if (WaitForSingleObject(process.value,0)==WAIT_OBJECT_0) continue;
        wchar_t image[32768]; DWORD size=32768;
        if (!QueryFullProcessImageNameW(process.value,0,image,&size)) { if (WaitForSingleObject(process.value,0)==WAIT_OBJECT_0) continue; return 3; }
        if (!Equal(image,launcher) && !Equal(image,payload)) continue;
        std::wstring canonical;
        if (!Canonical(image,canonical,false)) return 2;
        FILETIME created{}, exited{}, kernel{}, user{};
        if (!GetProcessTimes(process.value,&created,&exited,&kernel,&user)) return 3;
        if(targets.size()>=1024) return 5;
        targets.push_back(Target{entry.th32ProcessID,std::move(process),created,Equal(image,payload)});
    } while (Process32NextW(snapshot.value,&entry));
    return GetLastError()==ERROR_NO_MORE_FILES?0:5;
}
struct CloseContext { const Target* target; ULONGLONG deadline; bool failed=false; };
BOOL CALLBACK CloseWindow(HWND window, LPARAM parameter) {
    auto& context=*reinterpret_cast<CloseContext*>(parameter);
    DWORD pid=0; GetWindowThreadProcessId(window,&pid);
    if (pid!=context.target->pid) return TRUE;
    if (!SameIdentity(*context.target)) { context.failed=true; return FALSE; }
    if (WaitForSingleObject(context.target->process.value,0)==WAIT_OBJECT_0) return TRUE;
    auto now=GetTickCount64();
    if (now>=context.deadline) { context.failed=true; return FALSE; }
    DWORD_PTR ignored=0;
    const auto remaining=static_cast<UINT>(std::min<ULONGLONG>(context.deadline-now,500));
    if (!SendMessageTimeoutW(window,WM_CLOSE,0,0,SMTO_ABORTIFHUNG|SMTO_BLOCK,remaining,&ignored) && IsWindow(window)) context.failed=true;
    return TRUE;
}
int CloseTargets(std::vector<Target>& targets, bool payload, ULONGLONG deadline) {
    for (const auto& target:targets) {
        if (target.payload!=payload || WaitForSingleObject(target.process.value,0)==WAIT_OBJECT_0) continue;
        if (!SameIdentity(target)) return 2;
        CloseContext context{&target,deadline};
        if (!EnumWindows(CloseWindow,reinterpret_cast<LPARAM>(&context)) && context.failed) return 4;
        if (context.failed) return 4;
    }
    for (const auto& target:targets) {
        if (target.payload!=payload) continue;
        auto now=GetTickCount64(); if(now>=deadline) return 4;
        DWORD result=WaitForSingleObject(target.process.value,static_cast<DWORD>(deadline-now));
        if(result==WAIT_TIMEOUT) return 4;
        if(result!=WAIT_OBJECT_0) return 5;
    }
    return 0;
}
}
int Run(int argc,wchar_t** argv) {
    try {
        if(argc!=4) return 2;
        std::wstring root=argv[1], launcherRelative=argv[2], payloadRelative=argv[3], canonical;
        while(root.size()>3 && root.back()==L'\\') root.pop_back();
        if(!Canonical(root,canonical,true) || !Relative(launcherRelative) || launcherRelative.find(L'\\')!=std::wstring::npos || !Relative(payloadRelative)) return 2;
        DWORD rootAttributes=GetFileAttributesW(root.c_str());
        if(rootAttributes!=INVALID_FILE_ATTRIBUTES && !(rootAttributes&FILE_ATTRIBUTE_DIRECTORY)) return 2;
        const auto launcher=root+L"\\"+launcherRelative;
        const auto payload=root+L"\\App\\Payload\\"+payloadRelative;
        if(!Canonical(launcher,canonical,true) || !Canonical(payload,canonical,true)) return 2;
        const auto deadline=GetTickCount64()+10000;
        for(unsigned attempt=0;attempt<4;++attempt) {
            std::vector<Target> targets;
            int result=Collect(launcher,payload,targets,deadline); if(result) return result;
            if(GetTickCount64()>=deadline) return 4;
            if(targets.empty()) return 0;
            result=CloseTargets(targets,true,deadline); if(result) return result;
            // Root shims should finish naturally after payload exits; allow a short bounded grace.
            for(const auto& target:targets) if(!target.payload) {
                auto now=GetTickCount64(); if(now>=deadline) return 4;
                WaitForSingleObject(target.process.value,static_cast<DWORD>(std::min<ULONGLONG>(100,deadline-now)));
            }
            result=CloseTargets(targets,false,deadline); if(result) return result;
            if(GetTickCount64()>=deadline) return 4;
        }
        return 4;
    } catch(...) { return 5; }
}
int WINAPI wWinMain(HINSTANCE,HINSTANCE,PWSTR,int) {
    int count=0;
    wchar_t** arguments=CommandLineToArgvW(GetCommandLineW(),&count);
    if(!arguments) return 5;
    const int result=Run(count,arguments);
    LocalFree(arguments);
    return result;
}
