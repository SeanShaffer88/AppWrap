using System.IO;
using AppWrap.Core;
using AppWrap.Core.PortableProfiles;
using AppWrap.Generators;

/// <summary>
/// P1 checks that need no Windows compiler execution: launcher INI generation,
/// conditional defines, and the exact pinned compiler argument array. The async
/// build-boundary checks live in PalChecks.Async.cs (same partial class) so this
/// file stays compilable without the Build project.
/// </summary>
internal static partial class PalChecks
{
    private static PortableProfile Pal(string launcher) =>
        PortableProfileCodec.FromIniTexts(launcher, "", null, null, PortableExecutionKind.Pal);

    public static void Run(Action<string, bool> check, Action<string, Action> reject)
    {
        var input = new AppWrapInput("source", "Harness App", "1.0", "bin\\Demo.exe", true, false, "out.exe");
        Run(input, check, reject);
    }

    internal static void Run(AppWrapInput input, Action<string, bool> check, Action<string, Action> reject)
    {
        check("pal input profile defaults to null", input.Profile is null);
        check("pal app id matches package id", PortableLauncherGenerator.AppId(input) == PortableAppsGenerator.PackageId(input));

        // Default ProgramExecutable injection under Payload, preserving the document.
        var generated = PortableLauncherGenerator.Generate(input,
            Pal("[Launch]\r\n; keep this comment\r\nWaitForProgram=true\r\n"));
        check("pal launcher default executable injected", generated.Contains("ProgramExecutable=Payload\\bin\\Demo.exe", StringComparison.Ordinal));
        check("pal launcher preserves comments and order",
            generated.Contains("; keep this comment", StringComparison.Ordinal) &&
            generated.IndexOf("; keep this comment", StringComparison.Ordinal) < generated.IndexOf("WaitForProgram=true", StringComparison.Ordinal));

        // An existing valid value is kept as-is and never duplicated.
        var existing = PortableLauncherGenerator.Generate(input,
            Pal("[Launch]\nProgramExecutable=Payload\\bin\\Demo.exe\n"));
        check("pal launcher existing executable preserved once",
            existing.Split("ProgramExecutable=").Length - 1 == 1 && existing.Contains("ProgramExecutable=Payload\\bin\\Demo.exe", StringComparison.Ordinal));
        var lower = PortableLauncherGenerator.Generate(input,
            Pal("[Launch]\nProgramExecutable=payload\\bin\\demo.exe\n"));
        check("pal launcher payload prefix case-insensitive", lower.Contains("ProgramExecutable=payload\\bin\\demo.exe", StringComparison.Ordinal));

        // ProgramExecutable must resolve under App\Payload.
        reject("pal launcher rooted executable rejected", () => PortableLauncherGenerator.Generate(input, Pal("[Launch]\nProgramExecutable=C:\\Demo.exe\n")));
        reject("pal launcher UNC executable rejected", () => PortableLauncherGenerator.Generate(input, Pal("[Launch]\nProgramExecutable=\\\\host\\Payload\\Demo.exe\n")));
        reject("pal launcher parent escape rejected", () => PortableLauncherGenerator.Generate(input, Pal("[Launch]\nProgramExecutable=..\\Demo.exe\n")));
        reject("pal launcher payload escape rejected", () => PortableLauncherGenerator.Generate(input, Pal("[Launch]\nProgramExecutable=Payload\\..\\Demo.exe\n")));
        reject("pal launcher outside payload rejected", () => PortableLauncherGenerator.Generate(input, Pal("[Launch]\nProgramExecutable=App\\Demo.exe\n")));
        reject("pal launcher non-executable rejected", () => PortableLauncherGenerator.Generate(input, Pal("[Launch]\nProgramExecutable=Payload\\readme.txt\n")));
        reject("pal launcher dollar executable rejected", () => PortableLauncherGenerator.Generate(input, Pal("[Launch]\nProgramExecutable=Payload\\$evil.exe\n")));

        // Conditional defines.
        check("pal define XML on", PortableLauncherGenerator.ConditionalDefines(
            Pal("[Activate]\nXML=true\n")).SequenceEqual(["XML_ENABLED"]));
        check("pal define XML off", PortableLauncherGenerator.ConditionalDefines(
            Pal("[Activate]\nXML=false\n")).Count == 0);
        check("pal define XML absent", PortableLauncherGenerator.ConditionalDefines(
            Pal("[Launch]\nProgramExecutable=Payload\\x.exe\n")).Count == 0);
        check("pal define compileforce on", PortableLauncherGenerator.ConditionalDefines(
            Pal("[Launch]\nRunAsAdmin=compileforce\n")).SequenceEqual(["RUNASADMIN_COMPILEFORCE"]));
        check("pal define compileforce per-OS on", PortableLauncherGenerator.ConditionalDefines(
            Pal("[Launch]\nRunAsAdmin7=CompileForce\n")).SequenceEqual(["RUNASADMIN_COMPILEFORCE"]));
        check("pal define runasadmin force is not compileforce", PortableLauncherGenerator.ConditionalDefines(
            Pal("[Launch]\nRunAsAdmin=force\n")).Count == 0);
        check("pal defines ordered XML before compileforce", PortableLauncherGenerator.ConditionalDefines(
            Pal("[Activate]\nXML=true\n[Launch]\nRunAsAdmin=compileforce\n")).SequenceEqual(["XML_ENABLED", "RUNASADMIN_COMPILEFORCE"]));

        // Exact compiler argument array: /O log first, defines in matrix order, script last.
        var backend = "C:\\pal";
        var args = PalLaunchArguments.Build(backend, "C:\\stage\\portable", "C:\\stage\\pal.log",
            "My App Portable", "MyAppPortable", "2.2.9", ["XML_ENABLED"]);
        check("pal compiler arguments exact", args.SequenceEqual(new[]
        {
            "/OC:\\stage\\pal.log",
            "/DPACKAGE=C:\\stage\\portable",
            "/DNamePortable=My App Portable",
            "/DAppID=MyAppPortable",
            "/DVersion=2.2.9",
            "/DXML_ENABLED",
            Path.Combine(backend, "Other", "Source", "PortableApps.comLauncher.nsi"),
        }));
        var plain = PalLaunchArguments.Build(backend, "pkg", "log", "Name Portable", "AppPortable", "2.2.9", []);
        check("pal compiler arguments without conditional defines",
            plain.Length == 6 && plain[^1].EndsWith("PortableApps.comLauncher.nsi", StringComparison.Ordinal));
    }
}
