using AppWrap.Core;
using AppWrap.Core.PortableProfiles;

namespace AppWrap.Generators;

/// <summary>
/// Serializes a validated portable profile into the PAL launcher INI text and derives the
/// compile-time defines for the pinned PAL compiler. The profile's launcher document is the
/// canonical model: generation edits it through <see cref="IniDocument.SetValue"/> so
/// comments, ordering and unknown entries survive. PAL paths are relative to the package's
/// <c>App</c> folder; in AppWrap packages the payload lives under <c>App\Payload</c>, so
/// <c>ProgramExecutable</c> must name an executable under <c>Payload\</c>.
/// </summary>
public static class PortableLauncherGenerator
{
    public static string AppId(AppWrapInput input) => PortableAppsGenerator.PackageId(input);

    public static string Generate(AppWrapInput input, PortableProfile profile)
    {
        var doc = profile.Launcher;
        var existing = doc.GetValue("Launch", "ProgramExecutable");
        if (existing is null)
        {
            doc.SetValue("Launch", "ProgramExecutable", @"Payload\" + input.MainExecutable);
        }
        else
        {
            ValidateProgramExecutable(existing);
        }
        return doc.Serialize();
    }

    private static void ValidateProgramExecutable(string value)
    {
        // Same literal-safety rule the other portable generators apply to launch files:
        // the value is data for the PAL compiler, never a shell or preprocessor fragment.
        if (value.Length == 0 || value.Any(char.IsControl) || value.IndexOfAny(['$', '`', '"']) >= 0 || value.Contains("{#", StringComparison.Ordinal))
            throw new UserInputException("Choose a launch file without quote, dollar, or backtick symbols for portable packaging.");
        var normalized = value.Replace('/', '\\');
        if (normalized.StartsWith('\\') || (normalized.Length >= 2 && char.IsAsciiLetter(normalized[0]) && normalized[1] == ':') || normalized.Contains(':'))
            throw new UserInputException("ProgramExecutable must live under App\\Payload in AppWrap packages: use a path such as Payload\\App.exe, not a rooted or drive path.");
        var segments = normalized.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(s => s == ".."))
            throw new UserInputException("ProgramExecutable must live under App\\Payload in AppWrap packages: parent-folder steps are not allowed.");
        if (segments.Length < 2 || !segments[0].Equals("Payload", StringComparison.OrdinalIgnoreCase))
            throw new UserInputException("ProgramExecutable must live under App\\Payload in AppWrap packages: start the path with Payload\\ and name the application file below it.");
        if (!segments[^1].EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new UserInputException("ProgramExecutable must name an .exe file under App\\Payload in AppWrap packages.");
    }

    /// <summary>
    /// Compile defines implied by the profile, in the pinned compiler's documented order:
    /// <c>XML_ENABLED</c> when [Activate] XML=true, then <c>RUNASADMIN_COMPILEFORCE</c> when
    /// any [Launch] RunAsAdmin* value is <c>compileforce</c> (both case-insensitive).
    /// </summary>
    public static IReadOnlyList<string> ConditionalDefines(PortableProfile profile)
    {
        var defines = new List<string>();
        var doc = profile.Launcher;
        if (string.Equals(doc.GetValue("Activate", "XML"), "true", StringComparison.OrdinalIgnoreCase))
            defines.Add("XML_ENABLED");
        var compileForce = doc.Entries("Launch").Any(e =>
            e.Key.StartsWith("RunAsAdmin", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(e.Value, "compileforce", StringComparison.OrdinalIgnoreCase));
        if (compileForce) defines.Add("RUNASADMIN_COMPILEFORCE");
        return defines;
    }
}

/// <summary>
/// Builds the exact argument array for the pinned PAL <c>makensis.exe</c> invocation
/// (see docs/portable-capability-matrix.md, "Pinned evidence and compiler"). The compiler
/// executable itself is not part of the array; the launcher script path is always last.
/// Arguments are separate argv entries — never a shell string.
/// </summary>
public static class PalLaunchArguments
{
    public static string[] Build(string backend, string package, string logPath, string namePortable, string appId, string version, IReadOnlyList<string> defines)
    {
        var args = new List<string>
        {
            "/O" + logPath,
            "/DPACKAGE=" + package,
            "/DNamePortable=" + namePortable,
            "/DAppID=" + appId,
            "/DVersion=" + version,
        };
        foreach (var define in defines) args.Add("/D" + define);
        args.Add(Path.Combine(backend, "Other", "Source", "PortableApps.comLauncher.nsi"));
        return [.. args];
    }
}
