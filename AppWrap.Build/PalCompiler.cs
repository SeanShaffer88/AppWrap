using System.Security.Cryptography;
using AppWrap.Core;
using AppWrap.Core.PortableProfiles;
using AppWrap.Generators;

namespace AppWrap.Build;

/// <summary>
/// Direct adapter for the pinned official PortableApps.com Launcher (PAL) compiler.
/// The backend lives under <c>third_party/pal</c> beside AppWrap (prepared by
/// <c>tools/Prepare-PalBackend.ps1</c>); its bundled NSIS 2.46.5-Unicode
/// <c>makensis.exe</c> is hash-verified against <see cref="EngineLock.Pinned"/> before it
/// is ever run. Compilation failure is fatal: there is never a fallback to the basic
/// packaging shim for a profile (PAL) package.
/// </summary>
public static class PalCompiler
{
    public static async Task<string> CompileAsync(AppWrapInput input, PortableProfile profile, string package, string stage, string baseDirectory, CancellationToken token)
    {
        var backend = Path.Combine(baseDirectory, "third_party", "pal");
        var compiler = Path.Combine(backend, "App", "NSIS", "makensis.exe");
        var script = Path.Combine(backend, "Other", "Source", "PortableApps.comLauncher.nsi");
        if (!File.Exists(compiler) || !File.Exists(script))
            throw new BuildFailureException("A packaging component is missing. Download a complete copy of AppWrap and try again.", "Missing pinned PortableApps.com Launcher backend under: " + backend);

        string hash;
        await using (var stream = File.OpenRead(compiler))
            hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false));
        if (!string.Equals(hash, EngineLock.Pinned.NsisSha256, StringComparison.OrdinalIgnoreCase))
            throw new BuildFailureException("A packaging component is not the pinned version. Download a complete copy of AppWrap and try again.",
                $"Pinned PAL compiler hash mismatch. Expected makensis.exe SHA-256 {EngineLock.Pinned.NsisSha256}, found {hash.ToLowerInvariant()}. The compiler was not run.");

        var appId = PortableLauncherGenerator.AppId(input);
        var namePortable = SafeName(input.AppName + " Portable");
        var logPath = Path.Combine(stage, "pal-compiler.log");
        var args = PalLaunchArguments.Build(backend, package, logPath, namePortable, appId, profile.Engine.PalVersion,
            PortableLauncherGenerator.ConditionalDefines(profile));
        var result = await ProcessRunner.RunAsync(compiler, args, stage, token).ConfigureAwait(false);
        var log = await BackendLog.ReadAsync(logPath, token).ConfigureAwait(false);
        var launcher = Path.Combine(package, appId + ".exe");
        if (result.ExitCode != 0 || !File.Exists(launcher) || new FileInfo(launcher).Length <= 10000)
            throw new BuildFailureException("AppWrap couldn’t build the portable launcher. Check the app folder and try again.",
                $"PAL compiler exit code: {result.ExitCode}\n{result.Log}\n{log}");
        return launcher;
    }

    // Mirrors the local Safe check in PortableAppsGenerator.Generate: names handed to the
    // compiler as /D defines must never carry quote, dollar, backtick or "{#" fragments.
    private static string SafeName(string value)
    {
        if (value.Any(char.IsControl) || value.IndexOfAny(['$', '`', '"']) >= 0 || value.Contains("{#", StringComparison.Ordinal))
            throw new UserInputException("For a portable package, choose a name and launch file without quote, dollar, or backtick symbols.");
        return value;
    }
}
