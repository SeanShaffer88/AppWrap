using System.IO;
using System.IO.Compression;
using AppWrap.Core;
using AppWrap.Core.Audit;
using AppWrap.Core.PortableProfiles;
using AppWrap.Core.TestHarness;

/// <summary>
/// Portable-conversion audit checks (Core only, so they run in the Linux
/// harness): .reg parsing, MSI fixture classification, zip inventory and
/// extraction safety, exe family limits, registry snapshot diffing and
/// classification, runtime-finding conversion, and draft profile synthesis
/// validated by the real profile validator/planner.
/// </summary>
public static class AuditChecks
{
    public static void Run(string evidence, Action<string, bool> check, Action<string, Action> reject)
    {
        var root = Path.Combine(evidence, "audit");
        Directory.CreateDirectory(root);

        // --- .reg parsing ---------------------------------------------------
        var reg = RegFileParser.Parse(
            "Windows Registry Editor Version 5.00\r\n" +
            "[HKEY_CURRENT_USER\\Software\\SampleApp]\r\n" +
            "\"InstallDir\"=\"C:\\\\SampleApp\"\r\n" +
            "\"Count\"=dword:00000003\r\n" +
            "@=\"default text\"\r\n" +
            "[-HKEY_CURRENT_USER\\Software\\SampleApp\\Old]\r\n" +
            "[HKEY_LOCAL_MACHINE\\Software\\SampleApp]\r\n" +
            "\"Blob\"=hex:01,02,\\\r\n  03\r\n");
        check("reg parser reads values with normalized hives", reg.Values.Count == 4
            && reg.Values.Any(v => v.KeyPath == @"HKCU\Software\SampleApp" && v.ValueName == "InstallDir" && v.Data == @"C:\SampleApp")
            && reg.Values.Any(v => v.ValueName == "" && v.Data == "default text")
            && reg.Values.Any(v => v.KeyPath == @"HKLM\Software\SampleApp" && v.Data.StartsWith("hex:01,02,03", StringComparison.Ordinal)));
        check("reg parser records key deletions", reg.Deletions.Count == 1 && reg.Deletions[0].KeyPath == @"HKCU\Software\SampleApp\Old");

        // --- MSI fixture classification --------------------------------------
        var tables = new MsiTables(
            Files:
            [
                new MsiFileRow("F1", "C1", "SampleApp.exe", "INSTALLDIR"),
                new MsiFileRow("F2", "C2", "settings.db", "RoamingDir"),
                new MsiFileRow("F3", "C3", "driver.sys", "INSTALLDIR"),
            ],
            Registry:
            [
                new MsiRegistryRow("R1", 1, @"Software\SampleApp", "InstallDir", @"C:\SampleApp", "C1"),
                new MsiRegistryRow("R2", 2, @"Software\SampleApp", null, null, "C1"),
            ],
            Shortcuts: [new MsiShortcutRow("S1", "DesktopDir", "SampleApp", "C1", "SampleApp.exe")],
            Directories:
            [
                new MsiDirectoryRow("TARGETDIR", null, "SourceDir"),
                new MsiDirectoryRow("INSTALLDIR", "TARGETDIR", "SampleApp"),
                new MsiDirectoryRow("AppDataDir", "TARGETDIR", "AppData"),
                new MsiDirectoryRow("RoamingDir", "AppDataDir", "Roaming"),
            ],
            Services: [new MsiServiceRow("SV1", "SampleSvc", "Sample Service", "C9")]);
        var fixtureJson = FixtureMsiReader.ToJson(tables);
        var msiPath = Path.Combine(root, "sample.msi");
        File.WriteAllBytes(msiPath, [0xD0, 0xCF, 0x11, 0xE0]);
        var msiReport = StaticAuditor.AuditMsi(msiPath, FixtureMsiReader.FromJson(fixtureJson));
        check("msi fixture round-trips through json", FixtureMsiReader.FromJson(fixtureJson).Read(msiPath).Registry.Count == 2);
        check("msi registry rows become registry findings", msiReport.Findings.Count(f => f.Kind == AuditFindingKind.RegistryWrite) == 2
            && msiReport.Findings.Any(f => f.RegistryKeyPath == @"HKCU\Software\SampleApp" && f.RegistryValueName == "InstallDir")
            && msiReport.Findings.Any(f => f.Evidence.Contains("Registry table row R1", StringComparison.Ordinal)));
        check("msi service and driver rows are hard warnings", msiReport.Findings.Count(f => f.Kind == AuditFindingKind.ServiceOrDriver && f.HardWarning) == 2);
        check("msi appdata file is a data location", msiReport.Findings.Any(f => f.Kind == AuditFindingKind.FileDataLocation
            && f.Subject.Replace('/', '\\').EndsWith(@"Roaming\settings.db", StringComparison.OrdinalIgnoreCase)));
        check("msi shortcut row becomes a shortcut finding", msiReport.OfKind(AuditFindingKind.Shortcut).Count() == 1);

        // --- zip audit ---------------------------------------------------------
        var payload = Path.Combine(root, "payload-src");
        Directory.CreateDirectory(Path.Combine(payload, "data"));
        File.WriteAllBytes(Path.Combine(payload, "SampleApp.exe"), [0x4D, 0x5A]);
        File.WriteAllText(Path.Combine(payload, "settings.ini"), "[app]\nHome=C:\\Users\\Test\\SampleApp\n");
        File.WriteAllText(Path.Combine(payload, "data", "records.db"), "db");
        File.WriteAllText(Path.Combine(payload, "register.reg"),
            "Windows Registry Editor Version 5.00\n[HKEY_CURRENT_USER\\Software\\SampleApp]\n\"Theme\"=\"dark\"\n");
        var zipPath = Path.Combine(root, "sample.zip");
        ZipFile.CreateFromDirectory(payload, zipPath);
        var zipReport = StaticAuditor.AuditZip(zipPath, Path.Combine(root, "scratch"));
        check("zip audit inventories the payload", zipReport.PayloadFolder is not null && Directory.Exists(zipReport.PayloadFolder)
            && zipReport.MainExecutable == "SampleApp.exe");
        check("zip audit flags root config and data folder", zipReport.OfKind(AuditFindingKind.FileDataLocation)
            .Any(f => f.Subject == "settings.ini") && zipReport.OfKind(AuditFindingKind.FileDataLocation).Any(f => f.Subject == "data"));
        check("zip audit parses embedded .reg files", zipReport.OfKind(AuditFindingKind.RegistryWrite)
            .Any(f => f.RegistryKeyPath == @"HKCU\Software\SampleApp"));
        check("zip audit flags hardcoded paths in config", zipReport.OfKind(AuditFindingKind.HardcodedPath)
            .Any(f => f.Subject.StartsWith(@"C:\Users", StringComparison.Ordinal)));

        var evilZip = Path.Combine(root, "evil.zip");
        using (var archive = ZipFile.Open(evilZip, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("../evil.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("evil");
        }
        reject("zip audit rejects traversal entries", () => StaticAuditor.AuditZip(evilZip, Path.Combine(root, "evil-scratch")));

        // --- exe family --------------------------------------------------------
        var exePath = Path.Combine(root, "setup.exe");
        File.WriteAllText(exePath, "MZ...... Inno Setup ......");
        check("exe family detection reads inno signature", StaticAuditor.DetectExeFamily(exePath) == StaticAuditor.InstallerFamily.InnoSetup);
        var exeReport = StaticAuditor.AuditExe(exePath);
        check("exe static audit states its limit instead of guessing", exeReport.OfKind(AuditFindingKind.StaticAuditLimited).Count() == 1
            && exeReport.Findings.All(f => f.Kind != AuditFindingKind.RegistryWrite));

        // --- registry snapshot diff ---------------------------------------------
        RegistrySnapshot Snap(params (string Key, string Name, string Kind, string Data)[] values)
        {
            var map = new Dictionary<string, IReadOnlyDictionary<string, RegistryValueSnapshot>>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in values.GroupBy(v => v.Key))
                map[group.Key] = group.ToDictionary(v => v.Name, v => new RegistryValueSnapshot(v.Name, v.Kind, v.Data), StringComparer.OrdinalIgnoreCase);
            return new RegistrySnapshot(map);
        }
        var before = Snap((@"HKCU\Software\SampleApp", "Theme", "REG_SZ", "light"));
        var after = Snap(
            (@"HKCU\Software\SampleApp", "Theme", "REG_SZ", "dark"),
            (@"HKCU\Software\SampleApp", "NewKey", "REG_DWORD", "1"),
            (@"HKCU\Software\PortableApps.com\Keys\SampleApp", "Backup", "REG_SZ", "x"),
            (@"HKLM\Software\SampleApp", "Machine", "REG_SZ", "y"));
        var diff = before.Diff(after);
        check("registry diff finds created and modified values", diff.Any(c => c.Change == RegistryChangeKind.ValueModified && c.ValueName == "Theme" && c.NewData == "dark")
            && diff.Any(c => c.Change == RegistryChangeKind.ValueCreated && c.ValueName == "NewKey" && c.ValueKind == "REG_DWORD"));
        check("registry classification separates backup, leakage and machine-wide",
            diff.Single(c => c.ValueName == "Backup").Category == RegistryChangeCategory.LauncherBackup
            && !diff.Single(c => c.ValueName == "Backup").Violation
            && diff.Single(c => c.ValueName == "Theme").Category == RegistryChangeCategory.HostLeakage
            && diff.Single(c => c.ValueName == "Machine").Category == RegistryChangeCategory.MachineWide);
        check("registry verdict fails on violations only", WriteClassifier.Verdict([], diff, false, 0) == TestVerdict.Fail
            && WriteClassifier.Verdict([], diff.Where(c => !c.Violation).ToList(), false, 0) == TestVerdict.Pass);

        // --- runtime findings + synthesis ---------------------------------------
        var runtimeReport = new TestReport(TestReport.CurrentSchemaVersion, "SampleApp", TestRunMode.Portable, "\"SampleApp.exe\"",
            DateTimeOffset.UtcNow, 1.0, 0, false,
            [new DetectedWrite(WatchedRootKind.Profile, "AppData/Roaming/SampleApp/settings.ini", WriteChange.Created, WriteCategory.ProfileLeakage, true)],
            [], TestVerdict.Fail, true, diff, diff.Where(c => c.Violation).ToList());
        var runtimeFindings = RuntimeFindings.FromTestReport(runtimeReport);
        check("runtime findings convert profile leakage and registry writes", runtimeFindings.Any(f => f.Kind == AuditFindingKind.FileDataLocation)
            && runtimeFindings.Any(f => f.Kind == AuditFindingKind.RegistryWrite && f.RegistryKeyPath == @"HKCU\Software\SampleApp"));

        var combined = RuntimeFindings.Merge(zipReport, runtimeFindings.Concat(RuntimeFindings.FromRegistryChanges(diff)));
        var draft = AuditProfileSynthesizer.Synthesize(combined);
        var diagnostics = PortableProfileValidator.Validate(draft.Profile);
        check("synthesized draft passes the profile validator", diagnostics.All(d => d.Severity != ProfileSeverity.Error));
        var rules = ProfileRules.From(draft.Profile);
        check("draft maps registry keys to save/restore rules", rules.RegistryKeys.Any(r => r.HiveKey == @"HKCU\Software\SampleApp")
            && draft.Profile.Launcher.GetValue("Activate", "Registry") == "true");
        check("draft maps runtime string values to covered value writes", rules.RegistryValueWrites.Any(w => w.KeyPath == @"HKCU\Software\SampleApp" && w.ValueName == "Theme" && w.Data == "dark"));
        check("draft maps appdata leakage to a directory move", rules.DirectoryMoves.Any(m => m.HostTarget == @"%APPDATA%\SampleApp"));
        check("draft demotes the overlapping payload file move with a note", rules.FileMoves.All(m => m.PortablePath != "settings.ini")
            && draft.MappedNotes.Any(n => n.Contains("settings.ini", StringComparison.Ordinal)));
        var fileOnlyDraft = AuditProfileSynthesizer.Synthesize(new AuditReport(AuditReport.CurrentSchemaVersion, zipPath,
            AuditSourceKind.PortableZip, zipReport.PayloadFolder, "SampleApp.exe",
            [new AuditFinding(AuditFindingKind.FileDataLocation, "settings.ini", "config at root", "payload file name", AuditConfidence.Medium)]));
        check("draft maps payload config to a file move when nothing overlaps",
            ProfileRules.From(fileOnlyDraft.Profile).FileMoves.Any(m => m.PortablePath == "settings.ini" && m.HostTarget == "%PAL:AppDir%"));
        check("draft launch executable comes from the audit", rules.Launch.ProgramExecutable == "SampleApp.exe");
        check("draft grants no trust", draft.Profile.TrustedExtensions.Count == 0 && !draft.Profile.ForceCleanupAcknowledged
            && draft.Profile.ExecutionKind == PortableExecutionKind.Pal);
        var msiDraft = AuditProfileSynthesizer.Synthesize(msiReport);
        check("msi draft keeps services unmapped, never faked into rules", msiDraft.UnmappedFindings.Any(f => f.Kind == AuditFindingKind.ServiceOrDriver)
            && PortableProfileValidator.Validate(msiDraft.Profile).All(d => d.Severity != ProfileSeverity.Error));
        var plan = ProfilePlanner.Prepare(draft.Profile, "SampleAppPortable");
        check("draft produces a planner plan", plan.Plan is not null);
    }
}
