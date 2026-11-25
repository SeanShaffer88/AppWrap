namespace AppWrap.Core.TestHarness;

/// <summary>
/// The harness's portability rules, as pure functions over a detected change.
/// The watched roots are the copied package, the redirected profile and the
/// redirected temp folder, so every observable change falls into exactly one
/// rule and nothing outside them can be observed at all:
///
/// - Package root, under <c>Data/</c>: the expected portable write — noted, never a violation.
/// - Package root, anywhere else: the app wrote beside its own files (a launcher
///   updating its INI, a log next to the executable). Noted, not a violation:
///   the package root is the portable root, so the write stays portable.
/// - Profile root (redirected USERPROFILE / APPDATA / LOCALAPPDATA): leakage.
///   A portable app must not need the profile at all, so any file change there
///   is a portability violation.
/// - Temp root (redirected TEMP / TMP): scratch space is what temp is for.
///   Noted, not a violation by itself.
///
/// The verdict is Fail when any violation was detected, when the run timed
/// out, or when the app did not exit cleanly with code 0 (a crash or a killed
/// process is a failed test, whatever it wrote).
/// </summary>
public static class WriteClassifier
{
    public static (WriteCategory Category, bool Violation) Classify(WatchedRootKind root, string relativeDisplayPath) => root switch
    {
        WatchedRootKind.Package => relativeDisplayPath == "Data" || relativeDisplayPath.StartsWith("Data/", StringComparison.Ordinal)
            ? (WriteCategory.PortableData, false)
            : (WriteCategory.PackageRoot, false),
        WatchedRootKind.Profile => (WriteCategory.ProfileLeakage, true),
        _ => (WriteCategory.Temporary, false),
    };

    /// <summary>Classifies one diff entry from one watched root into a report row.</summary>
    public static DetectedWrite Describe(WatchedRootKind root, string relativeDisplayPath, WriteChange change)
    {
        var (category, violation) = Classify(root, relativeDisplayPath);
        return new DetectedWrite(root, relativeDisplayPath, change, category, violation);
    }

    public static TestVerdict Verdict(IEnumerable<DetectedWrite> writes, bool timedOut, int? exitCode) =>
        timedOut || exitCode is not 0 || writes.Any(w => w.Violation) ? TestVerdict.Fail : TestVerdict.Pass;
}
