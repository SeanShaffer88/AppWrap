using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace AppWrap.Core.Projects;

/// <summary>One completed build recorded in a project's history.</summary>
public sealed record BuildModeRecord(string Mode, DateTimeOffset CompletedUtc);

/// <summary>
/// The persisted AppWrap project model (appwrap.project.json, schema version 2).
/// This is the durable, human-readable record of a PortableApps project: which PAF
/// specification it targets, a deterministic snapshot hash of its source tree, the
/// history of build modes completed against it, and the user's configuration choices.
/// Unknown JSON properties are preserved verbatim through a load/save round-trip via
/// <see cref="ExtensionData"/> so newer or older tooling never silently drops data.
/// </summary>
public sealed class PafProject
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string Name { get; set; } = "";
    public string PafSpecVersion { get; set; } = PafLayout.DefaultPafSpecVersion;
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>SHA-256 of the source tree snapshot, or null when no snapshot has been taken (or the tree exceeded the snapshot bounds).</summary>
    public string? SourceSnapshotHash { get; set; }

    /// <summary>True when the last snapshot stopped at the source bounds; <see cref="SourceSnapshotHash"/> is null in that case.</summary>
    public bool SourceSnapshotTruncated { get; set; }

    public List<BuildModeRecord> BuildModeHistory { get; set; } = [];

    /// <summary>Free-form user configuration choices recorded by the wizard (for example the preferred filename style).</summary>
    public Dictionary<string, string> UserChoices { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Properties AppWrap does not model, carried verbatim so a round-trip loses nothing.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    internal static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    /// <summary>Serializes the project at the current schema version.</summary>
    public string ToJson()
    {
        SchemaVersion = CurrentSchemaVersion;
        return JsonSerializer.Serialize(this, JsonOptions);
    }

    /// <summary>
    /// Parses a project document of any supported schema version and migrates it
    /// forward to the current version. Throws <see cref="UserInputException"/> for
    /// malformed JSON, a missing/invalid schema version, or a version newer than
    /// this build understands (never silently downgrades a newer file).
    /// </summary>
    public static PafProject FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        JsonObject root;
        try { root = JsonNode.Parse(json) as JsonObject ?? throw new JsonException("Root is not an object."); }
        catch (JsonException ex) { throw new UserInputException("The project file (appwrap.project.json) is not valid JSON: " + ex.Message); }
        var migrated = ProjectMigrator.MigrateToCurrent(root);
        try
        {
            return migrated.Deserialize<PafProject>(JsonOptions)
                ?? throw new UserInputException("The project file (appwrap.project.json) could not be read.");
        }
        catch (JsonException ex) { throw new UserInputException("The project file (appwrap.project.json) has an unexpected shape: " + ex.Message); }
    }
}

/// <summary>
/// Forward-only schema migrator chain for appwrap.project.json. Each step upgrades a
/// document exactly one version and fills the new fields with lossless defaults;
/// fields a step does not know about are left untouched, so migration never drops data.
/// </summary>
internal static class ProjectMigrator
{
    public static JsonObject MigrateToCurrent(JsonObject root)
    {
        if (root["schemaVersion"] is not JsonValue versionNode || !versionNode.TryGetValue<int>(out var version))
            throw new UserInputException("The project file (appwrap.project.json) has no readable schema version.");
        if (version > PafProject.CurrentSchemaVersion)
            throw new UserInputException($"The project file (appwrap.project.json) uses schema version {version}, which is newer than this version of AppWrap understands ({PafProject.CurrentSchemaVersion}). Update AppWrap to open this project.");
        if (version < 1)
            throw new UserInputException("The project file (appwrap.project.json) has an invalid schema version.");
        while (version < PafProject.CurrentSchemaVersion)
        {
            switch (version)
            {
                case 1: Migrate1To2(root); version = 2; break;
                default: throw new UserInputException($"No migration exists from project schema version {version}.");
            }
            root["schemaVersion"] = version;
        }
        return root;
    }

    /// <summary>
    /// v1 shape: schemaVersion, name, pafSpecVersion, createdUtc.
    /// v2 adds sourceSnapshotHash, sourceSnapshotTruncated, buildModeHistory and
    /// userChoices, all defaulted empty/null — nothing a v1 file carried is changed.
    /// </summary>
    private static void Migrate1To2(JsonObject root)
    {
        // sourceSnapshotHash stays absent (deserializes as null) until a snapshot is taken.
        if (!root.ContainsKey("sourceSnapshotTruncated")) root["sourceSnapshotTruncated"] = false;
        if (!root.ContainsKey("buildModeHistory")) root["buildModeHistory"] = new JsonArray();
        if (!root.ContainsKey("userChoices")) root["userChoices"] = new JsonObject();
    }
}
