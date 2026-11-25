using System.IO;
using AppWrap.Core;
using AppWrap.Core.PortableProfiles;

/// <summary>
/// Round-2 wizard checks: the editable path fields (typed/pasted paths as
/// well as browse dialogs) and the "Package another app" reset. Covers the
/// path-text normalization, the suggested-path reconciliation rule — an
/// echoed suggestion follows a filename-style change, a diverged custom
/// path is left alone — and the portable-profile workspace reset that the
/// wizard's full reset is built on. Pure logic; WPF interaction itself is
/// not exercised here.
/// </summary>
internal static class WizardChecks
{
    public static void Run(Action<string, bool> check, Action<string, Action> reject)
    {
        // Typed/pasted path text: dialogs never add whitespace or quotes,
        // pasted text (Explorer "Copy as path") often does.
        check("path text trims whitespace", WizardPaths.NormalizePathText("  C:\\Apps\\Foo  ") == "C:\\Apps\\Foo");
        check("path text strips surrounding quotes", WizardPaths.NormalizePathText("\"C:\\My Apps\\Foo\"") == "C:\\My Apps\\Foo");
        check("path text trims inside stripped quotes", WizardPaths.NormalizePathText(" \" C:\\Apps\\Foo \" ") == "C:\\Apps\\Foo");
        check("path text blank stays blank", WizardPaths.NormalizePathText("   ") == "" && WizardPaths.NormalizePathText("") == "" && WizardPaths.NormalizePathText(null) == "");
        check("path text keeps interior quotes", WizardPaths.NormalizePathText("C:\\Apps\\Say \"Hi\"") == "C:\\Apps\\Say \"Hi\"");
        check("path text keeps single leading quote", WizardPaths.NormalizePathText("\"C:\\Apps\\Foo") == "\"C:\\Apps\\Foo");

        // Path equality: case-insensitive, separator-tolerant, empty is
        // never a location.
        var folder = Path.Combine(Path.GetTempPath(), "AppWrapWizardChecks");
        check("same path ignores case", WizardPaths.SamePath(folder.ToUpperInvariant(), folder.ToLowerInvariant()));
        check("same path ignores trailing separator", WizardPaths.SamePath(folder + Path.DirectorySeparatorChar, folder));
        check("same path ignores quotes and whitespace", WizardPaths.SamePath(" \"" + folder + "\" ", folder));
        check("different paths are not the same", !WizardPaths.SamePath(folder, folder + "Other"));
        check("empty path is never the same", !WizardPaths.SamePath("", "") && !WizardPaths.SamePath(null, null) && !WizardPaths.SamePath("", folder));

        // Suggested-path reconciliation: the destination box follows the
        // suggestion only while it still echoes the previous suggestion.
        var previous = Path.Combine(folder, "App-1.0-Setup.exe");
        var next = Path.Combine(folder, "App_Setup.exe");
        var custom = Path.Combine(folder, "My Custom Name.exe");
        check("echoed suggestion follows the new suggestion", WizardPaths.ReconcileSuggestion(previous, previous, next) == next);
        check("echoed suggestion follows despite case and quotes", WizardPaths.ReconcileSuggestion("\"" + previous.ToUpperInvariant() + "\"", previous, next) == next);
        check("diverged custom path is left alone", WizardPaths.ReconcileSuggestion(custom, previous, next) == custom);
        check("no override stays no override", WizardPaths.ReconcileSuggestion("", previous, next) == "");
        check("unrelated previous suggestion leaves current alone", WizardPaths.ReconcileSuggestion(custom, next, previous) == custom);

        // The profile workspace reset behind "Package another app": edits
        // and identity from one package must not leak into the next.
        var state = new ProfileWorkspaceState();
        state.WizardAppId = "SomePortable";
        state.NewProfile();
        state.AddEntry(PortableDocumentKind.Launcher, "Launch", "ProgramExecutable", "Payload\\App.exe");
        check("profile reset precondition: edits pending", state.HasWorkingEdits && state.EffectiveProfile != null && state.Mode == PortableExecutionKind.Pal);
        state.Reset();
        check("profile reset restores already-portable mode", state.Mode == PortableExecutionKind.PackagingOnly);
        check("profile reset clears the effective profile", state.EffectiveProfile == null);
        check("profile reset clears working edits", !state.HasWorkingEdits);
        check("profile reset clears the wizard identity", state.WizardAppId == null);
        check("profile reset leaves no diagnostics", state.Diagnostics.Count == 0 && !state.HasErrors);
    }
}
