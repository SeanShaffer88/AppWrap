using System.Security.Cryptography;
using System.Text;
using AppWrap.Core;
using AppWrap.Core.PortableProfiles;

namespace AppWrap.Build;

/// <summary>
/// Writes the generated source/license bundle that accompanies a compiled PAL
/// launcher inside its package under <c>Other/Source</c> (P6). The compiled
/// launcher is GPL-covered PAL code, so the bundle carries the exact
/// corresponding launcher source copied at build time from the prepared pinned
/// backend (<c>third_party/pal</c>) — never copies embedded in this repository —
/// plus the generated configuration for this package, a build recipe with
/// SHA-256 hashes, the GPL text, and the plugin notices the backend ships.
///
/// Writing is deterministic (ordinal-sorted inventory, no timestamps) and
/// fail-loud: every required backend source file is checked <em>before</em>
/// anything is written, and a missing one aborts the build with a
/// <see cref="BuildFailureException"/> instead of emitting a partial bundle.
/// The one deliberate exception is the GPL text file itself: the pinned
/// backend ships it as <c>Other/Source/License.txt</c>, but if a backend ever
/// lacks it the bundle still succeeds and the notices say where the GPL text
/// lives upstream instead of fabricating license text.
/// </summary>
public static class PalSourceBundle
{
    /// <summary>Package-relative folder (forward slashes) holding the copied launcher source.</summary>
    public const string LauncherSourceFolder = "Other/Source/PortableApps.comLauncher";
    /// <summary>Package-relative folder holding this package's generated INI files.</summary>
    public const string GeneratedFolder = "Other/Source/AppWrap-Generated";
    /// <summary>Package-relative folder holding copied compiler/plugin notice files.</summary>
    public const string PluginNoticesFolder = "Other/Source/PluginNotices";
    /// <summary>Package-relative path of the build recipe and hash inventory.</summary>
    public const string RecipePath = "Other/Source/AppWrap-BUILD-RECIPE.md";

    public const string LauncherPageUrl = "https://portableapps.com/apps/development/portableapps.com_launcher";

    /// <summary>The generated configuration files for one package, exactly as written into it.</summary>
    public sealed record GeneratedFiles(string LauncherFileName, string LauncherIni, string InstallerIni, string AppInfoIni);

    /// <summary>Outcome of a bundle write: whether the backend's GPL text file was bundled.</summary>
    public sealed record BundleResult(bool GplTextBundled, IReadOnlyList<string> Files);

    public static string ArchiveFileName(EngineLock engine) => $"PortableApps.comLauncher_{engine.PalVersion}.paf.exe";

    public static string DownloadUrl(EngineLock engine) =>
        $"https://portableapps.com/redir2/?a=PortableApps.comLauncher&s=s&d=pa&f={ArchiveFileName(engine)}";

    private static string BackendSource(string backendRoot) => Path.Combine(backendRoot, "Other", "Source");

    /// <summary>
    /// Backend-relative descriptions of every required launcher-source item that is
    /// absent (or an empty directory where files are required). An empty result means
    /// the bundle can be written completely. The GPL text file is intentionally not
    /// required here; see the class remarks.
    /// </summary>
    public static IReadOnlyList<string> MissingRequiredFiles(string backendRoot)
    {
        var source = BackendSource(backendRoot);
        var missing = new List<string>();
        if (!File.Exists(Path.Combine(source, "PortableApps.comLauncher.nsi")))
            missing.Add(@"Other\Source\PortableApps.comLauncher.nsi");
        if (!DirectoryHasFiles(source, "*.nsh"))
            missing.Add(@"Other\Source\*.nsh (launcher includes)");
        if (!DirectoryHasFiles(Path.Combine(source, "Segments"), "*"))
            missing.Add(@"Other\Source\Segments\ (launcher segments)");
        if (!DirectoryHasFiles(Path.Combine(source, "Languages"), "*"))
            missing.Add(@"Other\Source\Languages\ (launcher languages)");
        if (!DirectoryHasFiles(Path.Combine(source, "Plugins"), "*"))
            missing.Add(@"Other\Source\Plugins\ (launcher plugins)");
        return missing;
    }

    private static bool DirectoryHasFiles(string directory, string pattern) =>
        Directory.Exists(directory) && Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly).Any();

    /// <summary>
    /// Copies the corresponding source and generated files into
    /// <paramref name="packageDirectory"/> and writes the recipe last (its inventory
    /// hashes the bundled bytes). Returns what was written, package-relative with
    /// forward slashes, in write order; the recipe itself is not in the list.
    /// </summary>
    public static async Task<BundleResult> WriteAsync(string backendRoot, string packageDirectory, PortableProfile profile,
        GeneratedFiles generated, IReadOnlyList<string> compilerArguments, CancellationToken token)
    {
        var missing = MissingRequiredFiles(backendRoot);
        if (missing.Count != 0)
            throw new BuildFailureException("A packaging component is missing. Download a complete copy of AppWrap and try again.",
                "The pinned PortableApps.com Launcher backend is incomplete, so the source bundle cannot be written. Missing: " +
                string.Join("; ", missing) + " under " + backendRoot + ". Re-run tools/Prepare-PalBackend.ps1. Nothing was bundled.");

        var source = BackendSource(backendRoot);
        var written = new List<(string PackagePath, string Sha256)>();

        async Task CopyAsync(string from, string packagePath)
        {
            token.ThrowIfCancellationRequested();
            InputRules.SafePath(from);
            var to = PackageFile(packageDirectory, packagePath);
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            var bytes = await File.ReadAllBytesAsync(from, token).ConfigureAwait(false);
            await File.WriteAllBytesAsync(to, bytes, token).ConfigureAwait(false);
            written.Add((packagePath, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()));
        }

        Task WriteTextAsync(string packagePath, string text)
        {
            var bytes = new UTF8Encoding(true).GetBytes(text);
            var to = PackageFile(packageDirectory, packagePath);
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.WriteAllBytes(to, bytes);
            written.Add((packagePath, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()));
            return Task.CompletedTask;
        }

        // (a) Corresponding launcher source, exactly as the compile consumed it:
        // the script, its top-level includes, segments, languages and the plugin
        // binaries its !addplugindir directive makes available.
        await CopyAsync(Path.Combine(source, "PortableApps.comLauncher.nsi"), LauncherSourceFolder + "/PortableApps.comLauncher.nsi").ConfigureAwait(false);
        var gplTextBundled = File.Exists(Path.Combine(source, "License.txt"));
        if (gplTextBundled)
            await CopyAsync(Path.Combine(source, "License.txt"), LauncherSourceFolder + "/License.txt").ConfigureAwait(false);
        if (File.Exists(Path.Combine(source, "Readme.txt")))
            await CopyAsync(Path.Combine(source, "Readme.txt"), LauncherSourceFolder + "/Readme.txt").ConfigureAwait(false);
        foreach (var include in SortedFiles(source, "*.nsh"))
            await CopyAsync(include, LauncherSourceFolder + "/" + Path.GetFileName(include)).ConfigureAwait(false);
        foreach (var segment in SortedFiles(Path.Combine(source, "Segments"), "*"))
            await CopyAsync(segment, LauncherSourceFolder + "/Segments/" + Relative(Path.Combine(source, "Segments"), segment)).ConfigureAwait(false);
        foreach (var language in SortedFiles(Path.Combine(source, "Languages"), "*"))
            await CopyAsync(language, LauncherSourceFolder + "/Languages/" + Relative(Path.Combine(source, "Languages"), language)).ConfigureAwait(false);
        foreach (var plugin in SortedFiles(Path.Combine(source, "Plugins"), "*"))
            await CopyAsync(plugin, LauncherSourceFolder + "/Plugins/" + Path.GetFileName(plugin)).ConfigureAwait(false);

        // (d) Compiler/plugin notices the backend ships. The pinned backend carries
        // no license or readme file for the UAC or XML plugins; the recipe says so
        // rather than inventing one.
        var nsisCopying = Path.Combine(backendRoot, "App", "NSIS", "COPYING");
        if (File.Exists(nsisCopying))
            await CopyAsync(nsisCopying, PluginNoticesFolder + "/NSIS-COPYING.txt").ConfigureAwait(false);
        var textReplaceReadme = Path.Combine(backendRoot, "App", "NSIS", "Docs", "NewTextReplace", "Readme.txt");
        if (File.Exists(textReplaceReadme))
            await CopyAsync(textReplaceReadme, PluginNoticesFolder + "/NewTextReplace-Readme.txt").ConfigureAwait(false);

        // (b) This package's generated configuration, byte-identical to the files
        // the package itself carries.
        await WriteTextAsync(GeneratedFolder + "/" + generated.LauncherFileName, generated.LauncherIni).ConfigureAwait(false);
        await WriteTextAsync(GeneratedFolder + "/installer.ini", generated.InstallerIni).ConfigureAwait(false);
        await WriteTextAsync(GeneratedFolder + "/appinfo.ini", generated.AppInfoIni).ConfigureAwait(false);

        // (c) The recipe, written last so its inventory can hash everything above.
        var recipeBytes = new UTF8Encoding(true).GetBytes(RecipeText(profile, compilerArguments, written, gplTextBundled));
        var recipeFile = PackageFile(packageDirectory, RecipePath);
        Directory.CreateDirectory(Path.GetDirectoryName(recipeFile)!);
        await File.WriteAllBytesAsync(recipeFile, recipeBytes, token).ConfigureAwait(false);

        return new BundleResult(gplTextBundled, [.. written.Select(w => w.PackagePath), RecipePath]);
    }

    private static string PackageFile(string packageDirectory, string packagePath) =>
        Path.Combine([packageDirectory, .. packagePath.Split('/')]);

    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

    private static List<string> SortedFiles(string directory, string pattern) =>
        Directory.EnumerateFiles(directory, pattern, SearchOption.AllDirectories)
            .OrderBy(p => Relative(directory, p), StringComparer.Ordinal).ToList();

    /// <summary>
    /// The build recipe: pinned engine identity and hashes, where the source came
    /// from, how the backend was prepared, the exact compiler argument array, and
    /// the bundle inventory with SHA-256 per file. Pure and deterministic.
    /// </summary>
    public static string RecipeText(PortableProfile profile, IReadOnlyList<string> compilerArguments,
        IReadOnlyList<(string PackagePath, string Sha256)> inventory, bool gplTextBundled)
    {
        var engine = profile.Engine;
        var builder = new StringBuilder();
        builder.AppendLine("# AppWrap build recipe — PortableApps.com Launcher source bundle");
        builder.AppendLine();
        builder.AppendLine("This package's root launcher was compiled by AppWrap from the official");
        builder.AppendLine("PortableApps.com Launcher (PAL) source. This `Other/Source` folder accompanies");
        builder.AppendLine("the compiled launcher with its corresponding source, the generated");
        builder.AppendLine("configuration for this package, and the applicable license texts.");
        builder.AppendLine();
        builder.AppendLine("## Pinned engine");
        builder.AppendLine();
        builder.AppendLine($"- PAL version: {engine.PalVersion}");
        builder.AppendLine($"- PAL archive: {ArchiveFileName(engine)}");
        builder.AppendLine($"- PAL archive SHA-256: {engine.PalSha256}");
        builder.AppendLine($"- NSIS version: {engine.NsisVersion}");
        builder.AppendLine($"- Bundled makensis.exe SHA-256: {engine.NsisSha256}");
        builder.AppendLine();
        builder.AppendLine("## Source");
        builder.AppendLine();
        builder.AppendLine($"- PAL page: {LauncherPageUrl}");
        builder.AppendLine($"- Pinned archive download: {DownloadUrl(engine)}");
        builder.AppendLine("- Backend preparation on the build machine (before building):");
        builder.AppendLine("  `powershell -File tools/Prepare-PalBackend.ps1` — downloads the pinned");
        builder.AppendLine("  archive, verifies its SHA-256 against the pin, extracts it statically");
        builder.AppendLine("  with 7-Zip (the archive is never executed) into `third_party/pal`, and");
        builder.AppendLine("  verifies the bundled makensis.exe hash. See `third_party/pal/provenance.json`.");
        builder.AppendLine("- AppWrap re-verifies the makensis.exe SHA-256 against the pin before running it.");
        builder.AppendLine();
        builder.AppendLine("## Compile invocation");
        builder.AppendLine();
        builder.AppendLine("makensis.exe was run with this exact argument array (one argument per line,");
        builder.AppendLine("the launcher script last). The package and log paths name the build machine's");
        builder.AppendLine("staging folders; the argument shape is the pinned contract.");
        builder.AppendLine();
        foreach (var argument in compilerArguments) builder.AppendLine($"  {argument}");
        builder.AppendLine();
        builder.AppendLine("## Bundle contents (SHA-256)");
        builder.AppendLine();
        foreach (var (path, hash) in inventory.OrderBy(i => i.PackagePath, StringComparer.Ordinal))
            builder.AppendLine($"- `{path}` — {hash}");
        builder.AppendLine();
        builder.AppendLine("The launcher source under `PortableApps.comLauncher/` is copied unchanged");
        builder.AppendLine("from the prepared pinned backend at build time: the launcher script, its");
        builder.AppendLine("top-level `.nsh` includes, `Segments/`, `Languages/`, and the plugin binaries");
        builder.AppendLine("in `Plugins/` that the script's `!addplugindir` directive makes available");
        builder.AppendLine("(UAC, NewTextReplace and XML plugins). The wizard/generator files in the");
        builder.AppendLine("backend's source folder are not part of the launcher compile and are not");
        builder.AppendLine("bundled. `AppWrap-Generated/` holds the exact launcher INI, merged");
        builder.AppendLine("installer.ini and appinfo.ini written into this package.");
        builder.AppendLine();
        builder.AppendLine("## Licenses");
        builder.AppendLine();
        if (gplTextBundled)
        {
            builder.AppendLine("- The launcher and its source are licensed under the GNU General Public");
            builder.AppendLine("  License, version 2 or (at your option) any later version. The full GPL v2");
            builder.AppendLine($"  text is bundled at `{LauncherSourceFolder}/License.txt`, copied from the");
            builder.AppendLine("  pinned PAL release.");
        }
        else
        {
            builder.AppendLine("- The launcher and its source are licensed under the GNU General Public");
            builder.AppendLine("  License, version 2 or (at your option) any later version. The prepared");
            builder.AppendLine("  backend for this build did not include a standalone GPL text file, so no");
            builder.AppendLine("  license text is bundled here; the launcher source files carry the standard");
            builder.AppendLine($"  GPL notice header (see `{LauncherSourceFolder}/PortableApps.comLauncher.nsi`),");
            builder.AppendLine($"  and the full GPL v2 text is published with the PAL source at {LauncherPageUrl}.");
        }
        builder.AppendLine("- The NSIS compiler's own license (zlib/libpng style) is bundled at");
        builder.AppendLine($"  `{PluginNoticesFolder}/NSIS-COPYING.txt` when the backend ships it.");
        builder.AppendLine("- Plugin notices: the NewTextReplace plugin's readme is bundled at");
        builder.AppendLine($"  `{PluginNoticesFolder}/NewTextReplace-Readme.txt` when the backend ships it.");
        builder.AppendLine("  The pinned backend ships no separate license or readme file for the UAC or");
        builder.AppendLine("  XML plugins; those binaries are inventoried above with their hashes and are");
        builder.AppendLine("  distributed as part of the pinned PAL release.");
        builder.AppendLine("- AppWrap itself is MIT-licensed (`Other/AppWrap-LICENSE.txt`). The packaged");
        builder.AppendLine("  application's own license and trademarks are separate and unchanged.");
        return builder.ToString();
    }

    /// <summary>
    /// The short notices file kept at the package's established
    /// <c>Other/AppWrap-PAL-NOTICES.txt</c> path (since P1). Since P6 it points at
    /// the accompanying source bundle instead of deferring it.
    /// </summary>
    public static string NoticesText(PortableProfile profile, bool gplTextBundled)
    {
        var engine = profile.Engine;
        var gplLine = gplTextBundled
            ? $"The full GPL v2 text accompanies it at Other\\Source\\PortableApps.comLauncher\\License.txt."
            : $"The prepared backend for this build included no standalone GPL text file; the full GPL v2 text is published with the PAL source at {LauncherPageUrl}.";
        return $"""
            PortableApps.com Launcher (PAL) — generated package notices

            This package's root launcher was compiled by AppWrap from the official
            PortableApps.com Launcher (PAL) {engine.PalVersion} source, using the NSIS
            {engine.NsisVersion} compiler bundled with that PAL release.

            Pinned source archive: {ArchiveFileName(engine)}
            Archive SHA-256: {engine.PalSha256}
            Bundled makensis.exe SHA-256: {engine.NsisSha256}
            Source: {LauncherPageUrl}

            The PortableApps.com Launcher is licensed under the GNU General Public License,
            version 2 or (at your option) any later version (GPL-2.0-or-later). The compiled
            launcher in this package is covered by that license; it is not covered by
            AppWrap's MIT license. {gplLine}

            The corresponding source for the compiled launcher accompanies this package
            under Other\Source: the pinned launcher script, segments, includes and
            languages (Other\Source\PortableApps.comLauncher), the exact generated
            launcher/installer/appinfo configuration for this package
            (Other\Source\AppWrap-Generated), the build recipe with per-file SHA-256
            hashes (Other\Source\AppWrap-BUILD-RECIPE.md), and the compiler/plugin
            notices the pinned backend ships (Other\Source\PluginNotices).

            """;
    }
}
