using AppWrap.Core;
namespace AppWrap.Generators;

/// <summary>Trusted selected-destination upgrade hook. Its paired settings must be used together.</summary>
public static class UpgradeGuardGenerator
{
    public const string InstallerSettings = "[Source]\r\nIncludeInstallerSource=false\r\n[MainDirectories]\r\nRemoveAppDirectory=false\r\nRemoveOtherDirectory=false\r\nRemoveDataDirectory=false\r\n[CheckRunning]\r\nCloseEXE=NONE\r\n";
    public static string Generate(AppWrapInput input, string helperPath)
    {
        static string Literal(string value)
        {
            if (value.Any(char.IsControl) || value.IndexOfAny(['$', '`', '"']) >= 0 || value.Contains("{#", StringComparison.Ordinal))
                throw new UserInputException("Choose portable package paths without quote, dollar, or backtick symbols.");
            return value;
        }
        return $$"""
            ; Original trusted AppWrap hook; source hooks are never imported.
            !macro CustomCodePreInstall
              Push $R1
              InitPluginsDir
              File "/oname=$PLUGINSDIR\AppWrap.UpgradeGuard.exe" "{{Literal(helperPath)}}"
              ClearErrors
              ExecWait "$\"$PLUGINSDIR\AppWrap.UpgradeGuard.exe$\" $\"$INSTDIR$\" $\"{{Literal(PortableAppsGenerator.PackageId(input))}}.exe$\" $\"{{Literal(input.MainExecutable)}}$\"" $R1
              IfErrors appwrap_guard_failed
              StrCmp $R1 0 appwrap_guard_done
              appwrap_guard_failed:
              DetailPrint "AppWrap could not close the selected application's running processes (code $R1)."
              MessageBox MB_OK|MB_ICONEXCLAMATION "The app could not close safely. Close its windows, then run this installer again. Your installed files have not been replaced."
              Pop $R1
              Abort
              appwrap_guard_done:
              Pop $R1
            !macroend
            """;
    }
}
