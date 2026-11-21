using AppWrap.Core;
using AppWrap.Generators;
using AppWrap.Imaging;
using System.Text;
namespace AppWrap.Build;
internal static class PortableBuild
{
    public static async Task<string> BuildAsync(AppWrapInput input, string payload, string stage, string baseDirectory, CancellationToken token)
    {
        var sourceHooks = Path.Combine(payload, "Other", "Source");
        if (Directory.Exists(sourceHooks) && Directory.EnumerateFiles(sourceHooks, "*", SearchOption.AllDirectories).Any(p => Path.GetExtension(p).Equals(".nsh", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(p).Equals(".nsi", StringComparison.OrdinalIgnoreCase)))
            throw new UserInputException("This app contains custom packaging instructions. Choose its application folder without those instructions.");
        var package = Path.Combine(stage, PortableAppsGenerator.PackageId(input));
        Directory.CreateDirectory(Path.Combine(package, "App"));
        var previousInfo = Path.Combine(payload, "App", "AppInfo", "appinfo.ini");
        if (File.Exists(previousInfo) && new FileInfo(previousInfo).Length > 65536) throw new UserInputException("The app's existing package details are too large.");
        var existing = File.Exists(previousInfo) ? await File.ReadAllTextAsync(previousInfo, token).ConfigureAwait(false) : null;
        Directory.Move(payload, Path.Combine(package, "App", "Payload"));
        var metadataDir = Path.Combine(package, "App", "AppInfo"); Directory.CreateDirectory(metadataDir);
        var infoPath = Path.Combine(metadataDir, "appinfo.ini");
        await File.WriteAllTextAsync(infoPath, PortableAppsGenerator.Generate(input, existing), new UTF8Encoding(true), token).ConfigureAwait(false);
        // Never execute source-provided download actions, installer settings, or custom hooks.
        await File.WriteAllTextAsync(Path.Combine(metadataDir, "installer.ini"), "[Source]\r\nIncludeInstallerSource=false\r\n[MainDirectories]\r\nRemoveAppDirectory=false\r\nRemoveOtherDirectory=false\r\nRemoveDataDirectory=false\r\n", new UTF8Encoding(true), token).ConfigureAwait(false);
        ImageAssets.WriteIcons(input.LauncherIconPath, metadataDir);
        if (!File.Exists(Path.Combine(package, "help.html")))
            await File.WriteAllTextAsync(Path.Combine(package, "help.html"), "<!doctype html><html lang=\"en\"><meta charset=\"utf-8\"><title>Application help</title><h1>Application help</h1><p>Open the application's selected launch file. This package does not change where the application stores its settings.</p></html>", token).ConfigureAwait(false);
        Directory.CreateDirectory(Path.Combine(package, "Other", "Source"));
        var backend = Path.Combine(stage, "builder");
        var installedBackend = Path.Combine(baseDirectory, "third_party", "portableapps");
        if (!File.Exists(Path.Combine(installedBackend, "AppWrapPackagingBuilder.exe"))) throw new BuildFailureException("A packaging component is missing. Download a complete copy of AppWrap and try again.", "Missing portable packaging backend.");
        Copy( installedBackend, backend, token);
        var shimSource = Path.Combine(stage, "launcher.nsi");
        await File.WriteAllTextAsync(shimSource, LaunchShimGenerator.Generate(input, Path.Combine(package, PortableAppsGenerator.PackageId(input) + ".exe"), Path.Combine(metadataDir, "appicon.ico")), new UTF8Encoding(true), token).ConfigureAwait(false);
        var shim = await ProcessRunner.RunAsync(Path.Combine(backend, "App", "nsis", "makensis.exe"), ["/V2", shimSource], stage, token).ConfigureAwait(false);
        if (shim.ExitCode != 0) throw new BuildFailureException("AppWrap couldn’t prepare the app's portable launch file. Check the app folder and try again.", $"Launcher compiler exit code: {shim.ExitCode}\n{shim.Log}");
        if (input.SidebarImagePath != null) ImageAssets.WriteSidebar(input.SidebarImagePath, Path.Combine(backend, "App", "installer", "PortableApps.comInstaller.bmp"));
        var result = await ProcessRunner.RunAsync(Path.Combine(backend, "AppWrapPackagingBuilder.exe"), [package], backend, token).ConfigureAwait(false);
        var logPath = Path.Combine(backend, "Data", "PortableApps.comInstallerLog.txt");
        var log = "";
        if (File.Exists(logPath)) { using var logReader = new StreamReader(logPath, Encoding.Unicode, true); var buffer = new char[131072]; var read = await logReader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false); log = new string(buffer, 0, read) + (read < buffer.Length ? "" : "\n[log truncated]"); }
        var artifacts = Directory.GetFiles(stage, "*.paf.exe", SearchOption.TopDirectoryOnly);
        if (result.ExitCode != 0 || artifacts.Length != 1) throw new BuildFailureException("AppWrap couldn’t build the portable package. Check the app folder and try again.", $"Builder exit code: {result.ExitCode}\n{result.Log}\n{log}");
        return artifacts[0];
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
