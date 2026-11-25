namespace AppWrap.Cli;

/// <summary>Which face of the dual-mode executable a command line selects.</summary>
public enum CliMode
{
    /// <summary>Open the wizard window (the default).</summary>
    Gui,

    /// <summary>Run the command line in-process and exit with its code.</summary>
    Cli,
}

/// <summary>
/// The outcome of routing a raw command line: the selected mode, and — for
/// <see cref="CliMode.Cli"/> — the arguments to hand to <see cref="CliHost"/>
/// (the leading <c>--cli</c> shim marker stripped).
/// </summary>
public sealed record ModeDecision(CliMode Mode, string[] CliArgs)
{
    public static ModeDecision Gui { get; } = new(CliMode.Gui, []);
}

/// <summary>
/// Decides whether AppWrap.exe starts as the wizard or as the CLI. The rule
/// is deliberately conservative: only an unmistakable command line selects
/// the CLI, so a stray argument (a file path from a shell association, a
/// flag the wizard might one day grow) still opens the GUI exactly as
/// before.
///
/// - No arguments → GUI.
/// - <c>--cli</c> (the native shim's marker) is stripped; if nothing
///   remains, or the next argument is <c>gui</c>, the GUI opens; anything
///   else runs as the CLI with the remaining arguments.
/// - A first argument naming a CLI command (<c>build</c>, <c>project</c>,
///   <c>config</c>, <c>profile</c>, <c>test</c>) or a global CLI flag
///   (<c>--help</c>, <c>--version</c>) runs as the CLI with all arguments.
///   Command flags select the CLI only when they follow a command.
/// - <c>gui</c>, and anything unrecognized, opens the GUI.
/// </summary>
public static class CliModeDetector
{
    /// <summary>The shim marker the native AppWrap.Cli.exe prepends.</summary>
    public const string ShimMarker = "--cli";

    /// <summary>First arguments that select the CLI when given directly to AppWrap.exe.</summary>
    public static IReadOnlyList<string> Commands { get; } = ["build", "project", "config", "profile", "test", "audit"];

    /// <summary>Global flags that select the CLI as a first argument.</summary>
    public static IReadOnlyList<string> GlobalFlags { get; } = ["--help", "--version"];

    public static ModeDecision Detect(IReadOnlyList<string> args)
    {
        if (args.Count == 0) return ModeDecision.Gui;
        var first = args[0];
        if (first == ShimMarker)
        {
            var rest = args.Skip(1).ToArray();
            if (rest.Length == 0 || rest[0] == "gui") return ModeDecision.Gui;
            return new ModeDecision(CliMode.Cli, rest);
        }
        if (first == "gui") return ModeDecision.Gui;
        if (Commands.Contains(first, StringComparer.Ordinal) || GlobalFlags.Contains(first, StringComparer.Ordinal))
            return new ModeDecision(CliMode.Cli, args.ToArray());
        return ModeDecision.Gui;
    }
}
