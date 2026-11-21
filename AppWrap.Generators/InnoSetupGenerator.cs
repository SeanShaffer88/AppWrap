using AppWrap.Core;

namespace AppWrap.Generators;

public static class InnoSetupGenerator
{
    // All user strings are literals, never preprocessor expressions or executable code.
    public static string Literal(string value)
    {
        if (value.Any(char.IsControl) || value.Contains("{#", StringComparison.Ordinal)) throw new UserInputException("A name or path contains unsupported characters.");
        return value.Replace("{", "{{", StringComparison.Ordinal).Replace("\"", "\"\"", StringComparison.Ordinal);
    }

    public static string Generate(AppWrapInput input, string payload, string outputFolder)
    {
        var id = InputRules.SafeId(input.AppName);
        var name = Literal(input.AppName);
        var exe = Literal(input.MainExecutable);
        var script = $$"""
            [Setup]
            AppId=AppWrap-{{id}}
            AppName={{name}}
            AppVersion={{input.AppVersion}}
            DefaultDirName={localappdata}\Programs\{{id}}
            DefaultGroupName={{name}}
            PrivilegesRequired=lowest
            OutputDir={{Literal(outputFolder)}}
            OutputBaseFilename=package
            Compression=lzma2
            SolidCompression=yes
            CloseApplications=yes
            RestartApplications=no
            UninstallDisplayIcon={app}\{{exe}}
            WizardStyle=modern
            DisableProgramGroupPage=yes
            [Files]
            Source: "{{Literal(payload)}}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
            [Icons]
            """;
        if (input.CreateStartMenuShortcut) script += $"\nName: \"{{autoprograms}}\\{name}\"; Filename: \"{{app}}\\{exe}\"; WorkingDir: \"{{app}}\\{Literal(Path.GetDirectoryName(input.MainExecutable) ?? "")}\"";
        if (input.CreateDesktopShortcut) script += $"\nName: \"{{autodesktop}}\\{name}\"; Filename: \"{{app}}\\{exe}\"; WorkingDir: \"{{app}}\\{Literal(Path.GetDirectoryName(input.MainExecutable) ?? "")}\"";
        return script + $"\n[Run]\nFilename: \"{{app}}\\{exe}\"; Description: \"Open {name}\"; WorkingDir: \"{{app}}\\{Literal(Path.GetDirectoryName(input.MainExecutable) ?? "")}\"; Flags: nowait postinstall skipifsilent\n";
    }
}
