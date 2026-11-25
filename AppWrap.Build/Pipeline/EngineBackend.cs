using AppWrap.Core.Pipeline;

namespace AppWrap.Build.Pipeline;

/// <summary>
/// Locates the prepared extraction engines (stage 1) under
/// <c>third_party\engines</c> beside AppWrap — the same backend layout
/// discipline as <c>third_party\pal</c>: prepared by
/// tools/Prepare-EngineBackend.ps1 from the pinned releases in
/// <see cref="ExtractionEngineLock"/>, never downloaded at runtime.
/// An engine that was not prepared is simply absent from the result;
/// the audit degrades to its stated limits, never to an error.
/// </summary>
public static class EngineBackend
{
    public static string Root(string baseDirectory) => Path.Combine(baseDirectory, "third_party", "engines");

    public static string FolderName(ExtractionEngineKind kind) => kind switch
    {
        ExtractionEngineKind.SevenZip => "7zip",
        ExtractionEngineKind.InnoExtract => "innoextract",
        ExtractionEngineKind.LessMsi => "lessmsi",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static EngineAvailability Locate(string baseDirectory)
    {
        var found = new Dictionary<ExtractionEngineKind, string>();
        foreach (var pin in ExtractionEngineLock.Pinned)
        {
            var candidate = Path.Combine(Root(baseDirectory), FolderName(pin.Kind), pin.ExecutablePath);
            if (File.Exists(candidate)) found[pin.Kind] = candidate;
        }
        return new EngineAvailability(found);
    }
}

/// <summary>
/// Runs a prepared extraction engine over a source. 7-Zip's console
/// extractor handles NSIS/archive payloads, innoextract unpacks Inno
/// Setup, lessMSI extracts MSI payloads. Exit code 0 (7-Zip: also 1,
/// its "warning" success) counts as extracted; anything else is a
/// failed extraction and the audit degrades. Windows-only in practice
/// (the pinned engines are Windows binaries); the seam is what checks
/// exercise.
/// </summary>
public sealed class ProcessExtractionRunner : IExtractionRunner
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(10);

    public async Task<bool> ExtractAsync(ExtractionEngineKind engine, string executablePath, string sourcePath, string destinationDirectory, CancellationToken token)
    {
        Directory.CreateDirectory(destinationDirectory);
        IReadOnlyList<string> arguments = engine switch
        {
            // -y: assume yes; -o<dir>: output folder (no space after -o).
            ExtractionEngineKind.SevenZip => ["x", "-y", "-o" + destinationDirectory, sourcePath],
            ExtractionEngineKind.InnoExtract => ["--extract", "--output-dir", destinationDirectory, sourcePath],
            // lessmsi x <msi> <path\> — the target path takes a trailing separator.
            ExtractionEngineKind.LessMsi => ["x", sourcePath, destinationDirectory.TrimEnd('\\', '/') + Path.DirectorySeparatorChar],
            _ => throw new ArgumentOutOfRangeException(nameof(engine)),
        };
        try
        {
            var result = await ProcessRunner.RunAsync(executablePath, arguments, destinationDirectory, token, DefaultTimeout).ConfigureAwait(false);
            return engine == ExtractionEngineKind.SevenZip ? result.ExitCode is 0 or 1 : result.ExitCode == 0;
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
