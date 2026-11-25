namespace AppWrap.Build;

/// <summary>
/// A build step failed for a reason the user cannot fix by changing their input
/// (a missing or unpinned packaging component, a failed backend, and similar).
/// <see cref="Details"/> carries the diagnostic text for explicit disclosure.
/// </summary>
public sealed class BuildFailureException(string message, string details) : Exception(message)
{
    public string Details { get; } = details;
}
