using AppWrap.Cli;

/// <summary>
/// Dual-mode entry checks: the detector that decides whether AppWrap.exe
/// opens the wizard or runs the CLI. The rule is conservative — only an
/// unmistakable command line selects the CLI — so every case here pins one
/// side of that line, including the native shim's <c>--cli</c> marker
/// (stripped before the host sees the arguments) and the <c>gui</c>
/// command, which always routes to the window and never reaches the router.
/// </summary>
internal static class ModeDetectorChecks
{
    public static void Run(Action<string, bool> check)
    {
        void ExpectGui(string name, params string[] args) =>
            check(name, CliModeDetector.Detect(args).Mode == CliMode.Gui);

        void ExpectCli(string name, string[] args, params string[] expected) =>
            check(name, CliModeDetector.Detect(args) is { Mode: CliMode.Cli } decision
                && decision.CliArgs.SequenceEqual(expected));

        ExpectGui("mode no args opens gui");
        ExpectGui("mode shim marker alone opens gui", "--cli");
        ExpectGui("mode shim marker then gui opens gui", "--cli", "gui");
        ExpectGui("mode gui command opens gui", "gui");
        ExpectGui("mode unknown word opens gui", "frobnicate");
        ExpectGui("mode file path opens gui", "C:\\Apps\\Foo");
        ExpectGui("mode stray global flag opens gui", "--json");
        ExpectGui("mode command flag alone opens gui", "--source");

        ExpectCli("mode shim strips marker", ["--cli", "build", "--source", "X"], "build", "--source", "X");
        ExpectCli("mode shim forwards help", ["--cli", "--help"], "--help");
        ExpectCli("mode direct build command", ["build", "--source", "X"], "build", "--source", "X");
        ExpectCli("mode direct project command", ["project", "validate", "X"], "project", "validate", "X");
        ExpectCli("mode direct config command", ["config", "list", "X"], "config", "list", "X");
        ExpectCli("mode direct profile command", ["profile", "validate", "X"], "profile", "validate", "X");
        ExpectCli("mode direct test command", ["test", "X"], "test", "X");
        ExpectCli("mode help flag is cli", ["--help"], "--help");
        ExpectCli("mode version flag is cli", ["--version"], "--version");
    }
}
