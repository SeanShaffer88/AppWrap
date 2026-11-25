using AppWrap.Core.PortableProfiles;
using AppWrap.Generators;

/// <summary>
/// P4 checks: typed installer (installer.ini) and metadata (appinfo.ini) views,
/// installer validation (guard ownership, preserve/remove limits and conflicts,
/// optional components, languages, guard-order-blocked families, metadata
/// identity/version), and the deterministic guard-first document merger.
/// Pure configuration logic — no compiler, no host access, no downloads.
/// </summary>
internal static class InstallerChecks
{
    private const string Sha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static PortableProfile Pal(string installer, string? metadata = null) =>
        PortableProfileCodec.FromIniTexts(
            "[Launch]\nProgramExecutable=Payload\\bin\\MyApp.exe\n", installer, metadata, null,
            PortableExecutionKind.Pal);

    private static IReadOnlyList<ProfileDiagnostic> Validate(string installer, string? metadata = null, string? wizardAppId = null) =>
        InstallerValidator.ValidateInstaller(Pal(installer, metadata), wizardAppId);

    private static bool HasCode(IReadOnlyList<ProfileDiagnostic> diagnostics, string code) =>
        diagnostics.Any(d => d.Code == code);

    private static bool HasError(IReadOnlyList<ProfileDiagnostic> diagnostics, string code) =>
        diagnostics.Any(d => d.Code == code && d.Severity == ProfileSeverity.Error);

    public static void Run(Action<string, bool> check, Action<string, Action> reject)
    {
        TypedParsing(check);
        Diagnostics(check);
        Merger(check);
    }

    private static void TypedParsing(Action<string, bool> check)
    {
        var installer = InstallerRules.FromInstaller(IniDocument.Parse(
            "[DirectoriesToPreserve]\nPreserveDirectory2=App\\MyApp\\logs\nPreserveDirectory1=App\\MyApp\\cache\n" +
            "[DirectoriesToRemove]\nRemoveDirectory1=App\\Other\\old\n" +
            "[FilesToPreserve]\nPreserveFile1=App\\MyApp\\keep.txt\n" +
            "[FilesToRemove]\nRemoveFile1=App\\Other\\stale.txt\n" +
            "[MainDirectories]\nRemoveAppDirectory=true\nRemoveOtherDirectory=false\n" +
            "[OptionalComponents]\nOptionalComponents=true\nMainSectionTitle=Main\nOptionalSectionTitle=Extras\n" +
            "OptionalSectionSelectedInstallType=Multilingual\nOptionalSectionInstalledWhenSilent=true\n" +
            "OptionalDirectory1=extra\nOptionalFile1=extra\\file.txt\n" +
            "[Languages]\nENGLISH=true\nGERMAN=false\n"));
        check("installer preserve/remove lists parsed in numeric order",
            installer.PreserveRemove.DirectoriesToPreserve.Select(e => e.Value).SequenceEqual(["App\\MyApp\\cache", "App\\MyApp\\logs"])
            && installer.PreserveRemove.DirectoriesToPreserve[0].Index == 1
            && installer.PreserveRemove.DirectoriesToRemove.Single().Value == "App\\Other\\old"
            && installer.PreserveRemove.FilesToPreserve.Single().Value == "App\\MyApp\\keep.txt"
            && installer.PreserveRemove.FilesToRemove.Single().Value == "App\\Other\\stale.txt");
        check("installer main directories parsed",
            installer.MainDirectories.RemoveAppDirectory == true
            && installer.MainDirectories.RemoveOtherDirectory == false
            && !installer.MainDirectories.HasRemoveDataDirectory);
        check("installer optional components parsed",
            installer.OptionalComponents.OptionalComponents == true
            && installer.OptionalComponents.MainSectionTitle == "Main"
            && installer.OptionalComponents.OptionalSectionTitle == "Extras"
            && installer.OptionalComponents.OptionalSectionSelectedInstallType == "Multilingual"
            && installer.OptionalComponents.OptionalSectionInstalledWhenSilent == true
            && installer.OptionalComponents.OptionalDirectories.Single().Value == "extra"
            && installer.OptionalComponents.OptionalFiles.Single().Value == "extra\\file.txt");
        check("installer languages parsed",
            installer.HasLanguagesSection
            && installer.Languages.SequenceEqual(new[]
            {
                new KeyValuePair<string, bool?>("ENGLISH", true),
                new KeyValuePair<string, bool?>("GERMAN", false),
            }));

        var downloads = InstallerRules.FromInstaller(IniDocument.Parse(
            "[DownloadFiles]\nAdditionalInstallSize=2048\n" +
            "DownloadURL=https://example.org/app.zip\nDownloadName=Component\nDownloadFilename=app.zip\n" +
            $"DownloadSHA256={Sha}\nDownloadTo=App\\MyApp\n" +
            "AdvancedExtract1To=App\\MyApp\\sub\nAdvancedExtract1Filter=*.dll\n" +
            $"Download2URL=https://example.org/other.zip\nDownload2SHA256={Sha}\n" +
            "CustomCodeUses7zip=true\nUseLightweight7Zip=false\n"));
        check("installer downloads parsed",
            downloads.Downloads.AdditionalInstallSize == "2048"
            && downloads.Downloads.Primary.Url == "https://example.org/app.zip"
            && downloads.Downloads.Primary.Filename == "app.zip"
            && downloads.Downloads.Primary.Sha256 == Sha
            && downloads.Downloads.Primary.To == "App\\MyApp"
            && downloads.Downloads.Primary.IsEnabled
            && downloads.Downloads.Primary.AdvancedExtract.Any(p => p.Key == "AdvancedExtract1To" && p.Value == "App\\MyApp\\sub")
            && downloads.Downloads.Secondary.Url == "https://example.org/other.zip"
            && downloads.Downloads.Secondary.IsEnabled
            && downloads.Downloads.CustomCodeUses7zip == true
            && downloads.Downloads.UseLightweight7Zip == false);

        var copyLocal = InstallerRules.FromInstaller(IniDocument.Parse(
            "[CopyLocalFiles]\nCopyLocalFiles=true\nCopyFromDirectory=C:\\Program Files\\MyApp\nCopyToDirectory=App\\MyApp\n"));
        check("installer copy-local parsed and enabling",
            copyLocal.CopyLocal.CopyLocalFiles == true
            && copyLocal.CopyLocal.CopyFromDirectory == "C:\\Program Files\\MyApp"
            && copyLocal.CopyLocal.CopyToDirectory == "App\\MyApp"
            && copyLocal.CopyLocal.IsEnabled);
        var copyLocalOff = InstallerRules.FromInstaller(IniDocument.Parse("[CopyLocalFiles]\nCopyLocalFiles=false\n"));
        check("installer copy-local explicit false is not enabling", !copyLocalOff.CopyLocal.IsEnabled);

        var legacy = InstallerRules.FromInstaller(IniDocument.Parse(
            "[LegacyVersions]\nLegacy32AppIDSuffix=32\nLegacyOSAppNameSuffix=Legacy\n"));
        check("installer legacy versions parsed",
            legacy.Legacy.HasAnyEntry
            && legacy.Legacy.Legacy32.Any(p => p.Key == "Legacy32AppIDSuffix" && p.Value == "32")
            && legacy.Legacy.LegacyOs.Any(p => p.Key == "LegacyOSAppNameSuffix" && p.Value == "Legacy"));

        var metadata = InstallerRules.FromMetadata(IniDocument.Parse(
            "[Format]\nType=PortableApps.comFormat\nVersion=3.9\n" +
            "[Details]\nName=My App Portable\nAppID=MyAppPortable\nBaseAppName=MyApp\nPublisher=Example\n" +
            "Donate=https://example.org/donate\nInstallType=English, CustomType\n" +
            "[License]\nShareable=true\nOpenSource=false\nEULAVersion=1\n" +
            "[Version]\nPackageVersion=1.2.3.0\nDisplayVersion=1.2.3\n" +
            "[SpecialPaths]\nPlugins=App\\Plugins\n" +
            "[Dependencies]\nUsesJava=require\nRequires64bitOS=true\n" +
            "[Control]\nIcons=1\nStart=MyAppPortable.exe\nStart2=Other.exe\nName2=Other\nBaseAppID64=MyAppPortable64\nExitEXE=MyApp.exe\n"));
        check("installer metadata details parsed",
            metadata.FormatType == "PortableApps.comFormat" && metadata.FormatVersion == "3.9"
            && metadata.Name == "My App Portable" && metadata.AppID == "MyAppPortable"
            && metadata.BaseAppName == "MyApp" && metadata.Donate == "https://example.org/donate"
            && metadata.DeclaredInstallTypes.SequenceEqual(["English", "CustomType"]));
        check("installer metadata license and version parsed",
            metadata.LicenseShareable == true && metadata.LicenseOpenSource == false
            && metadata.LicenseEulaVersion == "1"
            && metadata.PackageVersion == "1.2.3.0" && metadata.DisplayVersion == "1.2.3"
            && metadata.PluginsPath == "App\\Plugins");
        check("installer metadata dependencies and control parsed",
            metadata.Dependencies.UsesJava == "require" && metadata.Dependencies.Requires64bitOS == true
            && metadata.Control.Start == "MyAppPortable.exe"
            && metadata.Control.AdditionalStarts.Single() is { Index: 2, Start: "Other.exe", Name: "Other" }
            && metadata.Control.BaseAppID64 == "MyAppPortable64"
            && metadata.Control.ExitEXE == "MyApp.exe");

        var plugin = InstallerRules.From(Pal("", "[Details]\nPluginName=Shared Files\nPluginType=CommonFiles\n"));
        check("installer plugin mode detected from metadata", plugin.Installer.Plugin.IsPluginMode
            && plugin.Installer.Plugin.PluginType == "CommonFiles");
        var noPlugin = InstallerRules.From(Pal(""));
        check("installer no plugin mode by default", !noPlugin.Installer.Plugin.IsPluginMode
            && noPlugin.Metadata.Document is null);
    }

    private static void Diagnostics(Action<string, bool> check)
    {
        // List limit: 10 allowed, 11 blocked.
        string PreserveList(int count) =>
            "[DirectoriesToPreserve]\n" + string.Concat(Enumerable.Range(1, count).Select(i => $"PreserveDirectory{i}=App\\MyApp\\dir{i}\n"));
        check("installer list limit allows ten", !HasError(Validate(PreserveList(10)), "InstallerListLimit"));
        check("installer list limit blocks eleven", HasError(Validate(PreserveList(11)), "InstallerListLimit"));

        // Preserve/remove overlap (ancestor in either direction), and disjoint paths are fine.
        var overlap = Validate("[DirectoriesToPreserve]\nPreserveDirectory1=App\\MyApp\n[FilesToRemove]\nRemoveFile1=App\\MyApp\\old.txt\n");
        check("installer preserve/remove overlap blocked", HasError(overlap, "PreserveRemoveOverlap"));
        var disjoint = Validate("[DirectoriesToPreserve]\nPreserveDirectory1=App\\MyApp\n[FilesToRemove]\nRemoveFile1=App\\Other\\old.txt\n");
        check("installer preserve/remove disjoint allowed", !HasCode(disjoint, "PreserveRemoveOverlap"));

        // Data is never a remove target; other removes are fine.
        check("installer remove of Data blocked", HasError(Validate("[DirectoriesToRemove]\nRemoveDirectory1=Data\n"), "RemoveTargetsData"));
        check("installer remove under Data blocked", HasError(Validate("[FilesToRemove]\nRemoveFile1=Data\\settings\\x.txt\n"), "RemoveTargetsData"));
        check("installer remove outside Data allowed", !HasCode(Validate("[DirectoriesToRemove]\nRemoveDirectory1=App\\old\n"), "RemoveTargetsData"));

        // Guard-owned settings cannot be set by a profile.
        check("installer guard CloseEXE override blocked",
            HasError(Validate("[CheckRunning]\nCloseEXE=MyApp.exe\n"), "GuardSettingOverride"));
        check("installer guard source override blocked",
            HasError(Validate("[Source]\nIncludeInstallerSource=true\n"), "GuardSettingOverride"));
        check("installer guard main-directory override blocked",
            HasError(Validate("[MainDirectories]\nRemoveAppDirectory=true\n"), "GuardSettingOverride"));

        // RemoveDataDirectory is not part of the current format.
        check("installer RemoveDataDirectory rejected",
            HasError(Validate("[MainDirectories]\nRemoveDataDirectory=true\n"), "RemovedDataDirectoryNotSupported"));

        // Optional payload entries need OptionalComponents=true.
        check("installer optional entries without components blocked",
            HasError(Validate("[OptionalComponents]\nOptionalFile1=extra\\file.txt\n"), "OptionalEntriesWithoutComponents"));
        check("installer optional entries with components allowed",
            !HasCode(Validate("[OptionalComponents]\nOptionalComponents=true\nOptionalFile1=extra\\file.txt\n"), "OptionalEntriesWithoutComponents"));

        // Install types: built-ins and metadata-declared types are valid; anything else is not.
        check("installer unknown install type blocked",
            HasError(Validate("[OptionalComponents]\nOptionalComponents=true\nOptionalSectionSelectedInstallType=Bogus\n"), "OptionalInstallTypeUnknown"));
        check("installer built-in install type allowed",
            !HasCode(Validate("[OptionalComponents]\nOptionalComponents=true\nOptionalSectionSelectedInstallType=Multilingual\n"), "OptionalInstallTypeUnknown"));
        check("installer metadata-declared install type allowed",
            !HasCode(Validate(
                "[OptionalComponents]\nOptionalComponents=true\nOptionalSectionSelectedInstallType=CustomType\n",
                "[Details]\nInstallType=CustomType\n"), "OptionalInstallTypeUnknown"));

        // Languages: English required when the section exists; unknown keys warn only.
        check("installer languages without English blocked",
            HasError(Validate("[Languages]\nGERMAN=true\n"), "InstallerLanguageEnglishRequired"));
        check("installer languages with English off blocked",
            HasError(Validate("[Languages]\nENGLISH=false\nGERMAN=true\n"), "InstallerLanguageEnglishRequired"));
        check("installer languages with English allowed",
            !HasCode(Validate("[Languages]\nENGLISH=true\nGERMAN=true\n"), "InstallerLanguageEnglishRequired"));
        var klingon = Validate("[Languages]\nENGLISH=true\nKLINGON=true\n");
        check("installer unknown language warns without erroring",
            klingon.Any(d => d.Code == "UnknownInstallerLanguage" && d.Severity == ProfileSeverity.Warning)
            && !klingon.Any(d => d.Severity == ProfileSeverity.Error));

        // Downloads: blocked by guard order, yet URL/hash correctness is still checked now.
        var goodDownload = Validate($"[DownloadFiles]\nDownloadURL=https://example.org/app.zip\nDownloadSHA256={Sha}\n");
        check("installer downloads blocked by guard order", HasError(goodDownload, "GuardOrderBlocked"));
        check("installer valid download passes URL and hash checks",
            !HasCode(goodDownload, "DownloadUrlNotHttps") && !HasCode(goodDownload, "DownloadHashInvalid"));
        check("installer http download blocked",
            HasError(Validate($"[DownloadFiles]\nDownloadURL=http://example.org/app.zip\nDownloadSHA256={Sha}\n"), "DownloadUrlNotHttps"));
        check("installer download with bad hash blocked",
            HasError(Validate("[DownloadFiles]\nDownloadURL=https://example.org/app.zip\nDownloadSHA256=xyz\n"), "DownloadHashInvalid"));
        check("installer download without hash blocked",
            HasError(Validate("[DownloadFiles]\nDownloadURL=https://example.org/app.zip\n"), "DownloadHashInvalid"));

        // Copy-local and legacy families are blocked while enabled; inert forms are not.
        check("installer copy-local blocked by guard order",
            HasError(Validate("[CopyLocalFiles]\nCopyLocalFiles=true\n"), "GuardOrderBlocked"));
        check("installer copy-local disabled is not blocked",
            !HasCode(Validate("[CopyLocalFiles]\nCopyLocalFiles=false\n"), "GuardOrderBlocked"));
        check("installer legacy versions blocked by guard order",
            HasError(Validate("[LegacyVersions]\nLegacy32AppIDSuffix=32\n"), "GuardOrderBlocked"));

        // Plugin mode is blocked as its own package mode.
        check("installer plugin mode blocked by guard order",
            HasError(Validate("", "[Details]\nPluginType=CommonFiles\n"), "GuardOrderBlocked"));

        // Metadata identity: the wizard-derived AppID is canonical.
        check("installer metadata AppID conflict blocked",
            HasError(Validate("", "[Details]\nAppID=OtherPortable\n", "MyAppPortable"), "MetadataAppIdConflict"));
        check("installer metadata AppID match allowed",
            !HasCode(Validate("", "[Details]\nAppID=MyAppPortable\n", "MyAppPortable"), "MetadataAppIdConflict"));
        check("installer metadata AppID unchecked without wizard identity",
            !HasCode(Validate("", "[Details]\nAppID=OtherPortable\n"), "MetadataAppIdConflict"));

        // Metadata version must be a one-to-four-component numeric version.
        check("installer metadata bad version blocked",
            HasError(Validate("", "[Version]\nPackageVersion=1.x\n"), "MetadataVersionInvalid"));
        check("installer metadata good version allowed",
            !HasCode(Validate("", "[Version]\nPackageVersion=1.2.3\n"), "MetadataVersionInvalid"));

        // A full, consistent installer + metadata pair produces no errors at all.
        var clean = Validate(
            "[DirectoriesToPreserve]\nPreserveDirectory1=App\\MyApp\\cache\n" +
            "[DirectoriesToRemove]\nRemoveDirectory1=App\\Other\\old\n" +
            "[FilesToPreserve]\nPreserveFile1=App\\MyApp\\keep.txt\n" +
            "[FilesToRemove]\nRemoveFile1=App\\Other\\stale.txt\n" +
            "[Languages]\nENGLISH=true\nGERMAN=true\n" +
            "[OptionalComponents]\nOptionalComponents=true\nOptionalSectionSelectedInstallType=Multilingual\nOptionalFile1=extra\\file.txt\n",
            "[Details]\nAppID=MyAppPortable\n[Version]\nPackageVersion=1.2.3.0\n",
            "MyAppPortable");
        check("installer clean profile has no errors", !clean.Any(d => d.Severity == ProfileSeverity.Error));
    }

    private static void Merger(Action<string, bool> check)
    {
        var guard = UpgradeGuardGenerator.InstallerSettings;

        // No profile entries: the guard text round-trips exactly.
        check("merger with empty profile returns guard text",
            InstallerDocumentMerger.Merge(guard, IniDocument.Parse("")) == guard);

        var profile = IniDocument.Parse(
            "[DirectoriesToPreserve]\nPreserveDirectory1=App\\MyApp\\cache\n" +
            "[CheckRunning]\nCloseEXE=EVIL.exe\n" +
            "[Languages]\nENGLISH=true\n");
        var merged = InstallerDocumentMerger.Merge(guard, profile);
        check("merger keeps guard values over profile attempts",
            merged.Contains("CloseEXE=NONE", StringComparison.Ordinal)
            && !merged.Contains("EVIL", StringComparison.Ordinal)
            && merged.Split("CloseEXE=").Length == 2);
        check("merger appends profile entries",
            merged.Contains("PreserveDirectory1=App\\MyApp\\cache", StringComparison.Ordinal)
            && merged.Contains("ENGLISH=true", StringComparison.Ordinal));
        check("merger keeps guard sections first",
            merged.IndexOf("[Source]", StringComparison.Ordinal) < merged.IndexOf("[DirectoriesToPreserve]", StringComparison.Ordinal)
            && merged.IndexOf("[CheckRunning]", StringComparison.Ordinal) < merged.IndexOf("[DirectoriesToPreserve]", StringComparison.Ordinal)
            && merged.IndexOf("[DirectoriesToPreserve]", StringComparison.Ordinal) < merged.IndexOf("[Languages]", StringComparison.Ordinal));
        check("merger is deterministic",
            InstallerDocumentMerger.Merge(guard, profile) == merged);
    }
}
