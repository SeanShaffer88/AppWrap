namespace AppWrap.Core;

public enum OutputMode { Standard, PortableApps }

public sealed record AppWrapInput(
    string SourceFolder, string AppName, string AppVersion, string MainExecutable,
    bool CreateStartMenuShortcut, bool CreateDesktopShortcut, string OutputPath,
    OutputMode Mode = OutputMode.Standard, string? LauncherIconPath = null, string? SidebarImagePath = null,
    StandardInstallerOptions? StandardOptions = null);

public sealed class UserInputException(string message) : Exception(message);

public static class InputRules
{
    public static string SafeId(string name)
    {
        var letters = new string(name.Where(c => char.IsAsciiLetterOrDigit(c)).ToArray());
        if (letters.Length == 0) letters = "Application";
        return letters[..Math.Min(letters.Length, 50)] + "-" +
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(name)))[..12];
    }

    public static string PackageVersion(string version)
    {
        var parts = version.Split('.');
        if (parts.Length is < 1 or > 4 || parts.Any(p => p.Length == 0 || !p.All(char.IsAsciiDigit) || !ushort.TryParse(p, out _)))
            throw new UserInputException("Use a version such as 1.0 or 1.2.3.4, with numbers from 0 to 65535.");
        return string.Join('.', parts.Select(p => ushort.Parse(p).ToString()).Concat(Enumerable.Repeat("0", 4 - parts.Length)));
    }

    public static void Text(string value, string label, int max = 120)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > max || value.Any(char.IsControl) || value.Contains("{#", StringComparison.Ordinal))
            throw new UserInputException($"Enter a {label} using 1–{max} printable characters.");
    }

    public static bool IsWithin(string path, string root) =>
        Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public static void SafePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith("\\\\", StringComparison.Ordinal) || path.Any(char.IsControl) || path.Contains("{#", StringComparison.Ordinal))
            throw new UserInputException("Choose a folder or file on a local drive.");
        var full = Path.GetFullPath(path);
        if (full.AsSpan(2).Contains(':')) throw new UserInputException("Choose a regular file path.");
        for (var current = full; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new UserInputException("Choose a folder without linked files or folders.");
    }

    public static IReadOnlyList<string> Executables(string source)
    {
        SafePath(source);
        if (!Directory.Exists(source)) throw new UserInputException("Choose the folder containing your application.");
        var result = new List<string>();
        Walk(source, source, result, 0, new Counter());
        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    private sealed class Counter { public int Count; }
    private static void Walk(string root, string directory, List<string> result, int depth, Counter count)
    {
        if (depth > 40) throw new UserInputException("This folder has too many nested folders.");
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            if (++count.Count > 50000) throw new UserInputException("Choose a smaller application folder (up to 50,000 files and folders).");
            SafePath(entry);
            if (Directory.Exists(entry)) Walk(root, entry, result, depth + 1, count);
            else if (Path.GetExtension(entry).Equals(".exe", StringComparison.OrdinalIgnoreCase)) result.Add(Path.GetRelativePath(root, entry));
        }
    }

    public static void Validate(AppWrapInput input)
    {
        if (input.Mode == OutputMode.Standard) StandardInstallerOptions.Normalize(input.StandardOptions);
        PackageNaming.ValidateName(input.AppName); PackageVersion(input.AppVersion);
        SafePath(input.SourceFolder); SafePath(input.OutputPath);
        if (!Directory.Exists(input.SourceFolder)) throw new UserInputException("Choose an application folder that still exists.");
        if (Path.IsPathRooted(input.MainExecutable) || !IsWithin(Path.Combine(input.SourceFolder, input.MainExecutable), input.SourceFolder) ||
            !File.Exists(Path.Combine(input.SourceFolder, input.MainExecutable)) || !input.MainExecutable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new UserInputException("Choose an application file from the selected folder.");
        SafePath(Path.Combine(input.SourceFolder, input.MainExecutable));
        if (IsWithin(input.OutputPath, input.SourceFolder) || string.Equals(Path.GetFullPath(input.OutputPath), Path.GetFullPath(input.SourceFolder), StringComparison.OrdinalIgnoreCase))
            throw new UserInputException("Save your package outside the application folder.");
        if (!input.OutputPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new UserInputException("Save the package with an .exe file name.");
        if (!Directory.Exists(Path.GetDirectoryName(Path.GetFullPath(input.OutputPath)))) throw new UserInputException("Choose an existing folder for the finished package.");
        foreach (var image in new[] { input.LauncherIconPath, input.SidebarImagePath })
            if (!string.IsNullOrEmpty(image)) { SafePath(image); if (!File.Exists(image)) throw new UserInputException("Choose an image that still exists, or skip it."); }
    }
}
