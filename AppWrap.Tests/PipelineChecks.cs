using System.IO;
using AppWrap.Build;
using AppWrap.Build.Pipeline;
using AppWrap.Cli;
using AppWrap.Core;
using AppWrap.Core.Audit;
using AppWrap.Core.Pipeline;
using AppWrap.Core.PortableProfiles;
using AppWrap.Core.TestHarness;

/// <summary>
/// Four-stage pipeline checks (portable parts, so they run in the Linux
/// harness): the Sandboxie and engine locks, the bootstrap decision
/// logic, engine planning and engine-backed static audit through a fake
/// runner, PAL path mapping, box config generation and harvest
/// classification, the synthesizer's cleanup/FileWrite extensions, the
/// splash spec, the harness's Sandboxie leak-verification mode through a
/// fake sandbox, and the CLI's new flags. Windows runtime behavior of
/// the real providers stays Unverified.
/// </summary>
public static class PipelineChecks
{
    public static async Task RunAsync(string evidence, Action<string, bool> check, Action<string, Action> reject)
    {
        var root = Path.Combine(evidence, "pipeline");
        Directory.CreateDirectory(root);

        // --- locks ------------------------------------------------------------
        var sbie = SandboxieLock.Pinned;
        check("sandboxie lock pins 1.18.8 from the official release", sbie.Version == "1.18.8"
            && sbie.ReleasePageUrl == "https://github.com/sandboxie-plus/Sandboxie/releases/tag/v1.18.8"
            && sbie.SilentArguments.Contains("/VERYSILENT"));
        var x64 = sbie.AssetFor("x64")!;
        check("sandboxie x64 asset is pinned with an honestly-unverified hash", x64.FileName == "Sandboxie-Plus-x64-v1.18.8.exe"
            && x64.DownloadUrl.EndsWith("/v1.18.8/Sandboxie-Plus-x64-v1.18.8.exe", StringComparison.Ordinal)
            && x64.Sha256 is { Length: 64 } && !x64.Sha256Verified);
        check("sandboxie arm64 asset has no pinned hash yet", sbie.AssetFor("ARM64") is { Sha256: null });
        check("sandboxie detection facts match the official installer", sbie.UninstallKeyPath.EndsWith(@"Uninstall\Sandboxie-Plus_is1", StringComparison.Ordinal)
            && sbie.ServiceKeyPath.EndsWith(@"Services\SbieSvc", StringComparison.Ordinal));
        check("engine lock pins all three engines", ExtractionEngineLock.Pinned.Count == 3
            && ExtractionEngineLock.Pin(ExtractionEngineKind.SevenZip).Version == "26.06"
            && ExtractionEngineLock.Pin(ExtractionEngineKind.InnoExtract).Version == "1.9"
            && ExtractionEngineLock.Pin(ExtractionEngineKind.LessMsi).Version == "2.12.9");
        check("innoextract pin carries the official md5, no invented sha256", ExtractionEngineLock.Pin(ExtractionEngineKind.InnoExtract).Md5 == "72d0d0dd874b6236eaa44411f4470ee1"
            && ExtractionEngineLock.Pinned.All(p => p.Sha256 is null && !p.Sha256Verified));

        // --- bootstrap decisions ------------------------------------------------
        check("bootstrap installs when sandboxie is absent", SandboxieBootstrapPlanner.Decide(SandboxieDetection.Absent).Action == SandboxieBootstrapAction.Install);
        check("bootstrap repairs a partial install", SandboxieBootstrapPlanner.Decide(new SandboxieDetection(true, true, false, "1.18.8", @"C:\Program Files\Sandboxie-Plus")).Action == SandboxieBootstrapAction.Install);
        check("bootstrap upgrades an outdated install", SandboxieBootstrapPlanner.Decide(new SandboxieDetection(true, true, true, "1.15.7", @"C:\Program Files\Sandboxie-Plus")).Action == SandboxieBootstrapAction.Upgrade);
        check("bootstrap leaves a current install alone", SandboxieBootstrapPlanner.Decide(new SandboxieDetection(true, true, true, "1.18.8", @"C:\Program Files\Sandboxie-Plus")).Action == SandboxieBootstrapAction.Ready
            && SandboxieBootstrapPlanner.Decide(new SandboxieDetection(true, true, true, "1.19.1", @"C:\Program Files\Sandboxie-Plus")).Action == SandboxieBootstrapAction.Ready);
        check("bootstrap leaves an unreadable version alone", SandboxieBootstrapPlanner.Decide(new SandboxieDetection(true, true, true, null, @"C:\Program Files\Sandboxie-Plus")).Action == SandboxieBootstrapAction.Ready);
        check("version comparison is numeric", SandboxieBootstrapPlanner.CompareVersions("1.18.8", "1.19") < 0
            && SandboxieBootstrapPlanner.CompareVersions("1.18.8", "1.18.8") == 0
            && SandboxieBootstrapPlanner.CompareVersions("2.0", "1.99.9") > 0);

        // --- engine planning ------------------------------------------------------
        var none = Array.Empty<ExtractionEngineKind>();
        check("engine plan gives inno setup to innoextract only", EnginePlanner.PlanExe(StaticAuditor.InstallerFamily.InnoSetup, [ExtractionEngineKind.InnoExtract]).Engine == ExtractionEngineKind.InnoExtract
            && EnginePlanner.PlanExe(StaticAuditor.InstallerFamily.InnoSetup, [ExtractionEngineKind.SevenZip]).Engine is null);
        check("engine plan gives nsis and wrappers to 7-zip", EnginePlanner.PlanExe(StaticAuditor.InstallerFamily.Nsis, [ExtractionEngineKind.SevenZip]).Engine == ExtractionEngineKind.SevenZip
            && EnginePlanner.PlanExe(StaticAuditor.InstallerFamily.MsiWrapper, [ExtractionEngineKind.SevenZip]).Engine == ExtractionEngineKind.SevenZip
            && EnginePlanner.PlanExe(StaticAuditor.InstallerFamily.Nsis, none).Limitation is not null);
        check("engine plan pairs lessmsi with msi and states the fallback", EnginePlanner.PlanMsi([ExtractionEngineKind.LessMsi]).Engine == ExtractionEngineKind.LessMsi
            && EnginePlanner.PlanMsi(none).Limitation is not null);

        // --- engine-backed static audit (fake runner) ------------------------------
        var exePath = Path.Combine(root, "setup.exe");
        File.WriteAllText(exePath, "MZ...... Inno Setup ......");
        var availability = new EngineAvailability(new Dictionary<ExtractionEngineKind, string> { [ExtractionEngineKind.InnoExtract] = "innoextract.exe" });
        var extracted = await StaticAuditor.AuditExeWithEnginesAsync(exePath, Path.Combine(root, "ex-scratch"), new FakeExtractionRunner(true), availability);
        check("engine audit inventories the extracted payload", extracted.OfKind(AuditFindingKind.Executable).Any()
            && extracted.OfKind(AuditFindingKind.FileDataLocation).Any(f => f.Subject == "data")
            && extracted.OfKind(AuditFindingKind.StaticAuditLimited).Count() == 0
            && extracted.Findings.Any(f => f.Evidence.Contains("innoextract", StringComparison.OrdinalIgnoreCase)));
        var failed = await StaticAuditor.AuditExeWithEnginesAsync(exePath, Path.Combine(root, "ex-fail"), new FakeExtractionRunner(false), availability);
        check("engine failure degrades to the stated limit", failed.OfKind(AuditFindingKind.StaticAuditLimited).Count() == 2);
        var noEngines = await StaticAuditor.AuditExeWithEnginesAsync(exePath, Path.Combine(root, "ex-none"), new FakeExtractionRunner(true), EngineAvailability.None);
        check("no engines prepared keeps the pre-engine behavior", noEngines.OfKind(AuditFindingKind.StaticAuditLimited).Count() == 1);

        var tables = new MsiTables(
            Files: [new MsiFileRow("F1", "C1", "SampleApp.exe", "INSTALLDIR")],
            Registry: [new MsiRegistryRow("R1", 1, @"Software\SampleApp", "InstallDir", @"C:\SampleApp", "C1")],
            Shortcuts: [], Directories: [new MsiDirectoryRow("TARGETDIR", null, "SourceDir"), new MsiDirectoryRow("INSTALLDIR", "TARGETDIR", "SampleApp")],
            Services: []);
        var msiPath = Path.Combine(root, "sample.msi");
        File.WriteAllBytes(msiPath, [0xD0, 0xCF, 0x11, 0xE0]);
        var msiAvailability = new EngineAvailability(new Dictionary<ExtractionEngineKind, string> { [ExtractionEngineKind.LessMsi] = "lessmsi.exe" });
        var msiMerged = await StaticAuditor.AuditMsiWithEnginesAsync(msiPath, FixtureMsiReader.FromJson(FixtureMsiReader.ToJson(tables)),
            Path.Combine(root, "msi-scratch"), new FakeExtractionRunner(true), msiAvailability);
        check("msi engine audit merges table and payload findings", msiMerged.OfKind(AuditFindingKind.RegistryWrite).Count() == 1
            && msiMerged.Findings.Any(f => f.Evidence.Contains("lessmsi", StringComparison.OrdinalIgnoreCase))
            && msiMerged.PayloadFolder is not null);

        // --- engine backend location ------------------------------------------------
        var fakeBase = Path.Combine(root, "fakebase");
        Directory.CreateDirectory(Path.Combine(fakeBase, "third_party", "engines", "7zip"));
        File.WriteAllText(Path.Combine(fakeBase, "third_party", "engines", "7zip", "7za.exe"), "fake");
        var located = EngineBackend.Locate(fakeBase);
        check("engine backend locates prepared engines only", located.Has(ExtractionEngineKind.SevenZip)
            && !located.Has(ExtractionEngineKind.InnoExtract) && !located.Has(ExtractionEngineKind.LessMsi));

        // --- path mapping --------------------------------------------------------------
        var context = new PalPathContext(
            @"C:\Users\Test", @"C:\Users\Test\AppData\Roaming", @"C:\Users\Test\AppData\Local",
            @"C:\ProgramData", @"C:\Program Files", @"C:\Program Files (x86)", @"C:\Windows",
            @"D:\Portable\SampleApp\App", @"D:\Portable\SampleApp\Data");
        check("path mapper tokenizes against the machine context", PalPathMapper.ToPalPath(@"C:\Users\Test\AppData\Roaming\Vendor\App.ini", context) == @"%APPDATA%\Vendor\App.ini"
            && PalPathMapper.ToPalPath(@"D:\Portable\SampleApp\App\bin\app.exe", context) == @"%PAL:AppDir%\bin\app.exe"
            && PalPathMapper.ToPalPath(@"C:\Users\Test\Documents\a.txt", context) == @"%USERPROFILE%\Documents\a.txt"
            && PalPathMapper.ToPalPath(@"E:\Elsewhere\file.bin", context) == @"E:\Elsewhere\file.bin");
        check("path mapper tokenizes known roots by shape", PalPathMapper.TokenizeKnownRoots(@"C:\Users\Who\AppData\Local\Vendor\x.db") == @"%LOCALAPPDATA%\Vendor\x.db"
            && PalPathMapper.TokenizeKnownRoots(@"D:\Program Files (x86)\Vendor\app.exe") == @"%PROGRAMFILES(X86)%\Vendor\app.exe"
            && PalPathMapper.TokenizeKnownRoots(@"C:\ProgramData\Vendor\cache") == @"%PROGRAMDATA%\Vendor\cache"
            && PalPathMapper.TokenizeKnownRoots(@"D:\Games\App\config.ini") == @"D:\Games\App\config.ini");
        check("box paths map back to host paths, bookkeeping excluded",
            PalPathMapper.BoxPathToHostPath(@"drive\D\Apps\SampleApp\app.exe", context) == @"D:\Apps\SampleApp\app.exe"
            && PalPathMapper.BoxPathToHostPath(@"user\Test\AppData\Roaming\Vendor\f.ini", context) == @"C:\Users\Test\AppData\Roaming\Vendor\f.ini"
            && PalPathMapper.BoxPathToHostPath("RegHive", context) is null);

        // --- box config + harvest ---------------------------------------------------------
        var spec = SandboxieBoxSpec.Profiling(@"C:\Scratch\box");
        var config = SandboxieBoxConfig.Generate(spec);
        check("box config pins the dedicated box and its scratch root", config.Contains("[AppWrapProfile]", StringComparison.Ordinal)
            && config.Contains(@"FileRootPath=C:\Scratch\box", StringComparison.Ordinal)
            && config.Contains("BlockNetworkFiles=y", StringComparison.Ordinal)
            && config.Contains("AutoRecover=n", StringComparison.Ordinal));
        reject("box config rejects unsafe box names", () => SandboxieBoxConfig.Generate(new SandboxieBoxSpec("bad]name", @"C:\Scratch\box")));
        var harvest = SandboxieHarvest.FromBoxFiles(
            [@"user\Test\AppData\Roaming\VendorX\settings.ini", @"user\Test\AppData\Local\VendorY\cache.db",
             @"drive\C\ProgramData\VendorZ\shared.dat", @"drive\C\SampleApp\driver.sys", "RegHive"],
            spec, context);
        check("box harvest groups appdata folders as data locations", harvest.Count(f => f.Kind == AuditFindingKind.FileDataLocation) == 3
            && harvest.Any(f => f.Subject == @"%APPDATA%\VendorX")
            && harvest.Any(f => f.Subject == @"%PROGRAMDATA%\VendorZ"));
        check("box harvest keeps drivers as hard warnings", harvest.Any(f => f.Kind == AuditFindingKind.ServiceOrDriver && f.HardWarning));

        RegistrySnapshot HiveSnap(params (string Key, string Name, string Kind, string Data)[] values)
        {
            var map = new Dictionary<string, IReadOnlyDictionary<string, RegistryValueSnapshot>>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in values.GroupBy(v => v.Key))
                map[group.Key] = group.ToDictionary(v => v.Name, v => new RegistryValueSnapshot(v.Name, v.Kind, v.Data), StringComparer.OrdinalIgnoreCase);
            return new RegistrySnapshot(map);
        }
        var loadRoot = SandboxieHarvest.LoadRootFor(spec);
        var hiveChanges = SandboxieHarvest.FromLoadedHive(HiveSnap(
            (loadRoot + @"\MACHINE\SOFTWARE\Vendor", "Machine", "REG_SZ", "1"),
            (loadRoot + @"\USER\S-1-5-21-1001\Software\Vendor", "Theme", "REG_SZ", "dark")), loadRoot);
        check("box hive changes map to host hives", hiveChanges.Count == 2
            && hiveChanges.Any(c => c.KeyPath == @"HKLM\SOFTWARE\Vendor" && c.Category == RegistryChangeCategory.MachineWide)
            && hiveChanges.Any(c => c.KeyPath == @"HKCU\Software\Vendor" && c.ValueName == "Theme"));
        var hiveFindings = RuntimeFindings.FromRegistryChanges(hiveChanges);
        check("box hive changes become registry findings", hiveFindings.Count == 2
            && hiveFindings.Any(f => f.RegistryKeyPath == @"HKCU\Software\Vendor" && f.RegistryValueData == "dark"));

        // --- synthesis extensions ------------------------------------------------------------
        var hostReport = new AuditReport(AuditReport.CurrentSchemaVersion, msiPath, AuditSourceKind.MsiPackage, null, "SampleApp.exe",
        [
            new AuditFinding(AuditFindingKind.FileDataLocation, @"C:\Users\Test\AppData\Roaming\Vendor\App\settings.db",
                "MSI installs into AppData.", "File table row F2", AuditConfidence.High),
            new AuditFinding(AuditFindingKind.FileDataLocation, @"C:\ProgramData\Vendor\App\cache.db",
                "MSI installs into ProgramData.", "File table row F3", AuditConfidence.High),
        ]);
        var hostDraft = AuditProfileSynthesizer.Synthesize(hostReport);
        var hostRules = ProfileRules.From(hostDraft.Profile);
        check("draft cleans up the leftover vendor folder", hostRules.DirectoriesCleanupIfEmpty.Contains(@"%APPDATA%\Vendor")
            && hostDraft.MappedNotes.Any(n => n.Contains("DirectoriesCleanupIfEmpty", StringComparison.Ordinal)));
        check("draft treats programdata as cleanup-only with a note", hostRules.DirectoriesCleanupIfEmpty.Contains(@"%PROGRAMDATA%\Vendor\App")
            && hostRules.DirectoryMoves.All(m => !m.HostTarget.StartsWith("%PROGRAMDATA%", StringComparison.OrdinalIgnoreCase))
            && hostDraft.MappedNotes.Any(n => n.Contains("ProgramData", StringComparison.Ordinal)));
        check("extended draft still validates and plans", PortableProfileValidator.Validate(hostDraft.Profile).All(d => d.Severity != ProfileSeverity.Error)
            && ProfilePlanner.Prepare(hostDraft.Profile, "SampleAppPortable").Plan is not null);

        var configReport = new AuditReport(AuditReport.CurrentSchemaVersion, "sample.zip", AuditSourceKind.PortableZip,
            Path.Combine(root, "payload"), "SampleApp.exe",
            [
                new AuditFinding(AuditFindingKind.HardcodedPath, @"C:\Users\Test\AppData\Roaming\Vendor\paths.ini",
                    "config hardcodes a path", "settings.ini", AuditConfidence.Medium),
                new AuditFinding(AuditFindingKind.HardcodedPath, @"D:\Custom\Place\file.dat",
                    "config hardcodes a path", "settings.ini", AuditConfidence.Medium),
            ]);
        var configDraft = AuditProfileSynthesizer.Synthesize(configReport);
        var write = ProfileRules.From(configDraft.Profile).FileWrites.Single();
        check("draft rewrites tokenizable hardcoded paths with filewrite", write.Get("Type") == "Replace"
            && write.Get("File") == @"%PAL:AppDir%\settings.ini"
            && write.Get("Find") == @"C:\Users\Test\AppData\Roaming\Vendor\paths.ini"
            && write.Get("Replace") == @"%APPDATA%\Vendor\paths.ini");
        check("draft leaves untokenizable paths unmapped", configDraft.UnmappedFindings.Count == 1
            && configDraft.UnmappedFindings[0].Subject == @"D:\Custom\Place\file.dat"
            && PortableProfileValidator.Validate(configDraft.Profile).All(d => d.Severity != ProfileSeverity.Error));

        // --- splash spec ------------------------------------------------------------------------
        check("splash spec is the standard branding bitmap", SplashSpec.Standard is { Width: 164, Height: 314 }
            && SplashSpec.Standard.FileName == "PortableApps.comInstaller.bmp"
            && SplashSpec.Standard.CanComposite("SampleApp", true)
            && !SplashSpec.Standard.CanComposite("SampleApp", false)
            && !SplashSpec.Standard.CanComposite(" ", true));

        // --- harness sandboxie leak verification (fake sandbox) ----------------------------------
        var package = Path.Combine(root, "package");
        Directory.CreateDirectory(package);
        File.WriteAllBytes(Path.Combine(package, "Demo.exe"), [0x4D, 0x5A]);
        var cleanRun = await TestHarness.RunSandboxieAsync(
            new TestRunRequest("SampleApp", TestRunMode.Portable, package, Path.Combine(root, "clean-report.json")),
            new FakeSandbox(leak: false), CancellationToken.None);
        check("sandboxie leak test passes a contained app and records the backend", cleanRun.Report.Verdict == TestVerdict.Pass
            && cleanRun.Report.ObservationBackend == "sandboxie");
        var leakyRun = await TestHarness.RunSandboxieAsync(
            new TestRunRequest("SampleApp", TestRunMode.Portable, package, Path.Combine(root, "leaky-report.json")),
            new FakeSandbox(leak: true), CancellationToken.None);
        check("sandboxie leak test fails on an escaped write", leakyRun.Report.Verdict == TestVerdict.Fail
            && leakyRun.Report.Violations.Any(w => w.Category == WriteCategory.ProfileLeakage));

        // --- cli flags ----------------------------------------------------------------------------
        check("cli parses the new audit flags", CliArguments.Parse(["audit", "setup.exe", "--runtime", "--sandboxie"]).Has("sandboxie")
            && CliArguments.Parse(["audit", "setup.exe", "--engines"]).Has("engines")
            && CliArguments.Parse(["test", "pkg", "--sandboxie"]).Has("sandboxie"));
    }

    /// <summary>Materializes a two-file fixture payload instead of running a real engine.</summary>
    private sealed class FakeExtractionRunner(bool succeed) : IExtractionRunner
    {
        public Task<bool> ExtractAsync(ExtractionEngineKind engine, string executablePath, string sourcePath, string destinationDirectory, CancellationToken token)
        {
            if (!succeed) return Task.FromResult(false);
            Directory.CreateDirectory(Path.Combine(destinationDirectory, "data"));
            File.WriteAllBytes(Path.Combine(destinationDirectory, "SampleApp.exe"), [0x4D, 0x5A]);
            File.WriteAllText(Path.Combine(destinationDirectory, "data", "records.db"), "db");
            return Task.FromResult(true);
        }
    }

    /// <summary>
    /// A fake Sandboxie box: runs nothing; in leak mode it writes into the
    /// harness's redirected profile (the sibling of the package copy),
    /// simulating an escape the verdict must catch.
    /// </summary>
    private sealed class FakeSandbox(bool leak) : ISandboxProvider
    {
        public bool IsAvailable => true;

        public Task<SandboxRunOutcome> RunAsync(SandboxieBoxSpec spec, string executable, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken token)
        {
            if (leak)
            {
                var profile = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(executable)!, "..", "profile", "AppData", "Roaming", "Escaped"));
                Directory.CreateDirectory(profile);
                File.WriteAllText(Path.Combine(profile, "leak.txt"), "escaped the box");
            }
            return Task.FromResult(new SandboxRunOutcome(0, false, TimeSpan.FromMilliseconds(5)));
        }

        public IReadOnlyList<string> HarvestFiles(SandboxieBoxSpec spec) => [];

        public RegistrySnapshot CaptureBoxRegistry(SandboxieBoxSpec spec, CancellationToken token) => RegistrySnapshot.Empty;
    }
}
