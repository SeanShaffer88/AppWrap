using System.Text.Json;

namespace AppWrap.Core.Audit;

// Row shapes for the MSI tables the audit reads. These mirror the documented
// MSI table schemas (File, Registry, Shortcut, Directory, ServiceInstall);
// only the columns the audit uses are carried.

public sealed record MsiFileRow(string FileKey, string Component, string FileName, string? Directory);
public sealed record MsiRegistryRow(string RegistryKey, int Root, string Key, string? Name, string? Value, string Component);
public sealed record MsiShortcutRow(string Shortcut, string Directory, string Name, string Component, string? Target);
public sealed record MsiDirectoryRow(string Directory, string? Parent, string DefaultDir);
public sealed record MsiServiceRow(string ServiceKey, string Name, string? DisplayName, string Component);

/// <summary>The subset of an MSI database the static audit consumes.</summary>
public sealed record MsiTables(
    IReadOnlyList<MsiFileRow> Files,
    IReadOnlyList<MsiRegistryRow> Registry,
    IReadOnlyList<MsiShortcutRow> Shortcuts,
    IReadOnlyList<MsiDirectoryRow> Directories,
    IReadOnlyList<MsiServiceRow> Services)
{
    public static MsiTables Empty { get; } = new([], [], [], [], []);
}

/// <summary>
/// The MSI reader seam. The real reader (AppWrap.Build) opens the database
/// through msi.dll on Windows; checks and non-Windows hosts substitute
/// <see cref="FixtureMsiReader"/>. Reading an MSI never installs anything.
/// </summary>
public interface IMsiReader
{
    MsiTables Read(string msiPath);
}

/// <summary>
/// An <see cref="IMsiReader"/> over recorded table data — the fixture form of
/// an MSI. <see cref="FromJson"/> loads the JSON fixture shape the checks use
/// (and that a Windows host can produce by dumping a real MSI's tables), so
/// MSI classification is fully check-covered without a Windows MSI service.
/// </summary>
public sealed class FixtureMsiReader(MsiTables tables) : IMsiReader
{
    public MsiTables Read(string msiPath) => tables;

    private sealed class FixtureDto
    {
        public List<MsiFileRow> Files { get; set; } = [];
        public List<MsiRegistryRow> Registry { get; set; } = [];
        public List<MsiShortcutRow> Shortcuts { get; set; } = [];
        public List<MsiDirectoryRow> Directories { get; set; } = [];
        public List<MsiServiceRow> Services { get; set; } = [];
    }

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static FixtureMsiReader FromJson(string json)
    {
        var dto = JsonSerializer.Deserialize<FixtureDto>(json, Options)
            ?? throw new UserInputException("The MSI fixture could not be read.");
        return new FixtureMsiReader(new MsiTables(dto.Files, dto.Registry, dto.Shortcuts, dto.Directories, dto.Services));
    }

    public static string ToJson(MsiTables tables) => JsonSerializer.Serialize(new FixtureDto
    {
        Files = tables.Files.ToList(), Registry = tables.Registry.ToList(), Shortcuts = tables.Shortcuts.ToList(),
        Directories = tables.Directories.ToList(), Services = tables.Services.ToList(),
    }, new JsonSerializerOptions { WriteIndented = true });
}
