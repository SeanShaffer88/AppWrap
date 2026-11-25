using System.IO;
using AppWrap.Build;
using AppWrap.Core.TestHarness;

/// <summary>
/// Phase 4 real end-to-end launch checks — Windows-only, like PalChecks.Async:
/// they launch actual processes through ProcessTestRunner, so they run only in
/// the real Windows test suite (Program.cs), never in the portable harness.
/// The fixture apps are this test executable copied under Fixture* names;
/// Program.Main dispatches on that name before doing anything else (see the
/// FixtureClean / FixtureLeaky / FixtureHang branches there).
/// </summary>
internal static class TestHarnessAsyncChecks
{
    public static async Task RunAsync(string evidence, Action<string, bool> check)
    {
        var work = Path.Combine(evidence, "test-harness-real"); Directory.CreateDirectory(work);

        string MakePackage(string name, string exeName)
        {
            var package = Path.Combine(work, name);
            Directory.CreateDirectory(Path.Combine(package, "App", "AppInfo"));
            File.Copy(Environment.ProcessPath!, Path.Combine(package, exeName));
            File.WriteAllText(Path.Combine(package, "App", "AppInfo", "appinfo.ini"),
                "[Format]\nType=PortableApps.comFormat\nVersion=3.9\n[Details]\nName=Fixture App\nAppID=FixtureAppPortable\n[Control]\nStart=" + exeName + "\n");
            return package;
        }
        string ReportPath(string name) => Path.Combine(work, "reports", name, "test-report.json");
        HashSet<string> SandboxDirs() => Directory.EnumerateDirectories(Path.GetTempPath(), "appwrap-test-*").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sandboxesBefore = SandboxDirs();

        var clean = await TestHarness.RunAsync(new TestRunRequest("Fixture App", TestRunMode.Portable, MakePackage("clean", "FixtureClean.exe"), ReportPath("clean")), CancellationToken.None);
        check("real clean fixture passes", clean.Report.Verdict == TestVerdict.Pass && clean.Report.ExitCode == 0
            && clean.Report.DetectedWrites.Any(w => w.Path == "Data/settings.ini" && w.Category == WriteCategory.PortableData));
        check("real clean fixture report is on disk", File.Exists(clean.ReportPath));

        var leaky = await TestHarness.RunAsync(new TestRunRequest("Fixture App", TestRunMode.Portable, MakePackage("leaky", "FixtureLeaky.exe"), ReportPath("leaky")), CancellationToken.None);
        check("real leaky fixture is flagged with a portability violation", leaky.Report.Verdict == TestVerdict.Fail
            && leaky.Report.Violations.Any(w => w.Root == WatchedRootKind.Profile && w.Path == "AppData/Roaming/FixtureApp/settings.ini" && w.Category == WriteCategory.ProfileLeakage));

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var hung = await TestHarness.RunAsync(new TestRunRequest("Fixture App", TestRunMode.Portable, MakePackage("hang", "FixtureHang.exe"), ReportPath("hang"), Timeout: TimeSpan.FromSeconds(2)), CancellationToken.None);
        watch.Stop();
        check("real hanging fixture times out and fails", hung.Report.TimedOut && hung.Report.Verdict == TestVerdict.Fail && watch.Elapsed < TimeSpan.FromSeconds(20));
        check("real runs clean their sandboxes, including after a timeout kill", SandboxDirs().SetEquals(sandboxesBefore));
    }
}
