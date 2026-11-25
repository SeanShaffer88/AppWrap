using AppWrap.Core;
using AppWrap.Core.PortableProfiles;

internal static class ProfileChecks
{
    private const string CleanLauncher =
        "; Clean realistic launcher\r\n" +
        "[Launch]\r\n" +
        "ProgramExecutable=App\\MyApp.exe\r\n" +
        "WaitForProgram=true\r\n" +
        "\r\n" +
        "[Activate]\r\n" +
        "Registry=true\r\n" +
        "\r\n" +
        "[Environment]\r\n" +
        "MYAPP_HOME=%PAL:DataDir%\\settings\r\n" +
        "\r\n" +
        "[RegistryKeys]\r\n" +
        "AppSettings=HKCU\\Software\\MyApp\r\n" +
        "\r\n" +
        "[FilesMove]\r\n" +
        "settings.ini=%APPDATA%\\MyApp\r\n" +
        "\r\n" +
        "[DirectoriesMove]\r\n" +
        "MyAppData=%APPDATA%\\MyApp\\Data\r\n";

    private static PortableProfile Pal(string launcher) =>
        PortableProfileCodec.FromIniTexts(launcher, "", null, null, PortableExecutionKind.Pal);

    private static bool Has(PortableProfile profile, string code) =>
        PortableProfileValidator.Validate(profile).Any(d => d.Code == code);

    public static void Run(Action<string, bool> check, Action<string, Action> reject)
    {
        // Lossless roundtrip: comments, blank lines, repeated '-' discard rows.
        var sample = "; comment\r\n# another\r\n\r\n[Launch]\r\nProgramExecutable=App\\MyApp.exe\r\n\r\n[RegistryKeys]\r\n-=HKCU\\Software\\OldOne\r\n-=HKCU\\Software\\OldTwo\r\nApp=HKCU\\Software\\MyApp\r\n";
        var doc = IniDocument.Parse(sample);
        check("profile roundtrip lossless", doc.Serialize() == sample);
        check("profile repeated discard rows preserved", doc.GetValues("RegistryKeys", "-").SequenceEqual(["HKCU\\Software\\OldOne", "HKCU\\Software\\OldTwo"]));
        check("profile entries in order", doc.Entries("RegistryKeys").Select(e => e.Key).SequenceEqual(["-", "-", "App"]));
        var lf = IniDocument.Parse("[Launch]\nProgramExecutable=x.exe\n");
        check("profile LF roundtrip lossless", lf.Serialize() == "[Launch]\nProgramExecutable=x.exe\n");
        var noTrail = IniDocument.Parse("[Launch]\nProgramExecutable=x.exe");
        check("profile no-trailing-newline roundtrip", noTrail.Serialize() == "[Launch]\nProgramExecutable=x.exe");

        // SetValue in place (preserving raw key spelling) and append.
        var edit = IniDocument.Parse("[Launch]\r\nprogramExecutable =old.exe\r\nWaitForProgram=true\r\n");
        edit.SetValue("Launch", "ProgramExecutable", "new.exe");
        check("profile SetValue in place preserves key spelling", edit.Serialize().Contains("programExecutable =new.exe", StringComparison.Ordinal));
        check("profile SetValue updates typed value", edit.GetValue("Launch", "ProgramExecutable") == "new.exe");
        edit.SetValue("Launch", "SplashTime", "2000");
        check("profile SetValue appends to section", edit.GetValue("Launch", "SplashTime") == "2000" && edit.Serialize().Contains("[Launch]", StringComparison.Ordinal));
        edit.SetValue("Environment", "FOO", "bar");
        check("profile SetValue creates section", edit.GetValue("Environment", "FOO") == "bar");

        // Quote stripping on typed view, raw retained.
        var quoted = IniDocument.Parse("[Launch]\nCommandLineArguments=\"--portable\"\n");
        check("profile outer quotes stripped", quoted.GetValue("Launch", "CommandLineArguments") == "--portable");
        check("profile quoted raw retained", quoted.Serialize().Contains("\"--portable\"", StringComparison.Ordinal));

        // Bounds and parse diagnostics (parser does not throw on content).
        reject("profile oversize rejected", () => IniDocument.Parse(new string('a', IniDocument.MaxBytes + 1)));
        reject("profile too many lines rejected", () => IniDocument.Parse(string.Join("\n", Enumerable.Repeat("a=1", IniDocument.MaxLines + 1))));
        var bad = IniDocument.Parse("[Launch]\nProgramExecutable=bad\u0001exe\n[Broken\nnonsense line\n");
        check("profile parse issues collected", bad.ParseIssues.Count >= 3);
        check("profile malformed still roundtrips", bad.Serialize().Contains("[Broken", StringComparison.Ordinal));

        // Engine lock and codec.
        check("profile engine lock pinned", EngineLock.Pinned.PalVersion == "2.2.9"
            && EngineLock.Pinned.PalSha256 == "7bc93e47b885d5e953fa2082a2e65169ae98128026ac4d747f774a23bd01345b"
            && EngineLock.Pinned.NsisVersion == "2.46.5-Unicode"
            && EngineLock.Pinned.NsisSha256 == "47cd625d2d90f0cfdff411ba18c39c88ab7e9b28730fc81e5e14ed66ead6fb21");
        var codecProfile = Pal(CleanLauncher);
        check("profile codec roundtrip lossless", PortableProfileCodec.LauncherText(codecProfile) == CleanLauncher);
        check("profile schema version", codecProfile.SchemaVersion == PortableProfile.CurrentSchemaVersion && PortableProfile.CurrentSchemaVersion == 1);

        // Clean profile: no diagnostics at all.
        check("profile clean launcher has no diagnostics", PortableProfileValidator.Validate(Pal(CleanLauncher)).Count == 0);
        check("profile packaging-only has no diagnostics", PortableProfileValidator.Validate(PortableProfile.PackagingOnly()).Count == 0);

        // Each validator rule fires.
        check("profile rule UnknownKey", Has(Pal("[Launch]\nProgramExecutable=x.exe\nBogusKey=1\n"), "UnknownKey"));
        check("profile rule UnknownSection", Has(Pal("[Launch]\nProgramExecutable=x.exe\n[Bogus]\nKey=1\n"), "UnknownSection"));
        check("profile rule DeprecatedKey", Has(Pal("[Launch]\nProgramExecutable=x.exe\nAppName=Old\n"), "DeprecatedKey"));
        check("profile rule UnsupportedKey CopyData", Has(Pal("[LiveMode]\nCopyData=true\n"), "UnsupportedKey"));
        check("profile rule UnsupportedKey Service", Has(Pal("[Service1]\nName=Svc\n"), "UnsupportedKey"));
        check("profile rule DuplicateSingleton", Has(Pal("[Launch]\nProgramExecutable=x.exe\nProgramExecutable=y.exe\n"), "DuplicateSingleton"));
        check("profile repeated dash not duplicate singleton", !Has(Pal("[RegistryKeys]\n-=HKCU\\Software\\A\n-=HKCU\\Software\\B\n"), "DuplicateSingleton"));
        check("profile rule SequentialGap keys", Has(Pal("[RegistryCleanupIfEmpty]\n1=HKCU\\Software\\MyApp\\A\n3=HKCU\\Software\\MyApp\\B\n"), "SequentialGap"));
        check("profile rule SequentialGap FileWrite", Has(Pal("[FileWrite1]\nType=INI\n[FileWrite3]\nType=INI\n"), "SequentialGap"));
        check("profile rule SequentialGap WaitForEXE", Has(Pal("[Launch]\nProgramExecutable=x.exe\nWaitForEXE2=other.exe\n"), "SequentialGap"));
        check("profile rule SectionCase", Has(Pal("[registrykeys]\nApp=HKCU\\Software\\MyApp\n"), "SectionCase"));
        check("profile rule RegistryValueWriteUncovered", Has(Pal("[RegistryValueWrite]\nHKCU\\Software\\MyApp\\Setting=REG_SZ:value\n"), "RegistryValueWriteUncovered"));
        check("profile RegistryValueWrite covered by RegistryKeys", !Has(Pal("[RegistryKeys]\nApp=HKCU\\Software\\MyApp\n[RegistryValueWrite]\nHKCU\\Software\\MyApp\\Setting=REG_SZ:value\n"), "RegistryValueWriteUncovered"));
        check("profile RegistryValueWrite covered by BackupDelete", !Has(Pal("[RegistryValueBackupDelete]\n1=HKCU\\Software\\MyApp\\Setting\n[RegistryValueWrite]\nHKCU\\Software\\MyApp\\Setting=REG_SZ:value\n"), "RegistryValueWriteUncovered"));
        check("profile rule WaitForProgramUnsafe", Has(Pal("[Launch]\nProgramExecutable=x.exe\nWaitForProgram=false\n[FilesMove]\na.ini=%APPDATA%\\MyApp\n"), "WaitForProgramUnsafe"));
        check("profile WaitForProgram false alone is safe", !Has(Pal("[Launch]\nProgramExecutable=x.exe\nWaitForProgram=false\n"), "WaitForProgramUnsafe"));
        check("profile rule ForceCleanupUnacknowledged", Has(Pal("[RegistryCleanupForce]\n1=HKCU\\Software\\MyApp\\Temp\n"), "ForceCleanupUnacknowledged"));
        check("profile force cleanup acknowledged passes", !Has(Pal("[RegistryCleanupForce]\n1=HKCU\\Software\\MyApp\\Temp\n") with { ForceCleanupAcknowledged = true }, "ForceCleanupUnacknowledged"));
        check("profile rule BroadRegistryTarget hive", Has(Pal("[RegistryKeys]\nApp=HKCU\n"), "BroadRegistryTarget"));
        check("profile rule BroadRegistryTarget Software", Has(Pal("[RegistryKeys]\nApp=HKCU\\Software\n"), "BroadRegistryTarget"));
        check("profile rule BroadDirectoryTarget token", Has(Pal("[DirectoriesCleanupForce]\n1=%APPDATA%\n") with { ForceCleanupAcknowledged = true }, "BroadDirectoryTarget"));
        check("profile rule BroadDirectoryTarget drive root", Has(Pal("[DirectoriesMove]\nData=C:\\\n"), "BroadDirectoryTarget"));
        check("profile rule UnknownToken", Has(Pal("[Launch]\nProgramExecutable=x.exe\nCommandLineArguments=%NOTATOKEN%\\run\n"), "UnknownToken"));
        check("profile rule UnknownToken forward reference", PortableProfileValidator.Validate(Pal("[Launch]\nProgramExecutable=x.exe\nCommandLineArguments=%LATER%\n[Environment]\nLATER=value\n")).Any(d => d.Code == "UnknownToken" && d.Message.Contains("forward", StringComparison.OrdinalIgnoreCase)));
        check("profile environment token defined earlier passes", !Has(Pal("[Environment]\nEARLY=value\n[Launch]\nProgramExecutable=x.exe\nCommandLineArguments=%EARLY%\n"), "UnknownToken"));
        check("profile rule ControlCharacters", Has(Pal("[Launch]\nProgramExecutable=bad\u0001exe\n"), "ControlCharacters"));

        // Catalog basics.
        check("profile catalog launch supported", CapabilityCatalog.Classify(PortableDocumentKind.Launcher, "Launch", "ProgramExecutable") == CapabilityStatus.Supported);
        check("profile catalog unknown default", CapabilityCatalog.Classify(PortableDocumentKind.Launcher, "Launch", "Nope") == CapabilityStatus.Unrecognized);
        check("profile catalog known section", CapabilityCatalog.IsKnownSection("FileWrite12") && CapabilityCatalog.IsKnownSection("RegistryKeys") && !CapabilityCatalog.IsKnownSection("Nope"));
        check("profile catalog documented tokens", CapabilityCatalog.DocumentedTokens.Contains("PAL:AppDir") && CapabilityCatalog.DocumentedTokens.Contains("APPDATA"));

        // Trusted extensions: import never grants trust.
        var imported = PortableProfileCodec.FromIniTexts(CleanLauncher, "", "[Details]\ntrusted:true\n", null, PortableExecutionKind.Pal);
        check("profile import grants no trusted extensions", imported.TrustedExtensions.All(t => !t.Trusted));
        check("profile trusted extension defaults untrusted", !new TrustedExtension("custom.nsh", "abc", "hook").Trusted);
    }
}
