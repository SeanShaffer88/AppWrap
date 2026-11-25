using AppWrap.Core.PortableProfiles;

/// <summary>
/// P5 checks: the portable-profile workspace state behind the WPF editor —
/// categories and columns, row derivation from the typed views, paging,
/// structured editing (including sequential renumbering and '-' discard rows),
/// live text roundtrip, blocked-family display, profile file export/import and
/// the rule that imported trust is never granted. Pure configuration logic.
/// </summary>
internal static class WorkspaceChecks
{
    private const string Launcher =
        "[Launch]\n" +
        "ProgramExecutable=Payload\\bin\\MyApp.exe\n" +
        "CommandLineArguments=--portable\n" +
        "WaitForEXE1=helper.exe\n" +
        "WaitForEXE2=second.exe\n" +
        "\n" +
        "[Activate]\n" +
        "Registry=true\n" +
        "\n" +
        "[Environment]\n" +
        "AYE=1\n" +
        "BEE=2\n" +
        "\n" +
        "[RegistryKeys]\n" +
        "AppSettings=HKCU\\Software\\MyApp\n" +
        "\n" +
        "[RegistryCleanupIfEmpty]\n" +
        "1=HKCU\\Software\\First\n" +
        "2=HKCU\\Software\\Second\n" +
        "3=HKCU\\Software\\Third\n" +
        "\n" +
        "[FilesMove]\n" +
        "settings\\settings.ini=%APPDATA%\\MyAppFiles\n" +
        "\n" +
        "[DirectoriesMove]\n" +
        "MyAppData=%APPDATA%\\MyApp\\Data\n" +
        "-=%LOCALAPPDATA%\\MyAppScratch\n" +
        "\n" +
        "[FileWrite1]\n" +
        "Type=INI\n" +
        "File=%PAL:DataDir%\\settings\\settings.ini\n" +
        "Key=Portable\n" +
        "Value=true\n";

    private const string Installer =
        "[DirectoriesToPreserve]\n" +
        "PreserveDirectory1=App\\Docs\n" +
        "\n" +
        "[DownloadFiles]\n" +
        "DownloadURL=https://example.com/app.zip\n" +
        "DownloadSHA256=0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\n";

    private const string Metadata =
        "[Details]\n" +
        "Name=My App\n" +
        "AppID=OtherPortable\n" +
        "Description=A test app\n";

    private static ProfileWorkspaceState PalState()
    {
        var state = new ProfileWorkspaceState();
        state.LoadProfile(PortableProfileCodec.FromIniTexts(Launcher, Installer, Metadata, null, PortableExecutionKind.Pal));
        return state;
    }

    private static ProfileRuleRow Row(ProfileWorkspaceState state, ProfileCategory category, string key) =>
        state.Rows(category).Single(r => r.Key == key);

    public static void Run(Action<string, bool> check, Action<string, Action> reject)
    {
        ModeAndSession(check);
        CategoriesAndRows(check);
        Paging(check);
        Editing(check, reject);
        TextRoundtrip(check);
        BlockedAndDiagnostics(check);
        Extensions(check, reject);
        ProfileFiles(check, reject);
        Presets(check);
    }

    private static void ModeAndSession(Action<string, bool> check)
    {
        var state = new ProfileWorkspaceState();
        check("workspace starts as already-portable", state.Mode == PortableExecutionKind.PackagingOnly
            && state.EffectiveProfile is null && !state.ProfileApplies && !state.HasErrors);
        state.SetMode(PortableExecutionKind.Pal);
        check("workspace configure mode applies the profile", state.ProfileApplies && state.EffectiveProfile is not null);
        var rowId = state.AddEntry(PortableDocumentKind.Launcher, "FilesMove", "a.ini", "%APPDATA%\\App");
        check("workspace add returns a row", rowId.Length > 0 && state.Rows(ProfileCategory.DataFilesFolders).Count == 1);
        state.SetMode(PortableExecutionKind.PackagingOnly);
        check("workspace mode switch keeps the edited profile", !state.ProfileApplies && state.EffectiveProfile is null
            && state.HasWorkingEdits && state.WorkingProfile.Launcher.GetValue("FilesMove", "a.ini") == "%APPDATA%\\App");
        state.SetMode(PortableExecutionKind.Pal);
        check("workspace profile applies again after switching back", state.EffectiveProfile is not null
            && state.Rows(ProfileCategory.DataFilesFolders).Count == 1);
        state.NewProfile();
        check("workspace new profile is empty and configuring", state.Mode == PortableExecutionKind.Pal
            && !state.HasWorkingEdits && state.Rows(ProfileCategory.OverviewLaunch).Count == 0);
    }

    private static void CategoriesAndRows(Action<string, bool> check)
    {
        check("workspace has the ten contract categories", ProfileWorkspaceState.Categories.Select(c => c.Title).SequenceEqual(
        [
            "Overview/Launch", "Data files & folders", "Registry", "Config & path updates", "Environment & language",
            "Processes & compatibility", "Install/upgrade rules", "Package details & distribution",
            "Configuration text", "Trusted extensions",
        ]));
        check("workspace runtime columns are the contract set", ProfileWorkspaceState.Categories[0].Columns.SequenceEqual(
            ["When", "Operation", "Host location", "Portable location", "Existing host data", "After exit", "Risk"]));
        check("workspace installer columns are the contract set", ProfileWorkspaceState.Categories[6].Columns.SequenceEqual(
            ["On upgrade", "Inside package path", "Preserve/remove", "Condition", "Data impact"]));

        var state = PalState();
        var overview = state.Rows(ProfileCategory.OverviewLaunch);
        check("workspace overview holds launch and activate rows", overview.Any(r => r.Key == "ProgramExecutable")
            && overview.Any(r => r.Key == "CommandLineArguments") && overview.Any(r => r.Section == "Activate"));
        var program = Row(state, ProfileCategory.OverviewLaunch, "ProgramExecutable");
        check("workspace program row cells", program.Cells[1] == "Run this program" && program.Cells[3] == "Payload\\bin\\MyApp.exe"
            && program.Cells.Count == 7 && program.CanEditValue && !program.CanEditKey);

        var data = state.Rows(ProfileCategory.DataFilesFolders);
        var fileMove = data.Single(r => r.Section == "FilesMove");
        check("workspace file move cells", fileMove.Cells[2] == "%APPDATA%\\MyAppFiles" && fileMove.Cells[3] == "settings\\settings.ini"
            && fileMove.Cells[5] == "Host copy restored" && fileMove.CanEditKey && fileMove.CanReorder);
        var discard = data.Single(r => r.Section == "DirectoriesMove" && r.Key == "-");
        check("workspace discard move row", discard.Cells[1].Contains("discard", StringComparison.OrdinalIgnoreCase)
            && discard.Cells[3].Contains("discarded", StringComparison.OrdinalIgnoreCase));

        var registry = Row(state, ProfileCategory.Registry, "AppSettings");
        check("workspace registry row cells", registry.Cells[2] == "HKCU\\Software\\MyApp"
            && registry.Cells[3] == "Data\\settings\\AppSettings.reg" && registry.Cells[5] == "Host key restored");

        var config = state.Rows(ProfileCategory.ConfigPathUpdates);
        check("workspace FileWrite fields are rows", config.Count == 4 && config.All(r => r.Section == "FileWrite1")
            && config.Single(r => r.Key == "File").Cells[3] == "%PAL:DataDir%\\settings\\settings.ini");

        var environment = state.Rows(ProfileCategory.EnvironmentLanguage);
        check("workspace environment rows keep order and reorder", environment.Select(r => r.Key).SequenceEqual(["AYE", "BEE"])
            && environment.All(r => r.CanReorder && r.CanEditKey));

        var processes = state.Rows(ProfileCategory.ProcessesCompatibility);
        check("workspace WaitForEXE rows are process rows", processes.Select(r => r.Key).SequenceEqual(["WaitForEXE1", "WaitForEXE2"])
            && processes.All(r => !r.CanEditKey && r.CanReorder));

        var install = state.Rows(ProfileCategory.InstallUpgradeRules);
        var preserve = install.Single(r => r.Key == "PreserveDirectory1");
        check("workspace preserve row cells", preserve.Cells[0] == "Kept across the upgrade" && preserve.Cells[1] == "App\\Docs"
            && preserve.Cells[2] == "Preserve" && preserve.Cells.Count == 5 && !preserve.IsBlocked);

        var details = state.Rows(ProfileCategory.PackageDetails);
        check("workspace metadata rows use installer columns", details.Count == 3 && details.All(r => r.Cells.Count == 5)
            && details.All(r => r.Document == PortableDocumentKind.Metadata));
        check("workspace configuration text has no grid rows", state.Rows(ProfileCategory.ConfigurationText).Count == 0);
    }

    private static void Paging(Action<string, bool> check)
    {
        var state = PalState();
        var first = state.Page(ProfileCategory.Registry, 0, 2);
        check("workspace page slices rows", first.Rows.Count == 2 && first.TotalCount == 4 && first.PageCount == 2);
        var clamped = state.Page(ProfileCategory.Registry, 99, 2);
        check("workspace page index clamps", clamped.Rows.Count == 2 && clamped.Rows[0].Key == "2");
        var empty = state.Page(ProfileCategory.TrustedExtensions, 0, 4);
        check("workspace empty page still has one page", empty.TotalCount == 0 && empty.PageCount == 1 && empty.Rows.Count == 0);
    }

    private static void Editing(Action<string, bool> check, Action<string, Action> reject)
    {
        var state = PalState();
        var move = Row(state, ProfileCategory.DataFilesFolders, "settings\\settings.ini");
        check("workspace value edit reaches the document", state.UpdateRowValue(move.Id, "%APPDATA%\\Other")
            && state.WorkingProfile.Launcher.GetValue("FilesMove", "settings\\settings.ini") == "%APPDATA%\\Other");
        var renamed = state.UpdateRowKey(move.Id, "other\\other.ini");
        check("workspace key edit renames and returns the new id", renamed != move.Id
            && state.WorkingProfile.Launcher.GetValue("FilesMove", "other\\other.ini") == "%APPDATA%\\Other"
            && state.WorkingProfile.Launcher.GetValue("FilesMove", "settings\\settings.ini") is null);
        reject("workspace key edit rejects '=' in a key", () => state.UpdateRowKey(renamed, "a=b"));
        var program = Row(state, ProfileCategory.OverviewLaunch, "ProgramExecutable");
        check("workspace fixed keys are not renamed", state.UpdateRowKey(program.Id, "Hacked") == program.Id
            && state.WorkingProfile.Launcher.GetValue("Launch", "ProgramExecutable") is not null);

        // Sequential removal closes the gap: values shift up and keys stay 1..N.
        var middle = Row(state, ProfileCategory.Registry, "2");
        check("workspace sequential remove renumbers", state.RemoveRow(middle.Id)
            && state.WorkingProfile.Launcher.GetValues("RegistryCleanupIfEmpty", "1").SequenceEqual(["HKCU\\Software\\First"])
            && state.WorkingProfile.Launcher.GetValues("RegistryCleanupIfEmpty", "2").SequenceEqual(["HKCU\\Software\\Third"])
            && state.WorkingProfile.Launcher.GetValue("RegistryCleanupIfEmpty", "3") is null
            && !state.Diagnostics.Any(d => d.Code == "SequentialGap"));
        // Sequential reorder swaps values, keeping the numbering.
        var first = Row(state, ProfileCategory.Registry, "1");
        check("workspace sequential move swaps values", state.MoveRow(first.Id, 1)
            && state.WorkingProfile.Launcher.GetValue("RegistryCleanupIfEmpty", "1") == "HKCU\\Software\\Third"
            && state.WorkingProfile.Launcher.GetValue("RegistryCleanupIfEmpty", "2") == "HKCU\\Software\\First");
        // WaitForEXE reorder behaves the same inside [Launch].
        var wait = Row(state, ProfileCategory.ProcessesCompatibility, "WaitForEXE1");
        check("workspace WaitForEXE move swaps values", state.MoveRow(wait.Id, 1)
            && state.WorkingProfile.Launcher.GetValue("Launch", "WaitForEXE1") == "second.exe"
            && state.WorkingProfile.Launcher.GetValue("Launch", "WaitForEXE2") == "helper.exe");
        // Environment reorder swaps the entries themselves (order is semantic there).
        var aye = Row(state, ProfileCategory.EnvironmentLanguage, "AYE");
        check("workspace environment move swaps entries", state.MoveRow(aye.Id, 1)
            && state.WorkingProfile.Launcher.Entries("Environment").Select(e => e.Key).SequenceEqual(["BEE", "AYE"]));

        // Numbered installer lists renumber on remove, like launcher sequential lists.
        var preserve = Row(state, ProfileCategory.InstallUpgradeRules, "PreserveDirectory1");
        var secondId = state.AddEntry(PortableDocumentKind.Installer, "DirectoriesToPreserve", "PreserveDirectory", "App\\Second");
        check("workspace numbered add takes the next number", state.WorkingProfile.Installer.GetValue("DirectoriesToPreserve", "PreserveDirectory2") == "App\\Second"
            && secondId.Length > 0);
        check("workspace numbered remove renumbers", state.RemoveRow(preserve.Id)
            && state.WorkingProfile.Installer.GetValue("DirectoriesToPreserve", "PreserveDirectory1") == "App\\Second"
            && state.WorkingProfile.Installer.GetValue("DirectoriesToPreserve", "PreserveDirectory2") is null);

        // Adding a '-' row produces a discard (protect-local) move row.
        state.AddEntry(PortableDocumentKind.Launcher, "DirectoriesMove", "-", "%TEMP%\\Scratch");
        var discards = state.Rows(ProfileCategory.DataFilesFolders).Where(r => r.Section == "DirectoriesMove" && r.Key == "-").ToList();
        check("workspace added '-' row is a second discard row", discards.Count == 2
            && discards.Any(r => r.Value == "%TEMP%\\Scratch"));

        // FileWrite rules are added as whole numbered sections.
        var fileWrite = state.AddFileWriteRule();
        check("workspace FileWrite add creates the next section", fileWrite.Length > 0
            && state.WorkingProfile.Launcher.HasSection("FileWrite2")
            && state.WorkingProfile.Launcher.GetValue("FileWrite2", "Type") == "INI");
        var typeRow = state.Rows(ProfileCategory.ConfigPathUpdates).Single(r => r.Section == "FileWrite1" && r.Key == "Type");
        check("workspace FileWrite move swaps sections", state.MoveRow(typeRow.Id, 1)
            && state.WorkingProfile.Launcher.GetValue("FileWrite2", "Key") == "Portable"
            && state.WorkingProfile.Launcher.GetValue("FileWrite1", "File") == "");

        // A plain entry removal deletes exactly that entry.
        var bee = Row(state, ProfileCategory.EnvironmentLanguage, "BEE");
        check("workspace entry remove", state.RemoveRow(bee.Id)
            && state.WorkingProfile.Launcher.GetValue("Environment", "BEE") is null
            && state.WorkingProfile.Launcher.GetValue("Environment", "AYE") == "1");
        check("workspace default rows are addable", state.AddDefaultRow(ProfileCategory.Registry) is { Length: > 0 }
            && state.AddDefaultRow(ProfileCategory.ConfigurationText) is null);
    }

    private static void TextRoundtrip(Action<string, bool> check)
    {
        // A launcher-only state, so the blocked installer fixture cannot mask the results.
        var state = new ProfileWorkspaceState();
        state.LoadProfile(PortableProfileCodec.FromIniTexts(Launcher, "", null, null, PortableExecutionKind.Pal));
        check("workspace text is the document text", state.GetDocumentText(PortableDocumentKind.Launcher) == Launcher);
        state.SetDocumentText(PortableDocumentKind.Launcher, "[Launch]\nProgramExecutable=App\\New.exe\nBogus=1\n");
        check("workspace text edit re-parses into rows", state.Rows(ProfileCategory.OverviewLaunch)
            .Any(r => r.Key == "ProgramExecutable" && r.Value == "App\\New.exe"));
        check("workspace invalid draft is kept and diagnosed", state.GetDocumentText(PortableDocumentKind.Launcher).Contains("Bogus=1", StringComparison.Ordinal)
            && state.Diagnostics.Any(d => d.Code == "UnknownKey" && d.Severity == ProfileSeverity.Error) && state.HasErrors);
        state.SetDocumentText(PortableDocumentKind.Launcher, "[Launch]\nProgramExecutable=App\\New.exe\n");
        check("workspace corrected text clears the error", !state.HasErrors
            && state.PlanSummaryLines().Any(l => l.Contains("App\\New.exe", StringComparison.Ordinal)));
        var invalid = new ProfileWorkspaceState();
        invalid.LoadProfile(PortableProfileCodec.FromIniTexts(
            "[Launch]\nProgramExecutable=App\\A.exe\nWaitForProgram=false\n[FilesMove]\na.ini=%APPDATA%\\App\n",
            "", null, null, PortableExecutionKind.Pal));
        check("workspace plan summary falls back while invalid", invalid.Plan is null
            && invalid.PlanSummaryLines().SequenceEqual(["Fix the problems below to see what this profile will do."]));
        check("workspace metadata text starts empty", state.GetDocumentText(PortableDocumentKind.Metadata) == "");
        state.SetDocumentText(PortableDocumentKind.Metadata, Metadata);
        check("workspace metadata text creates detail rows", state.Rows(ProfileCategory.PackageDetails).Count == 3);
        state.SetDocumentText(PortableDocumentKind.Metadata, "");
        check("workspace metadata can be cleared", state.Rows(ProfileCategory.PackageDetails).Count == 0);
    }

    private static void BlockedAndDiagnostics(Action<string, bool> check)
    {
        var state = PalState();
        state.WizardAppId = "MyAppPortable";
        var downloads = state.Rows(ProfileCategory.InstallUpgradeRules).Where(r => r.Section == "DownloadFiles").ToList();
        check("workspace download rows are visibly blocked", downloads.Count == 2 && downloads.All(r => r.IsBlocked)
            && downloads.All(r => r.StatusNote.Contains("blocked", StringComparison.OrdinalIgnoreCase))
            && downloads.All(r => r.Cells[3].Contains("Blocked", StringComparison.Ordinal)));
        check("workspace blocked family is an error, not a silent pass", state.Diagnostics.Any(d =>
            d.Code == "GuardOrderBlocked" && d.Severity == ProfileSeverity.Error) && state.HasErrors);
        check("workspace metadata AppID conflict is diagnosed", state.Diagnostics.Any(d =>
            d.Code == "MetadataAppIdConflict" && d.Severity == ProfileSeverity.Error));
        state.WizardAppId = "OtherPortable";
        check("workspace matching AppID clears the conflict", !state.Diagnostics.Any(d => d.Code == "MetadataAppIdConflict"));

        var force = new ProfileWorkspaceState();
        force.LoadProfile(PortableProfileCodec.FromIniTexts(
            "[Launch]\nProgramExecutable=App\\A.exe\n[DirectoriesCleanupForce]\n1=%TEMP%\\Scratch\n", "", null, null, PortableExecutionKind.Pal));
        check("workspace force cleanup needs acknowledgment", force.HasForceCleanupEntries
            && force.Diagnostics.Any(d => d.Code == "ForceCleanupUnacknowledged" && d.Severity == ProfileSeverity.Error));
        force.ForceCleanupAcknowledged = true;
        check("workspace acknowledgment clears the diagnostic", !force.Diagnostics.Any(d => d.Code == "ForceCleanupUnacknowledged"));
    }

    private static void Extensions(Action<string, bool> check, Action<string, Action> reject)
    {
        var state = PalState();
        state.AddExtension("Other\\Source\\Custom.nsh", new string('a', 64), "Custom segment");
        check("workspace extension starts untrusted", state.Extensions.Count == 1 && !state.Extensions[0].Trusted);
        var row = state.Rows(ProfileCategory.TrustedExtensions).Single();
        check("workspace extension row shows trust state", row.Cells[2] == "Not trusted" && row.Display == "Other\\Source\\Custom.nsh" && !row.CanEditValue);
        check("workspace explicit trust action", state.SetExtensionTrusted(0, true) && state.Extensions[0].Trusted
            && state.Rows(ProfileCategory.TrustedExtensions)[0].Cells[2] == "Trusted");
        reject("workspace extension rejects a short hash", () => state.AddExtension("x.nsh", "abc", "purpose"));
        reject("workspace extension rejects an empty path", () => state.AddExtension(" ", new string('b', 64), "purpose"));
        check("workspace extension row remove", state.RemoveRow(row.Id) && state.Extensions.Count == 0);

        // A profile carrying a trusted flag into LoadProfile loses it: import is not trust.
        var carrier = PortableProfile.PackagingOnly() with
        {
            TrustedExtensions = [new TrustedExtension("a.nsh", new string('c', 64), "p", Trusted: true)],
        };
        var imported = new ProfileWorkspaceState();
        imported.LoadProfile(carrier);
        check("workspace load never grants trust", imported.Extensions.Count == 1 && !imported.Extensions[0].Trusted);
    }

    private static void ProfileFiles(Action<string, bool> check, Action<string, Action> reject)
    {
        var state = PalState();
        state.AddExtension("Other\\Source\\Custom.nsh", new string('d', 64), "Custom segment");
        state.SetExtensionTrusted(0, true);
        var text = state.ExportProfileText();
        var restored = new ProfileWorkspaceState();
        restored.ImportProfileText(text);
        check("workspace profile file roundtrips documents", restored.GetDocumentText(PortableDocumentKind.Launcher) == Launcher
            && restored.GetDocumentText(PortableDocumentKind.Installer) == Installer
            && restored.GetDocumentText(PortableDocumentKind.Metadata) == Metadata
            && restored.Mode == PortableExecutionKind.Pal);
        check("workspace profile file never restores trust", restored.Extensions.Count == 1 && !restored.Extensions[0].Trusted);
        reject("workspace profile file rejects other files", () => restored.ImportProfileText("{ not a profile"));
        reject("workspace profile file rejects wrong schema", () => restored.ImportProfileText("{\"SchemaVersion\":99}"));
        reject("workspace profile file rejects empty input", () => restored.ImportProfileText(""));
    }

    private static void Presets(Action<string, bool> check)
    {
        check("workspace presets cover the contract conveniences", ProfileWorkspaceState.Presets.Count >= 10
            && ProfileWorkspaceState.Presets.Any(p => p.Label.Contains("Roaming AppData", StringComparison.Ordinal))
            && ProfileWorkspaceState.Presets.Any(p => p.Label.Contains("Documents", StringComparison.Ordinal))
            && ProfileWorkspaceState.Presets.Any(p => p.Label.Contains("registry value", StringComparison.OrdinalIgnoreCase))
            && ProfileWorkspaceState.Presets.Any(p => p.CreatesFileWriteSection));
        var state = new ProfileWorkspaceState();
        state.SetMode(PortableExecutionKind.Pal);
        foreach (var preset in ProfileWorkspaceState.Presets.Where(p => !p.CreatesFileWriteSection))
            state.AddEntry(preset.Document, preset.Section, preset.Key, preset.Value);
        check("workspace presets land in their categories", ProfileWorkspaceState.Presets.Where(p => !p.CreatesFileWriteSection)
            .All(p => state.Rows(p.Category).Count > 0));
        var fileWritePreset = ProfileWorkspaceState.Presets.Single(p => p.CreatesFileWriteSection);
        state.AddFileWriteRule();
        check("workspace FileWrite preset creates a rule", state.Rows(fileWritePreset.Category).Count > 0);
    }
}
