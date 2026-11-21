using AppWrap.Core;
using AppWrap.Generators;
internal static class UpgradeGuardChecks
{
    public static void Run(AppWrapInput input, Action<string,bool> check, Action<string,Action> reject)
    {
        var hook=UpgradeGuardGenerator.Generate(input with {MainExecutable="bin\\Bob's.exe"}, "C:\\owned\\guard.exe");
        check("guard hook invokes discrete quoted selected destination", hook.Contains("$\\\"$INSTDIR$\\\"") && hook.Contains("Bob's.exe"));
        check("guard fails before continuation",hook.Contains("IfErrors appwrap_guard_failed") && hook.Contains("Abort") && hook.Contains("StrCmp $R1 0"));
        check("guard safe config has no destructive directories or optional actions",UpgradeGuardGenerator.InstallerSettings.Contains("CloseEXE=NONE") && UpgradeGuardGenerator.InstallerSettings.Contains("RemoveAppDirectory=false") && !UpgradeGuardGenerator.InstallerSettings.Contains("DownloadFiles"));
        reject("guard rejects NSIS expansion",()=>UpgradeGuardGenerator.Generate(input with {MainExecutable="${bad}.exe"},"C:\\guard.exe"));
        reject("guard rejects helper script tokens",()=>UpgradeGuardGenerator.Generate(input,"C:\\${bad}.exe"));
    }
}
