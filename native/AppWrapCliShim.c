/*
 * AppWrap.Cli.exe — native command-line shim for the dual-mode AppWrap.
 *
 * Original AppWrap MIT source; the repository LICENSE applies.
 *
 * Since v0.2.3 the wizard and the CLI are one self-contained executable
 * (AppWrap.exe). This tiny console program exists so scripts keep a
 * console-subsystem entry point named AppWrap.Cli.exe: it locates
 * AppWrap.exe in its own directory, starts it with the --cli marker
 * prepended to the caller's arguments (forwarded verbatim, so the
 * caller's quoting is preserved exactly), inherits the console, waits,
 * and exits with the child's exit code.
 *
 * Exit codes: the child's code, or 4 when AppWrap.exe is missing or
 * cannot be started. No state, no files, no network.
 */

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <wchar.h>

#define EXIT_NOT_FOUND 4

/* Returns a pointer just past the program-name token of a command line:
 * the token is either double-quoted (skip to the closing quote) or runs
 * to the first space/tab. The remainder is the caller's argument tail,
 * kept byte-for-byte so its quoting survives untouched. */
static const wchar_t *SkipProgramName(const wchar_t *commandLine)
{
    const wchar_t *p = commandLine;
    while (*p == L' ' || *p == L'\t') p++;
    if (*p == L'"')
    {
        p++;
        while (*p != L'\0' && *p != L'"') p++;
        if (*p == L'"') p++;
    }
    else
    {
        while (*p != L'\0' && *p != L' ' && *p != L'\t') p++;
    }
    return p;
}

int wmain(void)
{
    wchar_t exePath[MAX_PATH];
    DWORD length = GetModuleFileNameW(NULL, exePath, MAX_PATH);
    if (length == 0 || length >= MAX_PATH)
    {
        fwprintf(stderr, L"AppWrap.Cli: could not determine its own location.\n");
        return EXIT_NOT_FOUND;
    }
    wchar_t *slash = wcsrchr(exePath, L'\\');
    if (slash == NULL)
    {
        fwprintf(stderr, L"AppWrap.Cli: could not determine its own directory.\n");
        return EXIT_NOT_FOUND;
    }
    slash[1] = L'\0';
    if (wcslen(exePath) + wcslen(L"AppWrap.exe") + 1 > MAX_PATH)
    {
        fwprintf(stderr, L"AppWrap.Cli: installation path is too long.\n");
        return EXIT_NOT_FOUND;
    }
    wcscat(exePath, L"AppWrap.exe");

    if (GetFileAttributesW(exePath) == INVALID_FILE_ATTRIBUTES)
    {
        fwprintf(stderr,
            L"AppWrap.Cli: AppWrap.exe was not found beside the shim at %s. "
            L"Run the CLI from the AppWrap distribution folder.\n", exePath);
        return EXIT_NOT_FOUND;
    }

    /* New command line: "C:\...\AppWrap.exe" --cli<caller's tail verbatim> */
    const wchar_t *tail = SkipProgramName(GetCommandLineW());
    size_t capacity = wcslen(exePath) + wcslen(L"\"\" --cli") + wcslen(tail) + 1;
    wchar_t *commandLine = (wchar_t *)malloc(capacity * sizeof(wchar_t));
    if (commandLine == NULL)
    {
        fwprintf(stderr, L"AppWrap.Cli: out of memory.\n");
        return EXIT_NOT_FOUND;
    }
    _snwprintf(commandLine, capacity, L"\"%s\" --cli%s", exePath, tail);

    STARTUPINFOW startup;
    ZeroMemory(&startup, sizeof(startup));
    startup.cb = sizeof(startup);
    startup.dwFlags = STARTF_USESTDHANDLES;
    startup.hStdInput = GetStdHandle(STD_INPUT_HANDLE);
    startup.hStdOutput = GetStdHandle(STD_OUTPUT_HANDLE);
    startup.hStdError = GetStdHandle(STD_ERROR_HANDLE);

    PROCESS_INFORMATION process;
    ZeroMemory(&process, sizeof(process));
    BOOL started = CreateProcessW(
        exePath, commandLine,
        NULL, NULL, TRUE, 0, NULL, NULL,
        &startup, &process);
    free(commandLine);
    if (!started)
    {
        fwprintf(stderr, L"AppWrap.Cli: could not start %s (error %lu).\n",
            exePath, (unsigned long)GetLastError());
        return EXIT_NOT_FOUND;
    }

    WaitForSingleObject(process.hProcess, INFINITE);
    DWORD exitCode = EXIT_NOT_FOUND;
    GetExitCodeProcess(process.hProcess, &exitCode);
    CloseHandle(process.hThread);
    CloseHandle(process.hProcess);
    return (int)exitCode;
}
