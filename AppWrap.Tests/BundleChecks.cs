using System.IO;
using System.Security.Cryptography;
using AppWrap.Build;
using AppWrap.Core.PortableProfiles;
using AppWrap.Generators;

/// <summary>
/// P6 checks for the generated source/license bundle. They run against a small
/// fake backend tree with the pinned layout — no real PAL backend, no compiler
/// execution — and cover the recipe/notices text, the bundle layout, per-file
/// hashes, determinism, and the fail-loud missing-backend contract.
/// </summary>
internal static class BundleChecks
{
    private static PortableProfile PalProfile() =>
        PortableProfileCodec.FromIniTexts("[Launch]\nProgramExecutable=Payload\\bin\\Demo.exe\n", "", null, null, PortableExecutionKind.Pal);

    private static string[] SampleArguments() => PalLaunchArguments.Build("C:\\pal", "C:\\stage\\portable",
        "C:\\stage\\pal-compiler.log", "Harness App Portable", "HarnessAppPortable", "2.2.9", ["XML_ENABLED"]);

    private static string Sha256(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static string PackageFile(string package, string packagePath) => Path.Combine([package, .. packagePath.Split('/')]);

    private static string FakeBackend(string root, bool withLicense = true, bool withSegments = true)
    {
        var source = Path.Combine(root, "Other", "Source");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "PortableApps.comLauncher.nsi"), "; fake launcher script\n");
        File.WriteAllText(Path.Combine(source, "Segments.nsh"), "; fake segments include\n");
        if (withLicense) File.WriteAllText(Path.Combine(source, "License.txt"), "GNU GENERAL PUBLIC LICENSE\nVersion 2, June 1991\n");
        File.WriteAllText(Path.Combine(source, "Readme.txt"), "fake readme\n");
        if (withSegments)
        {
            Directory.CreateDirectory(Path.Combine(source, "Segments"));
            File.WriteAllText(Path.Combine(source, "Segments", "Core.nsh"), "; fake segment\n");
        }
        Directory.CreateDirectory(Path.Combine(source, "Languages"));
        File.WriteAllText(Path.Combine(source, "Languages", "English.nsh"), "; fake language\n");
        Directory.CreateDirectory(Path.Combine(source, "Plugins"));
        File.WriteAllBytes(Path.Combine(source, "Plugins", "xml.dll"), [1, 2, 3, 4]);
        var nsis = Path.Combine(root, "App", "NSIS"); Directory.CreateDirectory(nsis);
        File.WriteAllText(Path.Combine(nsis, "COPYING"), "fake NSIS copying\n");
        var docs = Path.Combine(nsis, "Docs", "NewTextReplace"); Directory.CreateDirectory(docs);
        File.WriteAllText(Path.Combine(docs, "Readme.txt"), "fake NewTextReplace readme\n");
        return root;
    }

    public static async Task RunAsync(string evidence, Action<string, bool> check)
    {
        var profile = PalProfile();
        var args = SampleArguments();
        var engine = EngineLock.Pinned;

        // Recipe: pins, URLs, preparation command and the exact argument array.
        var recipe = PalSourceBundle.RecipeText(profile, args, [("Other/Source/x.nsh", "ab12")], gplTextBundled: true);
        check("bundle recipe records PAL pin", recipe.Contains($"PAL version: {engine.PalVersion}", StringComparison.Ordinal));
        check("bundle recipe records archive hash", recipe.Contains(engine.PalSha256, StringComparison.Ordinal));
        check("bundle recipe records NSIS pin", recipe.Contains(engine.NsisVersion, StringComparison.Ordinal) && recipe.Contains(engine.NsisSha256, StringComparison.Ordinal));
        check("bundle recipe records source URLs", recipe.Contains(PalSourceBundle.LauncherPageUrl, StringComparison.Ordinal) && recipe.Contains(PalSourceBundle.DownloadUrl(engine), StringComparison.Ordinal));
        check("bundle recipe records backend preparation", recipe.Contains("tools/Prepare-PalBackend.ps1", StringComparison.Ordinal));
        check("bundle recipe records every compiler argument", args.All(a => recipe.Contains(a, StringComparison.Ordinal)));
        check("bundle recipe lists inventory hashes", recipe.Contains("Other/Source/x.nsh", StringComparison.Ordinal) && recipe.Contains("ab12", StringComparison.Ordinal));
        check("bundle recipe points at bundled GPL text", recipe.Contains("PortableApps.comLauncher/License.txt", StringComparison.Ordinal));
        var recipeNoGpl = PalSourceBundle.RecipeText(profile, args, [], gplTextBundled: false);
        check("bundle recipe without backend GPL text says where it lives",
            recipeNoGpl.Contains("did not include a standalone GPL text file", StringComparison.Ordinal) && recipeNoGpl.Contains(PalSourceBundle.LauncherPageUrl, StringComparison.Ordinal));

        // Notices: the established path's text now points at the bundle.
        var notices = PalSourceBundle.NoticesText(profile, gplTextBundled: true);
        check("bundle notices keep pins and GPL statement", notices.Contains(engine.PalSha256, StringComparison.Ordinal) && notices.Contains("GPL-2.0-or-later", StringComparison.Ordinal));
        check("bundle notices point at the source bundle", notices.Contains(@"Other\Source\AppWrap-BUILD-RECIPE.md", StringComparison.Ordinal) && notices.Contains(@"Other\Source\PortableApps.comLauncher\License.txt", StringComparison.Ordinal));
        check("bundle notices without backend GPL text use the fallback",
            PalSourceBundle.NoticesText(profile, gplTextBundled: false).Contains("included no standalone GPL text file", StringComparison.Ordinal));

        // Missing-backend detection is exact and complete.
        var root = Path.Combine(evidence, "bundle"); Directory.CreateDirectory(root);
        var emptyBackend = Path.Combine(root, "empty-backend"); Directory.CreateDirectory(emptyBackend);
        check("bundle missing backend reports every required item", PalSourceBundle.MissingRequiredFiles(emptyBackend).Count == 5);

        var generated = new PalSourceBundle.GeneratedFiles("HarnessAppPortable.ini",
            "[Launch]\nProgramExecutable=Payload\\bin\\Demo.exe\n", "[Source]\nIncludeInstallerSource=false\n", "[Format]\nType=PortableApps.comFormat\n");

        // A missing required item aborts loudly and writes nothing at all.
        var partialBackend = FakeBackend(Path.Combine(root, "partial-backend"), withSegments: false);
        var partialPackage = Path.Combine(root, "partial-package"); Directory.CreateDirectory(partialPackage);
        var loud = false;
        try { await PalSourceBundle.WriteAsync(partialBackend, partialPackage, profile, generated, args, CancellationToken.None); }
        catch (BuildFailureException ex) { loud = ex.Details.Contains("Segments", StringComparison.Ordinal); }
        check("bundle missing segments fails loudly", loud);
        check("bundle failure writes no partial bundle", !Directory.Exists(Path.Combine(partialPackage, "Other")));

        // A complete fake backend produces the full deterministic bundle.
        var backend = FakeBackend(Path.Combine(root, "backend"));
        var package = Path.Combine(root, "package"); Directory.CreateDirectory(package);
        var result = await PalSourceBundle.WriteAsync(backend, package, profile, generated, args, CancellationToken.None);
        check("bundle reports GPL text bundled", result.GplTextBundled);
        foreach (var path in new[]
        {
            "Other/Source/PortableApps.comLauncher/PortableApps.comLauncher.nsi",
            "Other/Source/PortableApps.comLauncher/License.txt",
            "Other/Source/PortableApps.comLauncher/Segments.nsh",
            "Other/Source/PortableApps.comLauncher/Segments/Core.nsh",
            "Other/Source/PortableApps.comLauncher/Languages/English.nsh",
            "Other/Source/PortableApps.comLauncher/Plugins/xml.dll",
            "Other/Source/PluginNotices/NSIS-COPYING.txt",
            "Other/Source/PluginNotices/NewTextReplace-Readme.txt",
            "Other/Source/AppWrap-Generated/HarnessAppPortable.ini",
            "Other/Source/AppWrap-Generated/installer.ini",
            "Other/Source/AppWrap-Generated/appinfo.ini",
            "Other/Source/AppWrap-BUILD-RECIPE.md",
        })
            check("bundle contains " + path, File.Exists(PackageFile(package, path)));
        check("bundle generated launcher matches the package file",
            File.ReadAllText(PackageFile(package, "Other/Source/AppWrap-Generated/HarnessAppPortable.ini")).Contains("ProgramExecutable=Payload", StringComparison.Ordinal));
        check("bundle copied source is byte-identical",
            File.ReadAllBytes(PackageFile(package, "Other/Source/PortableApps.comLauncher/Plugins/xml.dll")).SequenceEqual(new byte[] { 1, 2, 3, 4 }));
        var writtenRecipe = File.ReadAllText(PackageFile(package, "Other/Source/AppWrap-BUILD-RECIPE.md"));
        check("bundle recipe hashes the copied launcher source",
            writtenRecipe.Contains(Sha256(Path.Combine(backend, "Other", "Source", "PortableApps.comLauncher.nsi")), StringComparison.Ordinal));
        check("bundle result lists the written files", result.Files.Contains("Other/Source/AppWrap-BUILD-RECIPE.md") && result.Files.Contains("Other/Source/AppWrap-Generated/installer.ini"));

        // Deterministic: a second identical write produces an identical recipe.
        var package2 = Path.Combine(root, "package2"); Directory.CreateDirectory(package2);
        await PalSourceBundle.WriteAsync(backend, package2, profile, generated, args, CancellationToken.None);
        check("bundle recipe is deterministic",
            File.ReadAllBytes(PackageFile(package, "Other/Source/AppWrap-BUILD-RECIPE.md")).SequenceEqual(File.ReadAllBytes(PackageFile(package2, "Other/Source/AppWrap-BUILD-RECIPE.md"))));

        // A backend without the GPL text file still bundles, and says so.
        var noGplBackend = FakeBackend(Path.Combine(root, "nogpl-backend"), withLicense: false);
        var noGplPackage = Path.Combine(root, "nogpl-package"); Directory.CreateDirectory(noGplPackage);
        var noGpl = await PalSourceBundle.WriteAsync(noGplBackend, noGplPackage, profile, generated, args, CancellationToken.None);
        check("bundle without backend GPL text succeeds with fallback flag", !noGpl.GplTextBundled && !File.Exists(PackageFile(noGplPackage, "Other/Source/PortableApps.comLauncher/License.txt")));
    }
}
