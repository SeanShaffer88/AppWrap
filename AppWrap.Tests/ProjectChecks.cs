using System.IO;
using System.Text.Json;
using AppWrap.Core;
using AppWrap.Core.Projects;

internal static class ProjectChecks
{
    public static void Run(string evidence, Action<string, bool> check, Action<string, Action> reject)
    {
        var root = Path.Combine(evidence, "projects");
        Directory.CreateDirectory(root);

        // --- Round-trip: every modelled field plus unknown properties survive. ---
        var created = DateTimeOffset.Parse("2026-01-02T03:04:05Z");
        var project = new PafProject
        {
            Name = "Meadow",
            PafSpecVersion = "3.9",
            CreatedUtc = created,
            SourceSnapshotHash = new string('a', 64),
            SourceSnapshotTruncated = false,
            BuildModeHistory = [new BuildModeRecord("PortableApps", created), new BuildModeRecord("Standard", created.AddDays(1))],
            UserChoices = new Dictionary<string, string> { ["namingStyle"] = "NameSetupVersion" },
            ExtensionData = new Dictionary<string, JsonElement>
            {
                ["futureToolSetting"] = JsonDocument.Parse("{\"nested\":{\"level\":3}}").RootElement.Clone(),
                ["note"] = JsonDocument.Parse("\"kept verbatim\"").RootElement.Clone(),
            },
        };
        var json = project.ToJson();
        var roundTripped = PafProject.FromJson(json);
        check("project round-trip identity", roundTripped.Name == "Meadow" && roundTripped.PafSpecVersion == "3.9" && roundTripped.CreatedUtc == created);
        check("project round-trip snapshot and history", roundTripped.SourceSnapshotHash == new string('a', 64) && !roundTripped.SourceSnapshotTruncated
            && roundTripped.BuildModeHistory.Count == 2 && roundTripped.BuildModeHistory[0].Mode == "PortableApps" && roundTripped.BuildModeHistory[1].Mode == "Standard");
        check("project round-trip user choices", roundTripped.UserChoices.TryGetValue("namingStyle", out var style) && style == "NameSetupVersion");
        check("project round-trip unknown properties kept", roundTripped.ExtensionData is not null
            && roundTripped.ExtensionData.TryGetValue("futureToolSetting", out var future) && future.GetProperty("nested").GetProperty("level").GetInt32() == 3
            && roundTripped.ExtensionData.TryGetValue("note", out var note) && note.GetString() == "kept verbatim");
        check("project round-trip serialization stable", roundTripped.ToJson() == json && json.Contains("\"futureToolSetting\"", StringComparison.Ordinal));

        // --- Store: save, load, absent. ---
        var storeDir = Path.Combine(root, "store"); Directory.CreateDirectory(storeDir);
        check("project load absent is null", ProjectStore.Load(storeDir) is null);
        ProjectStore.Save(storeDir, project);
        check("project file written at root", File.Exists(Path.Combine(storeDir, "appwrap.project.json")));
        var loaded = ProjectStore.Load(storeDir);
        check("project store round-trip", loaded is not null && loaded.Name == "Meadow" && loaded.BuildModeHistory.Count == 2
            && loaded.ExtensionData is not null && loaded.ExtensionData.ContainsKey("futureToolSetting"));

        // --- Migration: a real v1 document migrates forward without loss. ---
        const string v1Json = """
            {
              "schemaVersion": 1,
              "name": "Legacy App",
              "pafSpecVersion": "3.5",
              "createdUtc": "2025-06-07T08:09:10+00:00",
              "legacyNote": "written by schema v1"
            }
            """;
        var migrated = PafProject.FromJson(v1Json);
        check("project v1 migrates to current schema", migrated.SchemaVersion == PafProject.CurrentSchemaVersion);
        check("project v1 fields preserved", migrated.Name == "Legacy App" && migrated.PafSpecVersion == "3.5"
            && migrated.CreatedUtc == DateTimeOffset.Parse("2025-06-07T08:09:10+00:00"));
        check("project v1 new fields defaulted", migrated.SourceSnapshotHash is null && !migrated.SourceSnapshotTruncated
            && migrated.BuildModeHistory.Count == 0 && migrated.UserChoices.Count == 0);
        check("project v1 unknown property preserved", migrated.ExtensionData is not null
            && migrated.ExtensionData.TryGetValue("legacyNote", out var legacy) && legacy.GetString() == "written by schema v1"
            && migrated.ToJson().Contains("\"legacyNote\"", StringComparison.Ordinal));
        reject("project future schema rejected", () => PafProject.FromJson("{\"schemaVersion\": 99, \"name\": \"x\"}"));
        reject("project missing schema rejected", () => PafProject.FromJson("{\"name\": \"x\"}"));
        reject("project malformed json rejected", () => PafProject.FromJson("{not json"));

        // --- Snapshot: deterministic, bounded, project file excluded. ---
        var snapDir = Path.Combine(root, "snapshot"); Directory.CreateDirectory(Path.Combine(snapDir, "bin"));
        File.WriteAllText(Path.Combine(snapDir, "bin", "Demo.exe"), "fixture bytes");
        File.WriteAllText(Path.Combine(snapDir, "readme.txt"), "hello");
        var first = SourceSnapshot.Compute(snapDir);
        var second = SourceSnapshot.Compute(snapDir);
        check("snapshot deterministic and in bounds", first.Sha256 is not null && first.Sha256 == second.Sha256 && !first.Truncated && first.FileCount == 2);
        File.WriteAllText(Path.Combine(snapDir, "appwrap.project.json"), "{\"schemaVersion\":2}");
        check("snapshot excludes root project file", SourceSnapshot.Compute(snapDir).Sha256 == first.Sha256);
        File.WriteAllText(Path.Combine(snapDir, "bin", "appwrap.project.json"), "{}");
        var withNested = SourceSnapshot.Compute(snapDir);
        check("snapshot includes nested project-named file", withNested.Sha256 != first.Sha256 && withNested.FileCount == 3);
        File.WriteAllText(Path.Combine(snapDir, "readme.txt"), "changed");
        check("snapshot changes with content", SourceSnapshot.Compute(snapDir).Sha256 != withNested.Sha256);

        // --- Inference from bare folders. ---
        var bareDir = Path.Combine(root, "bare"); Directory.CreateDirectory(bareDir);
        File.WriteAllText(Path.Combine(bareDir, "Bare.exe"), "fixture");
        var inferredProject = ProjectStore.LoadOrInfer(bareDir, out var wasInferred);
        check("project inferred from bare folder", wasInferred && inferredProject.Name == "bare"
            && inferredProject.PafSpecVersion == PafLayout.DefaultPafSpecVersion && inferredProject.SourceSnapshotHash is not null);
        ProjectStore.Save(bareDir, inferredProject);
        var reopened = ProjectStore.LoadOrInfer(bareDir, out var wasInferredAgain);
        check("project reopened not re-inferred", !wasInferredAgain && reopened.Name == "bare");
        var pafDir = Path.Combine(root, "pafshaped"); Directory.CreateDirectory(Path.Combine(pafDir, "App", "AppInfo"));
        File.WriteAllText(Path.Combine(pafDir, "App", "AppInfo", "appinfo.ini"),
            "[Format]\nType=PortableApps.comFormat\nVersion=3.5\n[Details]\nName=Shaped App\nAppID=ShapedPortable\n");
        var shaped = ProjectStore.Infer(pafDir);
        check("project inference reads existing appinfo", shaped.Name == "Shaped App" && shaped.PafSpecVersion == "3.5");

        // --- RecordBuild appends history and persists. ---
        ProjectStore.RecordBuild(bareDir, reopened, OutputMode.PortableApps);
        var afterBuild = ProjectStore.Load(bareDir);
        check("project build history recorded", afterBuild is not null && afterBuild.BuildModeHistory.Count == 1
            && afterBuild.BuildModeHistory[0].Mode == "PortableApps" && afterBuild.SourceSnapshotHash is not null);

        // --- Validator: deliberately broken appinfo yields field-level errors. ---
        var brokenDir = Path.Combine(root, "broken"); Directory.CreateDirectory(Path.Combine(brokenDir, "App", "AppInfo"));
        File.WriteAllText(Path.Combine(brokenDir, "App", "AppInfo", "appinfo.ini"),
            "[Format]\nType=PortableApps.comFormat\nVersion=3.9\n" +
            "[Details]\nName=Broken App\nPublisher=Someone\nCategory=Utilities\nDescription=Broken fixture\nHomepage=https://example.invalid/\n" +
            "[Version]\nPackageVersion=1.x\nDisplayVersion=1.x\n" +
            "[Control]\nIcons=1\n");
        var broken = PafValidator.Validate(brokenDir);
        PafDiagnostic? Find(string code) => broken.FirstOrDefault(d => d.Code == code);
        var missingAppId = Find("MissingAppId");
        check("validator missing AppID is field-level", missingAppId is { Severity: PafDiagnosticSeverity.Error, FilePath: "App/AppInfo/appinfo.ini", Section: "Details", Key: "AppID" });
        var badVersion = Find("InvalidPackageVersion");
        check("validator bad PackageVersion is field-level", badVersion is { Severity: PafDiagnosticSeverity.Error, FilePath: "App/AppInfo/appinfo.ini", Section: "Version", Key: "PackageVersion" });
        var missingStart = Find("MissingStart");
        check("validator missing Start is field-level", missingStart is { Severity: PafDiagnosticSeverity.Error, FilePath: "App/AppInfo/appinfo.ini", Section: "Control", Key: "Start" });
        check("validator diagnostics always carry message and hint", broken.All(d => d.Message.Length > 0 && d.Hint.Length > 0 && d.FilePath.Length > 0 && d.Code.Length > 0));

        // --- Validator: a complete fixture produces no errors. ---
        var cleanDir = Path.Combine(root, "clean");
        Directory.CreateDirectory(Path.Combine(cleanDir, "App", "AppInfo", "Launcher"));
        Directory.CreateDirectory(Path.Combine(cleanDir, "Data"));
        Directory.CreateDirectory(Path.Combine(cleanDir, "Other"));
        File.WriteAllText(Path.Combine(cleanDir, "App", "AppInfo", "appinfo.ini"),
            "[Format]\nType=PortableApps.comFormat\nVersion=3.9\n" +
            "[Details]\nName=Clean App\nAppID=CleanPortable\nPublisher=Someone\nCategory=Utilities\nDescription=Clean fixture\nHomepage=https://example.invalid/\nLanguage=English\n" +
            "[License]\nShareable=true\nOpenSource=true\nFreeware=true\nCommercialUse=true\n" +
            "[Version]\nPackageVersion=1.2.0.0\nDisplayVersion=1.2\n" +
            "[Control]\nIcons=1\nStart=CleanPortable.exe\n");
        File.WriteAllText(Path.Combine(cleanDir, "App", "AppInfo", "Launcher", "CleanPortable.ini"),
            "[Launch]\nProgramExecutable=Clean.exe\nWaitForProgram=true\n");
        var clean = PafValidator.Validate(cleanDir, new PafProject { Name = "Clean App" });
        check("validator clean fixture has no errors", clean.All(d => d.Severity != PafDiagnosticSeverity.Error));

        // --- Validator: source installer hooks are flagged before packaging rejects them. ---
        var hookDir = Path.Combine(root, "hooks"); Directory.CreateDirectory(Path.Combine(hookDir, "Other", "Source"));
        File.WriteAllText(Path.Combine(hookDir, "Other", "Source", "PortableApps.comInstallerCustom.nsh"), "!macro CustomCodePreInstall\n!macroend\n");
        var hooks = PafValidator.Validate(hookDir);
        check("validator flags source installer hook", hooks.Any(d => d.Code == "SourceInstallerHook" && d.Severity == PafDiagnosticSeverity.Warning
            && d.FilePath == "Other/Source/PortableApps.comInstallerCustom.nsh"));

        // --- Payload exclusion: the shared predicate BuildService.Snapshot applies. ---
        var payloadDir = Path.Combine(root, "payload"); Directory.CreateDirectory(Path.Combine(payloadDir, "sub"));
        File.WriteAllText(Path.Combine(payloadDir, "app.exe"), "fixture");
        File.WriteAllText(Path.Combine(payloadDir, "sub", "data.bin"), "fixture");
        File.WriteAllText(Path.Combine(payloadDir, "sub", "appwrap.project.json"), "{}");
        var withoutProjectFile = PayloadList(payloadDir);
        File.WriteAllText(Path.Combine(payloadDir, "appwrap.project.json"), "{\"schemaVersion\":2}");
        var withProjectFile = PayloadList(payloadDir);
        check("payload list identical with and without root project file", withoutProjectFile.SequenceEqual(withProjectFile));
        check("payload keeps nested project-named file", withProjectFile.Contains("sub/appwrap.project.json") && withProjectFile.Count == 3);
        check("root project file predicate", ProjectStore.IsRootProjectFile(payloadDir, Path.Combine(payloadDir, "appwrap.project.json"))
            && !ProjectStore.IsRootProjectFile(payloadDir, Path.Combine(payloadDir, "sub", "appwrap.project.json"))
            && !ProjectStore.IsRootProjectFile(payloadDir, Path.Combine(payloadDir, "app.exe")));

        // Mirrors BuildService.Snapshot's enumeration: every entry is copied except the
        // root project file, which the shared ProjectStore predicate identifies.
        static List<string> PayloadList(string dir)
        {
            var result = new List<string>();
            Walk(dir, 0);
            void Walk(string from, int depth)
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(from))
                {
                    if (depth == 0 && !Directory.Exists(entry) && ProjectStore.IsProjectFileName(Path.GetFileName(entry))) continue;
                    if (Directory.Exists(entry)) Walk(entry, depth + 1);
                    else result.Add(PafLayout.ToDisplayPath(Path.GetRelativePath(dir, entry)));
                }
            }
            result.Sort(StringComparer.Ordinal);
            return result;
        }
    }
}
