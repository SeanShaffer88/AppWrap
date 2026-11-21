using AppWrap.Core;
using AppWrap.Generators;
using AppWrap.Build;
using System.IO;

internal static class StandardOptionsChecks
{
    internal static void Run(AppWrapInput input, Action<string, bool> check, Action<string, Action> reject)
    {
        check("standard options null keeps launch/default integration off", StandardInstallerOptions.Normalize(null) == new StandardInstallerOptions());
        foreach (var valid in new[] { "https://example.org/path?q=ok&x=1#part", "http://127.0.0.1:18765/verified", "http://localhost/", "https://example.org/%22quoted%22" })
            check("valid standard website " + valid, StandardInstallerOptions.ValidateWebsite(valid).StartsWith("http", StringComparison.Ordinal));
        foreach (var invalid in new[] { "", "relative/path", "//example.org/path", "javascript:alert(1)", "file:///C:/app.exe", "data:text/html,test", "mailto:a@b.com", "shell:startup", "https://a:b@example.org", "https://example.org/a b", " https://example.org", "https://example.org/\r\n[Run]", "https://example.org/\0", "https://example.org/\"; Filename: calc.exe", "https://example.org/`", "https://example.org/'", "https:\\example.org", "https://example.org/" + new string('a', 2048) })
            reject("reject unsafe standard website " + invalid.Replace('\0', '?').Replace('\r', '?').Replace('\n', '?'), () => StandardInstallerOptions.ValidateWebsite(invalid));
        check("website off accepts blank and discards unused value", StandardInstallerOptions.Normalize(new(DeveloperWebsite: "bad ignored")).DeveloperWebsite == null);
        reject("offered blank website rejected", () => InputRules.Validate(input with { StandardOptions = new(OfferDeveloperWebsite: true) }));
        InputRules.Validate(input with { Mode = OutputMode.PortableApps, StandardOptions = new(OfferDeveloperWebsite: true) });
        check("portable ignores standard-only options", true);
        var all = InnoSetupGenerator.Generate(input with { StandardOptions = new(true, true, true, "http://127.0.0.1:18765/finish?x={app}") }, "C:\\payload", "C:\\out");
        check("website is explicit unchecked silent-safe shell action", all.Contains("Flags: postinstall shellexec skipifsilent unchecked", StringComparison.Ordinal));
        check("URL braces escaped as Inno literal", all.Contains("finish?x={{app}", StringComparison.Ordinal) || all.Contains("finish?x=%7Bapp%7D", StringComparison.OrdinalIgnoreCase));
        check("startup real checkbox uses supported creation API", all.Contains("TNewCheckBox.Create", StringComparison.Ordinal) && all.Contains("CreateShellLink(", StringComparison.Ordinal));
        check("startup never adds unbacked native run items", !all.Contains("RunList.Add", StringComparison.Ordinal));
        check("startup scoped per-user exact link and uninstall-time ownership check", all.Contains("{userstartup}\\AppWrap-", StringComparison.Ordinal) && all.Contains("(CurUninstallStep = usUninstall) and StartupLinkOwned", StringComparison.Ordinal) && !all.Contains("Type: files; Name: \"{userstartup}", StringComparison.Ordinal) && !all.Contains("commonstartup", StringComparison.Ordinal));
        check("startup ownership verifies arguments target cwd and reparse", all.Contains("Link.Arguments = ''", StringComparison.Ordinal) && all.Contains("Link.TargetPath", StringComparison.Ordinal) && all.Contains("Link.WorkingDirectory", StringComparison.Ordinal) && all.Contains("Attr and $400", StringComparison.Ordinal));
        check("startup previous executable migration supported", all.Contains("GetPreviousData('AppWrapStartupExe'", StringComparison.Ordinal) && all.Contains("LinkMatches(PriorRoot, PriorExe, PriorCwd)", StringComparison.Ordinal));
        check("startup uninstall caches ownership before cleanup", all.Contains("function InitializeUninstall", StringComparison.Ordinal) && all.Contains("CleanupExe := GetPreviousData", StringComparison.Ordinal));
        var none = InnoSetupGenerator.Generate(input with { StandardOptions = new(false, false, false) }, "C:\\payload", "C:\\out");
        check("all completion offers can be omitted", !none.Contains("Flags: nowait postinstall", StringComparison.Ordinal) && !none.Contains("shellexec", StringComparison.Ordinal) && none.Contains("StartupOffered = False", StringComparison.Ordinal));
        check("shortcut tasks always offered with selected defaults", all.Contains("Tasks: startmenuicon", StringComparison.Ordinal) && all.Contains("Tasks: desktopicon", StringComparison.Ordinal) && all.Contains("Name: desktopicon; Description: \"Create a Desktop shortcut (optional)\"; Flags: unchecked", StringComparison.Ordinal));
    }

    internal static async Task CompileAsync(BuildService service, AppWrapInput input, string evidence, Action<string, bool> check)
    {
        for (var mask = 0; mask < 8; mask++)
        {
            var options = new StandardInstallerOptions((mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0, "http://127.0.0.1:18765/finish");
            var candidate = input with { OutputPath = Path.Combine(evidence, $"standard-options-{mask}.exe"), StandardOptions = options, CreateStartMenuShortcut = (mask & 1) != 0, CreateDesktopShortcut = (mask & 2) != 0 };
            File.WriteAllText(Path.Combine(evidence, $"standard-options-{mask}.iss"), InnoSetupGenerator.Generate(candidate, input.SourceFolder, Path.Combine(evidence, "compile-inspection")));
            await service.BuildAsync(candidate, CancellationToken.None);
            check("real standard optional combination compile " + mask, new FileInfo(candidate.OutputPath).Length > 10000);
        }
    }
}
