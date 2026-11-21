namespace AppWrap.Core;

public enum PackageNamingStyle { NameVersionSetup, NameOnly, NameSetupVersion }

/// <summary>Pure output naming rules; no filesystem reads, writes, or process startup.</summary>
public static class PackageNaming
{
    public static IReadOnlyList<string> Labels { get; } = Array.AsReadOnly(new[]
    {
        "Name-Version-Setup", "Name_Setup", "Name-Setup-Version"
    });

    public static string GetFileName(string name, string version, OutputMode mode,
        PackageNamingStyle style = PackageNamingStyle.NameVersionSetup)
    {
        ValidateName(name);
        if (string.IsNullOrWhiteSpace(version)) throw new UserInputException("Enter the app version before choosing a package name.");
        _ = InputRules.PackageVersion(version);
        if (mode is not OutputMode.Standard and not OutputMode.PortableApps)
            throw new UserInputException("Choose an installer or portable package format.");

        var portable = mode == OutputMode.PortableApps;
        var fileName = style switch
        {
            PackageNamingStyle.NameOnly => portable ? $"{name}Portable.paf.exe" : $"{name}_Setup.exe",
            PackageNamingStyle.NameSetupVersion => portable ? $"{name}-Portable-{version}.paf.exe" : $"{name}-Setup-{version}.exe",
            // An unknown saved preference follows the current default rather than failing.
            _ => portable ? $"{name}-{version}-Portable.paf.exe" : $"{name}-{version}-Setup.exe"
        };
        if (fileName.Length > 255 || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new UserInputException("Choose a shorter app name or version for the package filename.");
        return fileName;
    }

    public static void ValidateName(string name)
    {
        InputRules.Text(name, "name");
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith(' ') || name.EndsWith('.'))
            throw new UserInputException("Use an app name without file-name symbols or a space or period at the end.");
        var baseName = name.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        bool numberedDevice = baseName.Length == 4 &&
            (baseName.StartsWith("COM", StringComparison.Ordinal) || baseName.StartsWith("LPT", StringComparison.Ordinal)) &&
            (baseName[3] is >= '1' and <= '9' or '¹' or '²' or '³');
        if (baseName is "CON" or "PRN" or "AUX" or "NUL" || numberedDevice)
            throw new UserInputException("Choose a different app name. Windows reserves this name for a device.");
    }
}
