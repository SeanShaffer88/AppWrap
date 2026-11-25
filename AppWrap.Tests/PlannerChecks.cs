using AppWrap.Core.PortableProfiles;

/// <summary>
/// P2/P3 checks: typed rule views derived from the launcher document, the immutable
/// operation plan, and planner-level cross-rule diagnostics. Pure configuration
/// logic — no compiler, no host access.
///
/// RegistryValueWrite format verified against the pinned PAL 2.2.9 sources:
/// Other/Source/Segments/RegistryValueWrite.nsh (entry key is "path\value", the
/// entry value is "type:string or string", type defaults to REG_SZ) and
/// Other/Source/Manual/ref/launcher.ini/registry.rst
/// ("HK??\*Key\Value=REG_[TYPE]:[value]; REG_TYPE: is optional, defaults to REG_SZ").
/// </summary>
internal static class PlannerChecks
{
    private static PortableProfile Pal(string launcher) =>
        PortableProfileCodec.FromIniTexts(launcher, "", null, null, PortableExecutionKind.Pal);

    private static bool HasCode(IReadOnlyList<ProfileDiagnostic> diagnostics, string code) =>
        diagnostics.Any(d => d.Code == code);

    private static bool HasError(IReadOnlyList<ProfileDiagnostic> diagnostics, string code) =>
        diagnostics.Any(d => d.Code == code && d.Severity == ProfileSeverity.Error);

    // A full realistic profile exercising every rule family. Force-cleanup rows are
    // acknowledged, move targets are disjoint, and the value write is covered by a
    // [RegistryKeys] rule, so neither the P0 validator nor the planner may error.
    private const string FullLauncher =
        "[Launch]\n" +
        "ProgramExecutable=Payload\\bin\\MyApp.exe\n" +
        "ProgramExecutable64=Payload\\bin64\\MyApp.exe\n" +
        "CommandLineArguments=--portable\n" +
        "WaitForProgram=true\n" +
        "WaitForEXE1=helper.exe\n" +
        "RunAsAdmin=compileforce\n" +
        "SplashTime=2000\n" +
        "\n" +
        "[Activate]\n" +
        "Registry=true\n" +
        "XML=true\n" +
        "\n" +
        "[Environment]\n" +
        "MYAPP_HOME=%PAL:DataDir%\\settings\n" +
        "MYAPP_EXTRA=%MYAPP_HOME%\\extra\n" +
        "\n" +
        "[Language]\n" +
        "Base=%PortableApps.comLanguageCode%\n" +
        "Default=en\n" +
        "\n" +
        "[LanguageStrings]\n" +
        "en=English\n" +
        "de=Deutsch\n" +
        "\n" +
        "[LanguageFile]\n" +
        "Type=INI\n" +
        "File=%PAL:AppDir%\\MyApp\\lang.ini\n" +
        "Section=Language\n" +
        "Key=Current\n" +
        "\n" +
        "[RegistryKeys]\n" +
        "AppSettings=HKCU\\Software\\MyApp\n" +
        "-=HKCU\\Software\\MyAppLegacy\n" +
        "\n" +
        "[RegistryValueBackupDelete]\n" +
        "1=HKCU\\Software\\MyApp\\LegacyValue\n" +
        "\n" +
        "[RegistryValueWrite]\n" +
        "HKCU\\Software\\MyApp\\Key\\Value=REG_DWORD:16\n" +
        "\n" +
        "[RegistryCleanupIfEmpty]\n" +
        "1=HKCU\\Software\\Publisher\n" +
        "\n" +
        "[RegistryCleanupForce]\n" +
        "1=HKCU\\Software\\MyAppTemp\n" +
        "\n" +
        "[QtKeysCleanup]\n" +
        "1=Qt Plugin Cache 4.6.false\n" +
        "\n" +
        "[FilesMove]\n" +
        "settings\\settings.ini=%APPDATA%\\MyAppFiles\n" +
        "\n" +
        "[DirectoriesMove]\n" +
        "MyAppData=%APPDATA%\\MyApp\\Data\n" +
        "-=%LOCALAPPDATA%\\MyAppScratch\n" +
        "\n" +
        "[DirectoriesCleanupIfEmpty]\n" +
        "1=%APPDATA%\\Publisher\n" +
        "\n" +
        "[DirectoriesCleanupForce]\n" +
        "1=%TEMP%\\MyAppScratch\n" +
        "\n" +
        "[FileWrite1]\n" +
        "Type=INI\n" +
        "File=%PAL:DataDir%\\settings\\settings.ini\n" +
        "Section=Main\n" +
        "Key=Portable\n" +
        "Value=true\n" +
        "\n" +
        "[FileWrite2]\n" +
        "Type=XML attribute\n" +
        "File=%PAL:AppDir%\\MyApp\\config.xml\n" +
        "XPath=/config/setting[@name='portable']\n" +
        "Attribute=value\n" +
        "Value=true\n";

    public static void Run(Action<string, bool> check, Action<string, Action> reject)
    {
        RulesParsing(check);
        FullPlan(check);
        PlannerDiagnostics(check);
    }

    private static void RulesParsing(Action<string, bool> check)
    {
        // Launch defaults: unset booleans stay null in the raw view, PAL defaults apply effectively.
        var minimal = ProfileRules.FromLauncher(IniDocument.Parse("[Launch]\nProgramExecutable=App\\MyApp.exe\n"));
        check("rules launch executable parsed", minimal.Launch.ProgramExecutable == "App\\MyApp.exe");
        check("rules wait defaults are PAL defaults",
            minimal.Launch.WaitForProgram is null && minimal.Launch.EffectiveWaitForProgram
            && minimal.Launch.WaitForOtherInstances is null && minimal.Launch.EffectiveWaitForOtherInstances
            && minimal.Launch.CleanTemp is null && minimal.Launch.EffectiveCleanTemp);
        var explicitOff = ProfileRules.FromLauncher(IniDocument.Parse("[Launch]\nWaitForProgram=false\nSplashTime=2500\nRunAsAdmin=try\n"));
        check("rules explicit wait off parsed", explicitOff.Launch.WaitForProgram == false && !explicitOff.Launch.EffectiveWaitForProgram);
        check("rules splash time and run-as-admin parsed", explicitOff.Launch.SplashTime == 2500 && explicitOff.Launch.RunAsAdmin == "try");

        // WaitForEXE entries are ordered by suffix number, not document order.
        var waits = ProfileRules.FromLauncher(IniDocument.Parse("[Launch]\nWaitForEXE2=second.exe\nWaitForEXE1=first.exe\n"));
        check("rules WaitForExe ordered by number", waits.Launch.WaitForExe.SequenceEqual(["first.exe", "second.exe"]));

        // Moves, including the '-' protect-local (discard) form for directories.
        var moves = ProfileRules.FromLauncher(IniDocument.Parse(
            "[FilesMove]\nsettings.ini=%APPDATA%\\MyApp\n" +
            "[DirectoriesMove]\nData=%APPDATA%\\MyApp\\Data\n-=%LOCALAPPDATA%\\Scratch\n"));
        check("rules file move parsed", moves.FileMoves.Count == 1 && !moves.FileMoves[0].IsDiscard
            && moves.FileMoves[0].PortablePath == "settings.ini" && moves.FileMoves[0].HostTarget == "%APPDATA%\\MyApp");
        check("rules directory moves parsed with discard row", moves.DirectoryMoves.Count == 2
            && !moves.DirectoryMoves[0].IsDiscard && moves.DirectoryMoves[0].PortablePath == "Data"
            && moves.DirectoryMoves[1].IsDiscard && moves.DirectoryMoves[1].PortablePath == ""
            && moves.DirectoryMoves[1].HostTarget == "%LOCALAPPDATA%\\Scratch");

        // Registry keys, including the '-' discard row.
        var keys = ProfileRules.FromLauncher(IniDocument.Parse(
            "[RegistryKeys]\nAppSettings=HKCU\\Software\\MyApp\n-=HKCU\\Software\\Old\n"));
        check("rules registry keys parsed with discard row", keys.RegistryKeys.Count == 2
            && !keys.RegistryKeys[0].IsDiscard && keys.RegistryKeys[0].StorageName == "AppSettings"
            && keys.RegistryKeys[0].HiveKey == "HKCU\\Software\\MyApp"
            && keys.RegistryKeys[1].IsDiscard && keys.RegistryKeys[1].HiveKey == "HKCU\\Software\\Old");

        // RegistryValueWrite, per the verified PAL format (see file header).
        var writes = ProfileRules.FromLauncher(IniDocument.Parse(
            "[RegistryValueWrite]\n" +
            "HKCU\\Software\\MyApp\\Key\\Value=REG_DWORD:16\n" +
            "HKCU\\Software\\MyApp\\Key\\Value2=REG_SZ:%PAL:DataDir%\n" +
            "HKCU\\Software\\MyApp\\Key\\Value3=plain\n" +
            "HKCU\\Software\\MyApp\\Key\\Value4=C:\\path\n" +
            "HKCU\\Software\\MyApp\\Key\\=REG_SZ:defaulted\n"));
        check("rules value write typed parse", writes.RegistryValueWrites[0] is
            { KeyPath: "HKCU\\Software\\MyApp\\Key", ValueName: "Value", Type: "REG_DWORD", Data: "16" });
        check("rules value write string parse", writes.RegistryValueWrites[1] is
            { ValueName: "Value2", Type: "REG_SZ", Data: "%PAL:DataDir%" });
        check("rules value write default type", writes.RegistryValueWrites[2] is
            { ValueName: "Value3", Type: "REG_SZ", Data: "plain" });
        check("rules value write colon data stays intact", writes.RegistryValueWrites[3] is
            { ValueName: "Value4", Type: "REG_SZ", Data: "C:\\path" });
        check("rules value write default value name", writes.RegistryValueWrites[4] is
            { KeyPath: "HKCU\\Software\\MyApp\\Key", ValueName: "", Type: "REG_SZ", Data: "defaulted" });

        // Environment order and sequential lists in numeric order.
        var env = ProfileRules.FromLauncher(IniDocument.Parse(
            "[Environment]\nBEE=2\nAYE=1\n" +
            "[RegistryCleanupIfEmpty]\n2=HKCU\\Software\\Second\n1=HKCU\\Software\\First\n"));
        check("rules environment keeps document order", env.Environment.Select(e => e.Name).SequenceEqual(["BEE", "AYE"]));
        check("rules sequential list in numeric order", env.RegistryCleanupIfEmpty.SequenceEqual(["HKCU\\Software\\First", "HKCU\\Software\\Second"]));

        // FileWrite rules ordered by section number; language views.
        var fileWrites = ProfileRules.FromLauncher(IniDocument.Parse(
            "[FileWrite2]\nType=Replace\nFile=b.txt\n" +
            "[FileWrite1]\nType=INI\nFile=a.ini\n" +
            "[Language]\nBase=en\n[LanguageStrings]\nde=Deutsch\nen=English\n"));
        check("rules FileWrite ordered by index", fileWrites.FileWrites.Select(w => w.Index).SequenceEqual([1, 2])
            && fileWrites.FileWrites[0].Get("File") == "a.ini" && fileWrites.FileWrites[1].Get("Type") == "Replace");
        check("rules language parsed", fileWrites.Language.Base == "en"
            && fileWrites.Language.Strings.Select(p => p.Key).SequenceEqual(["de", "en"]));
    }

    private static void FullPlan(Action<string, bool> check)
    {
        var profile = Pal(FullLauncher) with { ForceCleanupAcknowledged = true };
        var (plan, diagnostics) = ProfilePlanner.Prepare(profile, "MyAppPortable");
        check("planner full profile produces a plan", plan is not null);
        check("planner full profile has no error diagnostics", !diagnostics.Any(d => d.Severity == ProfileSeverity.Error));
        if (plan is null) return;

        var expectedKinds = new[]
        {
            PlannedOperationKind.SetEnvironment, PlannedOperationKind.SetEnvironment,
            PlannedOperationKind.ApplyLanguageFile,
            PlannedOperationKind.BackupRegistryKey, PlannedOperationKind.DiscardRegistryKey,
            PlannedOperationKind.BackupDeleteRegistryValue,
            PlannedOperationKind.WriteRegistryValue,
            PlannedOperationKind.MoveFile,
            PlannedOperationKind.MoveDirectory, PlannedOperationKind.MoveDirectory,
            PlannedOperationKind.WriteConfigFile, PlannedOperationKind.WriteConfigFile,
            PlannedOperationKind.LaunchProgram,
            PlannedOperationKind.CleanupRegistryIfEmpty, PlannedOperationKind.ForceCleanupRegistry,
            PlannedOperationKind.CleanupDirectoryIfEmpty, PlannedOperationKind.ForceCleanupDirectory,
        };
        check("planner operation order and kinds", plan.Operations.Select(o => o.Kind).SequenceEqual(expectedKinds));
        check("planner summaries are plain language",
            plan.Operations.Any(o => o.Kind == PlannedOperationKind.MoveFile
                && o.Summary == "Save portable file settings\\settings.ini to %APPDATA%\\MyAppFiles and restore the host copy on exit")
            && plan.Operations.Any(o => o.Kind == PlannedOperationKind.LaunchProgram
                && o.Summary.Contains("Payload\\bin\\MyApp.exe", StringComparison.Ordinal)));
        check("planner required data parents", plan.RequiredDataParents.SequenceEqual(["settings"]));
        check("planner flags", plan.RequiresWaitForProgram && plan.RequiresXmlCompile && plan.RequiresAdminCompileForce);
        check("planner host backup locations",
            plan.HostBackupLocations.Any(b => b.Contains(@"HKCU\Software\PortableApps.com\Keys", StringComparison.Ordinal))
            && plan.HostBackupLocations.Any(b => b.Contains(@"HKCU\Software\PortableApps.com\Values", StringComparison.Ordinal))
            && plan.HostBackupLocations.Contains("%APPDATA%\\MyAppFiles\\settings.ini-BackupByMyAppPortable (host file backup)")
            && plan.HostBackupLocations.Contains("%APPDATA%\\MyApp\\Data-BackupByMyAppPortable (host folder backup)")
            && plan.HostBackupLocations.Contains("%LOCALAPPDATA%\\MyAppScratch-BackupByMyAppPortable (host folder backup)"));

        // A packaging-only profile plans to nothing, with no diagnostics.
        var (emptyPlan, emptyDiagnostics) = ProfilePlanner.Prepare(PortableProfile.PackagingOnly(), "AnyAppPortable");
        check("planner packaging-only is an empty plan", emptyPlan is not null && emptyPlan.Operations.Count == 0 && emptyDiagnostics.Count == 0);
    }

    private static void PlannerDiagnostics(Action<string, bool> check)
    {
        (PreparedPlan? Plan, IReadOnlyList<ProfileDiagnostic> Diagnostics) Prepare(string launcher, bool ack = false) =>
            ProfilePlanner.Prepare(Pal(launcher) with { ForceCleanupAcknowledged = ack }, "MyAppPortable");

        var settings = Prepare("[Launch]\nProgramExecutable=App\\MyApp.exe\n[DirectoriesMove]\nsettings=%APPDATA%\\MyApp\\Settings\n");
        check("planner rejects moving Data settings", settings.Plan is null && HasError(settings.Diagnostics, "MoveSourceIsSettingsDir"));
        var settingsSub = Prepare("[Launch]\nProgramExecutable=App\\MyApp.exe\n[DirectoriesMove]\nsettings\\sub=%APPDATA%\\MyApp\\Settings\n");
        check("planner rejects moving Data settings subfolder", settingsSub.Plan is null && HasError(settingsSub.Diagnostics, "MoveSourceIsSettingsDir"));

        var overlap = Prepare("[Launch]\nProgramExecutable=App\\MyApp.exe\n[FilesMove]\na.ini=%APPDATA%\\MyApp\n[DirectoriesMove]\nData=%APPDATA%\\MyApp\\Sub\n");
        check("planner rejects overlapping move targets", overlap.Plan is null && HasError(overlap.Diagnostics, "OverlappingMoveTargets"));
        var disjoint = Prepare("[Launch]\nProgramExecutable=App\\MyApp.exe\n[FilesMove]\na.ini=%APPDATA%\\MyAppFiles\n[DirectoriesMove]\nData=%APPDATA%\\MyApp\\Data\n");
        check("planner allows disjoint move targets", disjoint.Plan is not null && !HasCode(disjoint.Diagnostics, "OverlappingMoveTargets"));

        var dirCleanup = Prepare("[Launch]\nProgramExecutable=App\\MyApp.exe\n[FilesMove]\na.ini=%APPDATA%\\MyApp\n[DirectoriesCleanupForce]\n1=%APPDATA%\\MyApp\n", ack: true);
        check("planner rejects directory force cleanup over a move", dirCleanup.Plan is null && HasError(dirCleanup.Diagnostics, "CleanupOverlapsRestore"));
        var regCleanup = Prepare("[Activate]\nRegistry=true\n[Launch]\nProgramExecutable=App\\MyApp.exe\n[RegistryKeys]\nApp=HKCU\\Software\\MyApp\\Sub\n[RegistryCleanupForce]\n1=HKCU\\Software\\MyApp\n", ack: true);
        check("planner rejects registry force cleanup over a saved key", regCleanup.Plan is null && HasError(regCleanup.Diagnostics, "CleanupOverlapsRestore"));
        var safeCleanup = Prepare("[Launch]\nProgramExecutable=App\\MyApp.exe\n[FilesMove]\na.ini=%APPDATA%\\MyAppFiles\n[DirectoriesCleanupForce]\n1=%TEMP%\\MyAppScratch\n", ack: true);
        check("planner allows unrelated force cleanup", safeCleanup.Plan is not null && !HasCode(safeCleanup.Diagnostics, "CleanupOverlapsRestore"));

        var cycle = Prepare("[Launch]\nProgramExecutable=App\\MyApp.exe\n[Environment]\nAYE=%BEE%\\x\nBEE=%AYE%\\y\n");
        check("planner rejects environment cycle", cycle.Plan is null && HasError(cycle.Diagnostics, "EnvironmentCycle"));
        var selfRef = Prepare("[Launch]\nProgramExecutable=App\\MyApp.exe\n[Environment]\nPATH=%PATH%;%PAL:AppDir%\n");
        check("planner allows environment self-reference", selfRef.Plan is not null && !HasCode(selfRef.Diagnostics, "EnvironmentCycle"));

        var missingFile = Prepare("[Launch]\nProgramExecutable=App\\MyApp.exe\n[FileWrite1]\nType=INI\nKey=Portable\n");
        check("planner rejects FileWrite without File", missingFile.Plan is null && HasError(missingFile.Diagnostics, "FileWriteMissingFile"));

        var fileXml = Prepare("[Launch]\nProgramExecutable=App\\MyApp.exe\n[FileWrite1]\nType=XML text\nFile=config.xml\n");
        check("planner rejects FileWrite XML without Activate", fileXml.Plan is null && HasError(fileXml.Diagnostics, "FileWriteXmlNeedsActivate"));
        var fileXmlOk = Prepare("[Activate]\nXML=true\n[Launch]\nProgramExecutable=App\\MyApp.exe\n[FileWrite1]\nType=XML text\nFile=config.xml\n");
        check("planner allows FileWrite XML with Activate", fileXmlOk.Plan is not null && !HasCode(fileXmlOk.Diagnostics, "FileWriteXmlNeedsActivate")
            && fileXmlOk.Plan!.RequiresXmlCompile);

        var langXml = Prepare("[Launch]\nProgramExecutable=App\\MyApp.exe\n[LanguageFile]\nType=XML attribute\nFile=config.xml\n");
        check("planner rejects LanguageFile XML without Activate", langXml.Plan is null && HasError(langXml.Diagnostics, "LanguageFileXmlNeedsActivate"));
        var langXmlOk = Prepare("[Activate]\nXML=true\n[Launch]\nProgramExecutable=App\\MyApp.exe\n[LanguageFile]\nType=XML attribute\nFile=config.xml\n");
        check("planner allows LanguageFile XML with Activate", langXmlOk.Plan is not null && !HasCode(langXmlOk.Diagnostics, "LanguageFileXmlNeedsActivate"));

        // A P0 validator error also blocks planning (no plan from an invalid profile).
        var unknown = Prepare("[Launch]\nProgramExecutable=App\\MyApp.exe\nNoSuchKey=1\n");
        check("planner produces no plan when validation fails", unknown.Plan is null && HasError(unknown.Diagnostics, "UnknownKey"));
    }
}
