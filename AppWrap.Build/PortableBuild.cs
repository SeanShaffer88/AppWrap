using AppWrap.Core;
using AppWrap.Core.PortableProfiles;
using AppWrap.Generators;
using AppWrap.Imaging;
using System.Text;
namespace AppWrap.Build;
internal static class PortableBuild
{
    /// <summary>
    /// The guard helper's location inside a PAF package folder, written package-relative
    /// (the upstream installer template resolves its own source files the same way from
    /// App\installer, the same depth below the package root as Other\Source). The compiled
    /// .paf.exe flow instead embeds the helper from a build-machine staging path, because
    /// AppWrap's packaging builder compiles the folder immediately on that machine.
    /// </summary>
    internal const string PafGuardHelperPath = "..\\..\\Other\\Source\\AppWrap.UpgradeGuard.exe";

    public static async Task<string> BuildAsync(AppWrapInput input, string payload, string stage, string baseDirectory, CancellationToken token)
    {
        var package = await StagePackageAsync(input, payload, stage, baseDirectory, token, pafPackage: false, compileLauncher: true).ConfigureAwait(false);
        var backend = Path.Combine(stage, "builder");
        if (input.SidebarImagePath != null) ImageAssets.WriteSidebar(input.SidebarImagePath, Path.Combine(backend, "App", "installer", "PortableApps.comInstaller.bmp"));
        var result = await ProcessRunner.RunAsync(Path.Combine(backend, "AppWrapPackagingBuilder.exe"), [package], backend, token).ConfigureAwait(false);
        var logPath = Path.Combine(backend, "Data", "PortableApps.comInstallerLog.txt");
        var log = await BackendLog.ReadAsync(logPath, token).ConfigureAwait(false);
        var artifacts = Directory.GetFiles(stage, "*.paf.exe", SearchOption.TopDirectoryOnly);
        if (result.ExitCode != 0 || artifacts.Length != 1) throw new BuildFailureException("AppWrap couldn’t build the portable package. Check the app folder and try again.", $"Builder exit code: {result.ExitCode}\n{result.Log}\n{log}");
        return artifacts[0];
    }

    /// <summary>
    /// Builds a PAF Package: the same staged portable tree as the compiled portable
    /// package, left as a folder for the official PortableApps.com Installer to
    /// consume unchanged. No AppWrap packaging-compiler step runs; the folder is the
    /// output. See docs/PAF-COMPLIANCE.md for the exact relationship.
    /// </summary>
    public static Task<string> BuildPafPackageAsync(AppWrapInput input, string payload, string stage, string baseDirectory, CancellationToken token) =>
        StagePackageAsync(input, payload, stage, baseDirectory, token, pafPackage: true, compileLauncher: true);

    /// <summary>
    /// Assembles the canonical portable package tree under <c>stage/portable</c>:
    /// payload, generated appinfo.ini and installer.ini, launcher configuration and
    /// (when <paramref name="compileLauncher"/>) the compiled root launcher. The two
    /// outputs differ only in what a folder consumer needs: a PAF package also carries
    /// an empty Data placeholder and a self-contained upgrade-guard helper beside its
    /// hook (the compiled flow embeds the helper from the staging path instead).
    /// Launcher compilation uses the pinned Windows backends; checks pass
    /// <paramref name="compileLauncher"/>: false to verify the tree itself anywhere.
    /// </summary>
    internal static async Task<string> StagePackageAsync(AppWrapInput input, string payload, string stage, string baseDirectory, CancellationToken token, bool pafPackage, bool compileLauncher)
    {
        var sourceHooks = Path.Combine(payload, "Other", "Source");
        if (Directory.Exists(sourceHooks) && Directory.EnumerateFiles(sourceHooks, "*", SearchOption.AllDirectories).Any(p => Path.GetExtension(p).Equals(".nsh", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(p).Equals(".nsi", StringComparison.OrdinalIgnoreCase)))
            throw new UserInputException("This app contains custom packaging instructions. Choose its application folder without those instructions.");
        var package = Path.Combine(stage, "portable");
        Directory.CreateDirectory(Path.Combine(package, "App"));
        if (pafPackage) Directory.CreateDirectory(Path.Combine(package, "Data"));
        var previousInfo = Path.Combine(payload, "App", "AppInfo", "appinfo.ini");
        if (File.Exists(previousInfo) && new FileInfo(previousInfo).Length > 65536) throw new UserInputException("The app's existing package details are too large.");
        var existing = File.Exists(previousInfo) ? await File.ReadAllTextAsync(previousInfo, token).ConfigureAwait(false) : null;
        Directory.Move(payload, Path.Combine(package, "App", "Payload"));
        var metadataDir = Path.Combine(package, "App", "AppInfo"); Directory.CreateDirectory(metadataDir);
        var infoPath = Path.Combine(metadataDir, "appinfo.ini");
        var appInfoText = PortableAppsGenerator.Generate(input, existing);
        await File.WriteAllTextAsync(infoPath, appInfoText, new UTF8Encoding(true), token).ConfigureAwait(false);
        // Never execute source-provided download actions, installer settings, or custom hooks.
        ImageAssets.WriteIcons(input.LauncherIconPath, metadataDir);
        if (!File.Exists(Path.Combine(package, "help.html")))
            await File.WriteAllTextAsync(Path.Combine(package, "help.html"), "<!doctype html><html lang=\"en\"><meta charset=\"utf-8\"><title>Application help</title><h1>Application help</h1><p>Open the application's selected launch file. This package does not change where the application stores its settings.</p></html>", token).ConfigureAwait(false);
        Directory.CreateDirectory(Path.Combine(package, "Other", "Source"));
        foreach (var notice in new[] { (Resource: "AppWrap.LICENSE", Name: "AppWrap-LICENSE.txt"), (Resource: "AppWrap.NativeNotices", Name: "AppWrap-NATIVE-NOTICES.txt") })
        {
            using var resource = typeof(PortableBuild).Assembly.GetManifestResourceStream(notice.Resource) ?? throw new BuildFailureException("A packaging component is missing.", "Missing embedded notice: " + notice.Resource);
            using var noticeFile = new FileStream(Path.Combine(package, "Other", notice.Name), FileMode.CreateNew, FileAccess.Write);
            await resource.CopyToAsync(noticeFile, token).ConfigureAwait(false);
        }
        var guardPath = Path.Combine(stage, "AppWrap.UpgradeGuard.exe");
        using (var resource = typeof(PortableBuild).Assembly.GetManifestResourceStream("AppWrap.UpgradeGuard.exe") ?? throw new BuildFailureException("A packaging component is missing.", "Missing embedded upgrade guard."))
        using (var guardFile = File.Create(guardPath))
            await resource.CopyToAsync(guardFile, token).ConfigureAwait(false);
        // A folder consumed later by the official installer cannot reference the
        // staging path: the helper ships beside the hook and the hook addresses it
        // package-relatively. The compiled flow keeps the staging path, which its
        // packaging builder resolves during the same build.
        string guardHelperPath = guardPath;
        if (pafPackage)
        {
            File.Copy(guardPath, Path.Combine(package, "Other", "Source", "AppWrap.UpgradeGuard.exe"));
            guardHelperPath = PafGuardHelperPath;
        }
        // These settings are coupled to the trusted embedded guard. No caller-supplied hook is accepted.
        await File.WriteAllTextAsync(Path.Combine(package, "Other", "Source", "PortableApps.comInstallerCustom.nsh"), UpgradeGuardGenerator.Generate(input, guardHelperPath), new UTF8Encoding(true), token).ConfigureAwait(false);
        // A Pal profile replaces the basic launch shim with a real compiled PAL launcher.
        // PackagingOnly (or no profile) keeps the existing shim path unchanged below.
        PortableProfile? palProfile = input.Profile is { ExecutionKind: PortableExecutionKind.Pal } profile ? profile : null;
        var installerText = UpgradeGuardGenerator.InstallerSettings;
        if (palProfile is not null)
        {
            // P4: the profile's installer document is validated before anything is
            // written from it, and merged over the guard settings with the guard
            // always winning. A profile with no installer entries keeps the exact
            // guard-only text.
            var installerErrors = InstallerValidator.ValidateInstaller(palProfile, PortableAppsGenerator.PackageId(input))
                .Where(d => d.Severity == ProfileSeverity.Error).ToList();
            if (installerErrors.Count != 0)
                throw new UserInputException("The portable profile's installer settings are not ready to build: " +
                    string.Join(" ", installerErrors.Take(5).Select(d => d.Message)) +
                    (installerErrors.Count > 5 ? $" (+{installerErrors.Count - 5} more problems)" : ""));
            if (palProfile.Installer.AllEntries.Any())
                installerText = InstallerDocumentMerger.Merge(UpgradeGuardGenerator.InstallerSettings, palProfile.Installer);
            // The profile's metadata document is validated above (AppID conflicts,
            // PackageVersion shape) but deliberately does NOT replace the generated
            // appinfo.ini here: PortableAppsGenerator output stays canonical in P4,
            // and reconciling profile metadata into it is a P5 workspace concern.
        }
        await File.WriteAllTextAsync(Path.Combine(metadataDir, "installer.ini"), installerText, new UTF8Encoding(true), token).ConfigureAwait(false);
        string? launcherFileName = null;
        string? launcherText = null;
        if (palProfile is not null)
        {
            var errors = PortableProfileValidator.Validate(palProfile).Where(d => d.Severity == ProfileSeverity.Error).ToList();
            if (errors.Count != 0)
                throw new UserInputException("The portable profile is not ready to build: " +
                    string.Join(" ", errors.Take(5).Select(d => d.Message)) +
                    (errors.Count > 5 ? $" (+{errors.Count - 5} more problems)" : ""));
            var launcherDir = Path.Combine(metadataDir, "Launcher"); Directory.CreateDirectory(launcherDir);
            launcherFileName = PortableAppsGenerator.PackageId(input) + ".ini";
            launcherText = PortableLauncherGenerator.Generate(input, palProfile);
            await File.WriteAllTextAsync(Path.Combine(launcherDir, launcherFileName), launcherText, new UTF8Encoding(true), token).ConfigureAwait(false);
        }
        if (!compileLauncher) return package;
        var backend = Path.Combine(stage, "builder");
        var installedBackend = Path.Combine(baseDirectory, "third_party", "portableapps");
        if (!File.Exists(Path.Combine(installedBackend, "AppWrapPackagingBuilder.exe"))) throw new BuildFailureException("A packaging component is missing. Download a complete copy of AppWrap and try again.", "Missing portable packaging backend.");
        Copy( installedBackend, backend, token);
        if (palProfile is not null)
        {
            // The PAL compile needs only the staged package folder and produces the root
            // launcher inside it; it runs before the packaging builder, never instead of it.
            // Failure is fatal — a profile package never falls back to the basic shim.
            var palBackend = PalCompiler.BackendRoot(baseDirectory);
            await PalCompiler.CompileAsync(input, palProfile, package, stage, baseDirectory, token).ConfigureAwait(false);
            // P6: the compiled launcher is GPL-covered PAL code, so its corresponding
            // source/license bundle is written into the package before the packaging
            // builder runs. A backend too incomplete to bundle fails the build here,
            // after the compile, rather than shipping a launcher without its source.
            var bundle = await PalSourceBundle.WriteAsync(palBackend, package, palProfile,
                new PalSourceBundle.GeneratedFiles(launcherFileName!, launcherText!, installerText, appInfoText),
                PalCompiler.BuildArguments(input, palProfile, package, stage, palBackend), token).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(package, "Other", "AppWrap-PAL-NOTICES.txt"),
                PalSourceBundle.NoticesText(palProfile, bundle.GplTextBundled), new UTF8Encoding(true), token).ConfigureAwait(false);
        }
        else
        {
            var shimSource = Path.Combine(stage, "launcher.nsi");
            await File.WriteAllTextAsync(shimSource, LaunchShimGenerator.Generate(input, Path.Combine(package, PortableAppsGenerator.PackageId(input) + ".exe"), Path.Combine(metadataDir, "appicon.ico")), new UTF8Encoding(true), token).ConfigureAwait(false);
            var shim = await ProcessRunner.RunAsync(Path.Combine(backend, "App", "nsis", "makensis.exe"), ["/V2", shimSource], stage, token).ConfigureAwait(false);
            if (shim.ExitCode != 0) throw new BuildFailureException("AppWrap couldn’t prepare the app's portable launch file. Check the app folder and try again.", $"Launcher compiler exit code: {shim.ExitCode}\n{shim.Log}");
        }
        return package;
    }

    private static void Copy(string source, string destination, CancellationToken token)
    {
        InputRules.SafePath(source); Directory.CreateDirectory(destination);
        foreach (var path in Directory.EnumerateFileSystemEntries(source))
        {
            token.ThrowIfCancellationRequested(); InputRules.SafePath(path);
            if (Directory.Exists(path)) Copy(path, Path.Combine(destination, Path.GetFileName(path)), token);
            else File.Copy(path, Path.Combine(destination, Path.GetFileName(path)), overwrite: false);
        }
    }
}
