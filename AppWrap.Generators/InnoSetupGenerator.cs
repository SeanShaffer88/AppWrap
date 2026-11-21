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
        var options = StandardInstallerOptions.Normalize(input.StandardOptions);
        var cwd = Literal(Path.GetDirectoryName(input.MainExecutable) ?? "");
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
        script += $"\nName: \"{{autoprograms}}\\{name}\"; Filename: \"{{app}}\\{exe}\"; WorkingDir: \"{{app}}\\{cwd}\"; Tasks: startmenuicon";
        script += $"\nName: \"{{autodesktop}}\\{name}\"; Filename: \"{{app}}\\{exe}\"; WorkingDir: \"{{app}}\\{cwd}\"; Tasks: desktopicon";
        script += "\n[Tasks]\nName: startmenuicon; Description: \"Create a Start Menu shortcut (optional)\"; Flags: " + (input.CreateStartMenuShortcut ? "" : "unchecked") + "\n";
        script += "Name: desktopicon; Description: \"Create a Desktop shortcut (optional)\"; Flags: " + (input.CreateDesktopShortcut ? "" : "unchecked") + "\n[Run]\n";
        if (options.OfferLaunchAfterInstall) script += $"Filename: \"{{app}}\\{exe}\"; Description: \"Open {name} (optional)\"; WorkingDir: \"{{app}}\\{cwd}\"; Flags: nowait postinstall skipifsilent\n";
        if (options.OfferDeveloperWebsite) script += $"Filename: \"{Literal(options.DeveloperWebsite!)}\"; Description: \"Visit the developer's website (optional)\"; Flags: postinstall shellexec skipifsilent unchecked\n";
        return script + StartupCodeGenerator.Generate(input, options);
    }
}
