using System.IO;
using AppWrap.Build;
using AppWrap.Core;
using AppWrap.Core.TestHarness;

/// <summary>
/// Phase 4 (runtime test harness) checks that need no real process launch: the
/// report model, the classification rules, snapshot diffing, and the full
/// copy → sandbox → snapshot → classify → report pipeline driven by a stub
/// runner that simulates clean, leaking, temp-writing, crashing and hanging
/// apps by writing into the sandbox roots it is handed. The real end-to-end
/// launch checks are Windows-only and live in TestHarnessAsyncChecks.
/// </summary>
internal static class TestHarnessChecks
{
    public static void Run(string root, string evidence, Action<string, bool> check, Action<string, Action> reject)
    {
        var work = Path.Combine(evidence, "test-harness"); Directory.CreateDirectory(work);

        // --- Report model ------------------------------------------------------
        var dataWrite = new DetectedWrite(WatchedRootKind.Package, "Data/settings.ini", WriteChange.Created, WriteCategory.PortableData, false);
        var leakWrite = new DetectedWrite(WatchedRootKind.Profile, "AppData/Roaming/FixtureApp/settings.ini", WriteChange.Created, WriteCategory.ProfileLeakage, true);
        var report = new TestReport(TestReport.CurrentSchemaVersion, "Fixture App", TestRunMode.PafPackage, "\"C:\\pkg\\FixtureApp.exe\"",
            new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero), 1.25, 0, false, [dataWrite, leakWrite], [leakWrite], TestVerdict.Fail);
        var json = report.ToJson();
        var parsed = TestReport.FromJson(json);
        check("test report json round-trip", parsed.SchemaVersion == report.SchemaVersion && parsed.AppName == report.AppName && parsed.Mode == report.Mode
            && parsed.LaunchCommand == report.LaunchCommand && parsed.StartedUtc == report.StartedUtc && parsed.DurationSeconds == report.DurationSeconds
            && parsed.ExitCode == report.ExitCode && parsed.TimedOut == report.TimedOut && parsed.Verdict == report.Verdict
            && parsed.DetectedWrites.SequenceEqual(report.DetectedWrites) && parsed.Violations.SequenceEqual(report.Violations));
        check("test report json uses the documented schema", json.Contains("\"schemaVersion\": 1", StringComparison.Ordinal)
            && json.Contains("\"mode\": \"PafPackage\"", StringComparison.Ordinal) && json.Contains("\"category\": \"ProfileLeakage\"", StringComparison.Ordinal)
            && json.Contains("\"violation\": true", StringComparison.Ordinal) && json.Contains("\"verdict\": \"Fail\"", StringComparison.Ordinal));
        var unreadable = false;
        try { TestReport.FromJson("not json"); } catch (Exception) { unreadable = true; }
        check("test report rejects unreadable json", unreadable);

        // --- Classification ------------------------------------------------------
        check("data write is the expected portable write", WriteClassifier.Classify(WatchedRootKind.Package, "Data/settings.ini") == (WriteCategory.PortableData, false));
        check("data folder itself classifies as portable data", WriteClassifier.Classify(WatchedRootKind.Package, "Data") == (WriteCategory.PortableData, false));
        check("package root write is noted, not a violation", WriteClassifier.Classify(WatchedRootKind.Package, "App/Payload/trace.log") == (WriteCategory.PackageRoot, false)
            && WriteClassifier.Classify(WatchedRootKind.Package, "FixtureAppPortable.exe") == (WriteCategory.PackageRoot, false));
        check("lookalike data prefix is not portable data", WriteClassifier.Classify(WatchedRootKind.Package, "Database/settings.ini") == (WriteCategory.PackageRoot, false));
        check("profile write is leakage and a violation", WriteClassifier.Classify(WatchedRootKind.Profile, "AppData/Roaming/FixtureApp/settings.ini") == (WriteCategory.ProfileLeakage, true)
            && WriteClassifier.Classify(WatchedRootKind.Profile, "Documents/notes.txt") == (WriteCategory.ProfileLeakage, true));
        check("temp write is noted, not a violation", WriteClassifier.Classify(WatchedRootKind.Temp, "scratch.tmp") == (WriteCategory.Temporary, false));
        check("verdict passes a clean run", WriteClassifier.Verdict([dataWrite], false, 0) == TestVerdict.Pass);
        check("verdict passes with noted writes only", WriteClassifier.Verdict([dataWrite,
            new DetectedWrite(WatchedRootKind.Temp, "scratch.tmp", WriteChange.Created, WriteCategory.Temporary, false),
            new DetectedWrite(WatchedRootKind.Package, "trace.log", WriteChange.Created, WriteCategory.PackageRoot, false)], false, 0) == TestVerdict.Pass);
        check("verdict fails on a violation", WriteClassifier.Verdict([dataWrite, leakWrite], false, 0) == TestVerdict.Fail);
        check("verdict fails on timeout", WriteClassifier.Verdict([dataWrite], true, null) == TestVerdict.Fail);
        check("verdict fails on crash exit code", WriteClassifier.Verdict([dataWrite], false, 7) == TestVerdict.Fail);
        check("verdict fails when no exit code was produced", WriteClassifier.Verdict([], false, null) == TestVerdict.Fail);

        // --- Snapshot diff --------------------------------------------------------
        var tree = Path.Combine(work, "tree"); Directory.CreateDirectory(Path.Combine(tree, "sub"));
        File.WriteAllText(Path.Combine(tree, "a.txt"), "one");
        File.WriteAllText(Path.Combine(tree, "sub", "b.txt"), "bee");
        var before = TreeSnapshot.Capture(tree);
        File.WriteAllText(Path.Combine(tree, "a.txt"), "one — edited");
        File.Delete(Path.Combine(tree, "sub", "b.txt"));
        File.WriteAllText(Path.Combine(tree, "c.txt"), "see");
        var diff = before.Diff(TreeSnapshot.Capture(tree));
        check("snapshot diff sees create modify delete", diff.Count == 3
            && diff.Contains(("c.txt", WriteChange.Created)) && diff.Contains(("a.txt", WriteChange.Modified)) && diff.Contains(("sub/b.txt", WriteChange.Deleted)));
        check("snapshot diff is empty for an unchanged tree", before.Diff(before).Count == 0);
        check("snapshot diff ignores identical rewrites", TreeSnapshot.Capture(tree).Diff(TreeSnapshot.Capture(tree)).Count == 0);

        // --- Pipeline over a stub runner -------------------------------------------
        var fixture = Path.Combine(work, "fixture-package"); Directory.CreateDirectory(Path.Combine(fixture, "App", "AppInfo"));
        File.Copy(Environment.ProcessPath!, Path.Combine(fixture, "FixtureApp.exe"));
        File.WriteAllText(Path.Combine(fixture, "App", "AppInfo", "appinfo.ini"),
            "[Format]\nType=PortableApps.comFormat\nVersion=3.9\n[Details]\nName=Fixture App\nAppID=FixtureAppPortable\n[Control]\nStart=FixtureApp.exe\n");
        string ReportPath(string name) => Path.Combine(work, "reports", name, "test-report.json");
        HashSet<string> SandboxDirs() => Directory.EnumerateDirectories(Path.GetTempPath(), "appwrap-test-*").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sandboxesBefore = SandboxDirs();

        TestRunResult RunWith(StubRunner stub, string name, TestRunMode mode = TestRunMode.Portable) =>
            TestHarness.RunAsync(new TestRunRequest("Fixture App", mode, fixture, ReportPath(name)), stub, CancellationToken.None).GetAwaiter().GetResult();

        var clean = RunWith(new StubRunner(start =>
        {
            Directory.CreateDirectory(Path.Combine(start.WorkingDirectory, "Data"));
            File.WriteAllText(Path.Combine(start.WorkingDirectory, "Data", "settings.ini"), "portable");
        }, exitCode: 0, timedOut: false), "clean", TestRunMode.PafPackage);
        check("clean stub run passes", clean.Report.Verdict == TestVerdict.Pass && clean.Report.ExitCode == 0 && !clean.Report.TimedOut && clean.Report.Mode == TestRunMode.PafPackage);
        check("clean stub run records the data write as portable", clean.Report.DetectedWrites.Any(w => w.Path == "Data/settings.ini" && w.Category == WriteCategory.PortableData && !w.Violation)
            && clean.Report.Violations.Count == 0);
        check("clean stub run writes the report file", File.Exists(clean.ReportPath) && TestReport.FromJson(File.ReadAllText(clean.ReportPath)).Verdict == TestVerdict.Pass);
        check("clean stub run launches the appinfo start file", clean.Report.LaunchCommand.EndsWith("FixtureApp.exe\"", StringComparison.Ordinal));

        var leaky = RunWith(new StubRunner(start =>
        {
            var roaming = start.Environment["APPDATA"];
            Directory.CreateDirectory(Path.Combine(roaming, "FixtureApp"));
            File.WriteAllText(Path.Combine(roaming, "FixtureApp", "settings.ini"), "leaked");
            File.WriteAllText(Path.Combine(start.WorkingDirectory, "Data", "settings.ini"), "portable");
        }, exitCode: 0, timedOut: false), "leaky");
        check("leaky stub run fails with a profile violation", leaky.Report.Verdict == TestVerdict.Fail
            && leaky.Report.Violations.Any(w => w.Root == WatchedRootKind.Profile && w.Path == "AppData/Roaming/FixtureApp/settings.ini" && w.Category == WriteCategory.ProfileLeakage));
        check("leaky stub run still records the portable write", leaky.Report.DetectedWrites.Any(w => w.Path == "Data/settings.ini" && !w.Violation));

        var tempOnly = RunWith(new StubRunner(start =>
        {
            File.WriteAllText(Path.Combine(start.Environment["TEMP"], "scratch.tmp"), "scratch");
            File.WriteAllText(Path.Combine(start.WorkingDirectory, "trace.log"), "beside the exe");
        }, exitCode: 0, timedOut: false), "temp");
        check("temp and package-root writes are noted, not failing", tempOnly.Report.Verdict == TestVerdict.Pass
            && tempOnly.Report.DetectedWrites.Any(w => w.Root == WatchedRootKind.Temp && w.Category == WriteCategory.Temporary && !w.Violation)
            && tempOnly.Report.DetectedWrites.Any(w => w.Path == "trace.log" && w.Category == WriteCategory.PackageRoot && !w.Violation));

        var hung = RunWith(new StubRunner(null, exitCode: null, timedOut: true), "hung");
        check("timeout stub run fails as timed out", hung.Report.TimedOut && hung.Report.ExitCode is null && hung.Report.Verdict == TestVerdict.Fail && File.Exists(hung.ReportPath));

        var crashed = RunWith(new StubRunner(null, exitCode: 7, timedOut: false), "crashed");
        check("crash stub run fails with its exit code", !crashed.Report.TimedOut && crashed.Report.ExitCode == 7 && crashed.Report.Verdict == TestVerdict.Fail);

        check("sandbox trees are cleaned after every stub run", SandboxDirs().SetEquals(sandboxesBefore));

        var empty = Path.Combine(work, "empty-package"); Directory.CreateDirectory(empty);
        var refused = false;
        try { TestHarness.RunAsync(new TestRunRequest("Fixture App", TestRunMode.Portable, empty, ReportPath("empty")), new StubRunner(null, 0, false), CancellationToken.None).GetAwaiter().GetResult(); }
        catch (UserInputException) { refused = true; }
        check("package without a launcher is refused before launch", refused);
        check("refused run still cleans its sandbox", SandboxDirs().SetEquals(sandboxesBefore));
        reject("test run rejects a rooted launcher name", () =>
            TestHarness.RunAsync(new TestRunRequest("Fixture App", TestRunMode.Portable, fixture, ReportPath("bad"), LauncherFileName: "\\evil.exe"), new StubRunner(null, 0, false), CancellationToken.None).GetAwaiter().GetResult());

        // --- Invariant: builds never reach the harness ------------------------------
        var buildFlow = File.ReadAllText(Path.Combine(root, "AppWrap.Build", "BuildService.cs"))
            + File.ReadAllText(Path.Combine(root, "AppWrap.Build", "PortableBuild.cs"))
            + File.ReadAllText(Path.Combine(root, "AppWrap.Build", "BatchBuild.cs"));
        check("build flow never references the test harness", !buildFlow.Contains("TestHarness", StringComparison.Ordinal) && !buildFlow.Contains("TestRun", StringComparison.Ordinal));
    }

    /// <summary>
    /// Simulates the packaged app: performs the writes a real app would, through
    /// the very environment and working directory the harness hands the runner.
    /// </summary>
    private sealed class StubRunner(Action<TestProcessStart>? act, int? exitCode, bool timedOut) : ITestProcessRunner
    {
        public Task<TestProcessOutcome> RunAsync(TestProcessStart start, TimeSpan timeout, CancellationToken token)
        {
            act?.Invoke(start);
            return Task.FromResult(new TestProcessOutcome(exitCode, timedOut, TimeSpan.FromMilliseconds(25)));
        }
    }
}
