namespace AppWrap.Core.Pipeline;

/// <summary>
/// The concrete host locations the path mapper tokenizes against. The
/// Build layer fills this from the real machine (Environment folders, the
/// package being built); synthesis uses the pattern entry point instead,
/// which needs no machine at all.
/// </summary>
public sealed record PalPathContext(
    string UserProfile,
    string RoamingAppData,
    string LocalAppData,
    string ProgramData,
    string ProgramFiles,
    string? ProgramFilesX86,
    string WindowsDirectory,
    string? AppDirectory,
    string? DataDirectory);

/// <summary>
/// Maps absolute host paths to the PAL/environment-variable form the
/// launcher understands — the same token style the audit synthesizer
/// already emits (%APPDATA%, %LOCALAPPDATA%, %PAL:AppDir%, …). Two entry
/// points: <see cref="ToPalPath"/> tokenizes against a concrete machine
/// context (longest matching root wins), and
/// <see cref="TokenizeKnownRoots"/> recognizes the standard roots by
/// path shape alone, for machine-independent work like draft synthesis.
/// Unrecognized paths come back unchanged; the mapper never guesses.
/// </summary>
public static class PalPathMapper
{
    public static string ToPalPath(string absolutePath, PalPathContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var path = absolutePath.Replace('/', '\\');
        var roots = new List<(string Root, string Token)>();
        void Add(string? root, string token)
        {
            if (!string.IsNullOrWhiteSpace(root)) roots.Add((root.Replace('/', '\\').TrimEnd('\\'), token));
        }
        Add(context.AppDirectory, "%PAL:AppDir%");
        Add(context.DataDirectory, "%PAL:DataDir%");
        Add(context.RoamingAppData, "%APPDATA%");
        Add(context.LocalAppData, "%LOCALAPPDATA%");
        Add(context.ProgramData, "%PROGRAMDATA%");
        Add(context.ProgramFilesX86, "%PROGRAMFILES(X86)%");
        Add(context.ProgramFiles, "%PROGRAMFILES%");
        Add(context.UserProfile, "%USERPROFILE%");
        Add(context.WindowsDirectory, "%WINDIR%");
        foreach (var (root, token) in roots.OrderByDescending(r => r.Root.Length))
        {
            if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase)) return token;
            if (path.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase))
                return token + path[root.Length..];
        }
        return absolutePath;
    }

    /// <summary>
    /// Pattern form: <c>C:\Users\&lt;name&gt;\AppData\Roaming\…</c> →
    /// <c>%APPDATA%\…</c>, <c>…\AppData\Local\…</c> → %LOCALAPPDATA%,
    /// <c>C:\Users\&lt;name&gt;\…</c> → %USERPROFILE%, any drive's
    /// <c>Program Files</c> / <c>Program Files (x86)</c> / <c>ProgramData</c>
    /// roots → their tokens. Anything else is returned unchanged.
    /// </summary>
    public static string TokenizeKnownRoots(string absolutePath)
    {
        var path = absolutePath.Replace('/', '\\');
        var segments = path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2 || !segments[0].EndsWith(':')) return absolutePath;
        var rest = segments.Skip(1).ToArray();
        string Join(int from) => string.Join("\\", rest.Skip(from));
        if (rest.Length >= 2 && rest[0].Equals("ProgramData", StringComparison.OrdinalIgnoreCase))
            return "%PROGRAMDATA%" + (rest.Length > 1 ? "\\" + Join(1) : "");
        if (rest.Length >= 1 && rest[0].Equals("Program Files (x86)", StringComparison.OrdinalIgnoreCase))
            return "%PROGRAMFILES(X86)%" + (rest.Length > 1 ? "\\" + Join(1) : "");
        if (rest.Length >= 1 && rest[0].Equals("Program Files", StringComparison.OrdinalIgnoreCase))
            return "%PROGRAMFILES%" + (rest.Length > 1 ? "\\" + Join(1) : "");
        if (rest.Length >= 4 && rest[0].Equals("Users", StringComparison.OrdinalIgnoreCase)
            && rest[2].Equals("AppData", StringComparison.OrdinalIgnoreCase)
            && rest[3].Equals("Roaming", StringComparison.OrdinalIgnoreCase))
            return "%APPDATA%" + (rest.Length > 4 ? "\\" + Join(4) : "");
        if (rest.Length >= 4 && rest[0].Equals("Users", StringComparison.OrdinalIgnoreCase)
            && rest[2].Equals("AppData", StringComparison.OrdinalIgnoreCase)
            && rest[3].Equals("Local", StringComparison.OrdinalIgnoreCase))
            return "%LOCALAPPDATA%" + (rest.Length > 4 ? "\\" + Join(4) : "");
        if (rest.Length >= 2 && rest[0].Equals("Users", StringComparison.OrdinalIgnoreCase))
            return "%USERPROFILE%" + (rest.Length > 2 ? "\\" + Join(2) : "");
        if (rest.Length >= 1 && rest[0].Equals("Windows", StringComparison.OrdinalIgnoreCase))
            return "%WINDIR%" + (rest.Length > 1 ? "\\" + Join(1) : "");
        return absolutePath;
    }

    /// <summary>
    /// Maps a file found inside a Sandboxie box root back to the host path
    /// the sandboxed program believed it was writing: the box mirrors
    /// absolute paths under <c>drive\&lt;letter&gt;\…</c> and the user
    /// profile under <c>user\&lt;name&gt;\…</c>. Box bookkeeping files
    /// (the registry hive and its journals) return null — they are the
    /// registry harvest's business, not files.
    /// </summary>
    public static string? BoxPathToHostPath(string boxRelativePath, PalPathContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var path = boxRelativePath.Replace('/', '\\').TrimStart('\\');
        var segments = path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return null;
        if (segments[0].Equals("drive", StringComparison.OrdinalIgnoreCase) && segments.Length >= 3
            && segments[1].Length == 1 && char.IsAsciiLetter(segments[1][0]))
            return char.ToUpperInvariant(segments[1][0]) + ":\\" + string.Join("\\", segments.Skip(2));
        if (segments[0].Equals("user", StringComparison.OrdinalIgnoreCase) && segments.Length >= 3)
            return context.UserProfile.Replace('/', '\\').TrimEnd('\\') + "\\" + string.Join("\\", segments.Skip(2));
        return null;
    }
}
