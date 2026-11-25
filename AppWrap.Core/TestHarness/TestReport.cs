using System.Text.Json;
using System.Text.Json.Serialization;

namespace AppWrap.Core.TestHarness;

/// <summary>Which packaged output a test run exercised.</summary>
public enum TestRunMode { Portable, PafPackage }

/// <summary>The outcome of a test run: Pass only for a clean, complete, portable run.</summary>
public enum TestVerdict { Pass, Fail }

/// <summary>The watched root a detected change belongs to.</summary>
public enum WatchedRootKind { Package, Profile, Temp }

/// <summary>How a watched file changed across the run.</summary>
public enum WriteChange { Created, Modified, Deleted }

/// <summary>
/// Where a detected change landed. <see cref="ProfileLeakage"/> is the only
/// category that is a portability violation: the profile roots are redirected
/// into the sandbox precisely so that any write there is observable leakage —
/// a truly portable app writes only under its own package root.
/// </summary>
public enum WriteCategory { PortableData, PackageRoot, ProfileLeakage, Temporary }

/// <summary>
/// One detected file change. <see cref="Path"/> is relative to the watched root
/// (<see cref="Root"/>) in display form (forward slashes), so a report stays
/// meaningful after the sandbox it describes has been deleted.
/// </summary>
public sealed record DetectedWrite(WatchedRootKind Root, string Path, WriteChange Change, WriteCategory Category, bool Violation);

/// <summary>
/// The structured result of one test run, serialized as <c>test-report.json</c>.
/// Schema version 1: camelCase fields — schemaVersion, appName, mode,
/// launchCommand, startedUtc, durationSeconds, exitCode (null when the app was
/// killed or never exited), timedOut, detectedWrites, violations (the subset of
/// detectedWrites with violation = true), verdict. Registry observation is
/// additive within schema 1: registryObserved says whether a registry
/// provider took part, registryChanges lists the detected registry changes
/// and registryViolations the subset that are portability violations (they
/// also fail the verdict — see WriteClassifier).
/// </summary>
public sealed record TestReport(
    int SchemaVersion,
    string AppName,
    TestRunMode Mode,
    string LaunchCommand,
    DateTimeOffset StartedUtc,
    double DurationSeconds,
    int? ExitCode,
    bool TimedOut,
    IReadOnlyList<DetectedWrite> DetectedWrites,
    IReadOnlyList<DetectedWrite> Violations,
    TestVerdict Verdict,
    bool RegistryObserved = false,
    IReadOnlyList<DetectedRegistryChange>? RegistryChanges = null,
    IReadOnlyList<DetectedRegistryChange>? RegistryViolations = null)
{
    /// <summary>Detected registry changes, or an empty list when the registry was not observed.</summary>
    public IReadOnlyList<DetectedRegistryChange> RegistryChangeList => RegistryChanges ?? [];

    /// <summary>Registry changes that are portability violations, or an empty list when not observed.</summary>
    public IReadOnlyList<DetectedRegistryChange> RegistryViolationList => RegistryViolations ?? [];
    public const int CurrentSchemaVersion = 1;

    internal static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static TestReport FromJson(string json) =>
        JsonSerializer.Deserialize<TestReport>(json, JsonOptions)
        ?? throw new UserInputException("The test report could not be read.");
}
