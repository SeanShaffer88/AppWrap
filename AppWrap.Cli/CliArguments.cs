using AppWrap.Core;

namespace AppWrap.Cli;

/// <summary>
/// A command-line usage problem (unknown command or flag, a missing value, a
/// conflicting flag pair). The router maps it to exit code 2 and shows the
/// help for the relevant command alongside the message.
/// </summary>
public sealed class CliUsageException(string message, string? command = null) : Exception(message)
{
    /// <summary>The command key whose help accompanies the error, when known.</summary>
    public string? Command { get; } = command;
}

/// <summary>One option of a command: its long name, whether it takes a value, and its help text.</summary>
public sealed record CliOptionSpec(string Name, bool TakesValue, string Description);

/// <summary>The declared shape of one command: usage line, summary, options and positional bounds.</summary>
public sealed record CliCommandSpec(
    string Key, string Usage, string Summary,
    IReadOnlyList<CliOptionSpec> Options, int MinPositionals, int MaxPositionals, string PositionalsHint);

/// <summary>
/// A parsed command line. Produced only by <see cref="CliArguments.Parse"/>;
/// carries no console references, so the parse layer is check-covered by the
/// Linux harness exactly as shipped.
/// </summary>
public sealed class CliInvocation
{
    internal CliInvocation(string commandKey, IReadOnlyDictionary<string, string?> options,
        IReadOnlyList<string> positionals, bool json, bool dryRun, bool verbose, bool help, bool version)
    {
        CommandKey = commandKey;
        Options = options;
        Positionals = positionals;
        Json = json;
        DryRun = dryRun;
        Verbose = verbose;
        Help = help;
        Version = version;
    }

    /// <summary>The resolved command key ("build", "project validate", …), or "" when no command was given.</summary>
    public string CommandKey { get; }

    /// <summary>Command options by long name (without the leading --); flags map to null.</summary>
    public IReadOnlyDictionary<string, string?> Options { get; }

    public IReadOnlyList<string> Positionals { get; }
    public bool Json { get; }
    public bool DryRun { get; }
    public bool Verbose { get; }
    public bool Help { get; }
    public bool Version { get; }

    public bool Has(string name) => Options.ContainsKey(name);

    /// <summary>The value of a value option, or null when it was not given.</summary>
    public string? Value(string name) => Options.TryGetValue(name, out var value) ? value : null;

    /// <summary>
    /// Resolves an on/off flag pair (--name / --no-name) to a tri-state:
    /// true, false, or null when neither was given. Giving both is a usage error.
    /// </summary>
    public bool? TriState(string name)
    {
        var on = Has(name);
        var off = Has("no-" + name);
        if (on && off) throw new CliUsageException($"Use either --{name} or --no-{name}, not both.", CommandKey);
        return on ? true : off ? false : null;
    }
}

/// <summary>
/// The command table and the argument parser. Global switches (--json,
/// --dry-run, --verbose, --help, --version) are accepted anywhere on the
/// line; every other option must belong to the command being run, so a typo
/// is a usage error naming the valid options rather than a silent ignore.
/// </summary>
public static class CliArguments
{
    private static readonly string[] Roots = ["build", "test", "gui", "project", "config", "profile", "audit"];
    private static readonly string[] GlobalSwitches = ["json", "dry-run", "verbose", "help", "version"];

    private static CliOptionSpec Value(string name, string description) => new(name, true, description);
    private static CliOptionSpec Flag(string name, string description) => new(name, false, description);

    public static IReadOnlyDictionary<string, CliCommandSpec> Commands { get; } = new Dictionary<string, CliCommandSpec>
    {
        ["build"] = new("build",
            "appwrap build --source <folder> [options]",
            "Package an application folder (the wizard's Build action).",
            [
                Value("source", "Application folder to package (required)."),
                Value("mode", "installer | portable | both | paf (default: installer, the wizard default)."),
                Value("name", "App name (default: the project name with --project, else the folder name)."),
                Value("version", "App version (default: 1.0)."),
                Value("launch", "Launch .exe relative to the source folder (default: the folder's sole .exe)."),
                Value("output", "Destination: a file for installer/portable, a folder for both/paf (default: the Desktop, with the suggested filename)."),
                Value("filename-style", "Name-Version-Setup (default) | Name_Setup | Name-Setup-Version."),
                Value("icon", "Icon image for portable and PAF packages (default: the --project project's iconPath)."),
                Value("sidebar", "Sidebar image for portable packages (default: the --project project's sidebarImagePath)."),
                Flag("start-menu", "Offer a Start Menu shortcut (installer; default on)."),
                Flag("no-start-menu", "Do not offer a Start Menu shortcut."),
                Flag("desktop", "Offer a Desktop shortcut (installer; default off)."),
                Flag("no-desktop", "Do not offer a Desktop shortcut."),
                Flag("run-after", "Offer to launch the app after install (default on)."),
                Flag("no-run-after", "Do not offer to launch the app after install."),
                Flag("startup", "Offer Start with Windows after install (default off)."),
                Value("website", "Developer website offered after install (http/https URL)."),
                Value("profile", "Portable profile file (.json as saved by the wizard, or a launcher .ini) for portable modes."),
                Value("audit-draft", "A draft profile saved by 'audit --draft' (reviewed and re-saved in the profile workspace): used as the build's portable profile for portable modes. Equivalent to --profile; the two cannot be combined."),
                Value("project", "appwrap.project.json (or its folder): supplies default name/filename style and records the build."),
                Flag("overwrite", "Replace existing output files/folders (the wizard asks; the CLI requires this flag)."),
            ], 0, 0, ""),
        ["project validate"] = new("project validate",
            "appwrap project validate <folder-or-project-file>",
            "Run the PAF validator over a project folder and print field-level diagnostics.",
            [], 1, 1, "<folder-or-project-file>"),
        ["project info"] = new("project info",
            "appwrap project info <folder-or-project-file>",
            "Summarize the project stored at (or inferred for) a folder.",
            [], 1, 1, "<folder-or-project-file>"),
        ["project create"] = new("project create",
            "appwrap project create <folder> [--template blank|wrapped] [--name <name>] [--app-id <id>] [--advanced]",
            "Create a project from a template (the wizard's New project from template action).",
            [
                Value("template", "blank (default) scaffolds a new PAF project; wrapped records the wrap-a-folder template for an existing app folder."),
                Value("name", "App name (default: the folder name)."),
                Value("app-id", "AppID; must match the ID AppWrap derives from the name."),
                Flag("advanced", "Start the blank project with Advanced mode on (writes the inert custom-code starters)."),
            ], 1, 1, "<folder>"),
        ["config list"] = new("config list",
            "appwrap config list <folder> [document]",
            "List a project's configuration documents, or one document's fields with their values.",
            [], 1, 2, "<folder> [document]"),
        ["config get"] = new("config get",
            "appwrap config get <folder> <document> <section> <key>",
            "Read one configuration field of a project.",
            [], 4, 4, "<folder> <document> <section> <key>"),
        ["config set"] = new("config set",
            "appwrap config set <folder> <document> <section> <key> <value>",
            "Set one configuration field of a project and save it through the configuration workspace.",
            [], 5, 5, "<folder> <document> <section> <key> <value>"),
        ["profile validate"] = new("profile validate",
            "appwrap profile validate <file>",
            "Validate a portable profile file (.json as saved by the wizard, or a launcher .ini).",
            [], 1, 1, "<file>"),
        ["test"] = new("test",
            "appwrap test <package-folder> [--mode portable|paf] [--launch <file>] [--timeout <seconds>] [--report <path>] [--registry] [--sandboxie]",
            "Run the runtime test harness once against a finished package folder (the wizard's Test package action).",
            [
                Value("mode", "portable (default) | paf."),
                Value("launch", "Launch file at the package root (default: from the package's appinfo, else its sole root .exe)."),
                Value("timeout", "Seconds before the tested app is closed (default: 60)."),
                Value("report", "Report path (default: Data/TestReports/test-report-<timestamp>.json beside the CLI)."),
                Flag("registry", "Also observe the registry (Windows only): snapshot the HKCU/HKLM Software roots before and after the run and report the diff; registry violations fail the verdict."),
                Flag("sandboxie", "Leak verification (Windows only): run the packaged app inside a clean Sandboxie-Plus box instead of on the host; the verdict then means zero writes escaped the box. Requires Sandboxie-Plus (AppWrap Setup bootstraps the pinned release)."),
            ], 1, 1, "<package-folder>"),
        ["audit"] = new("audit",
            "appwrap audit <source> [--runtime] [--sandboxie] [--engines] [--draft <profile.json>] [--report <audit-report.json>]",
            "Audit a setup .exe/.msi or portable .zip for conversion to a PortableApps.com package: where it stores data and which registry entries it creates.",
            [
                Flag("runtime", "Also run the runtime pass (Windows only): silently install a setup into scratch, launch the app once, and observe file/registry changes. Explicit action only — never part of a build."),
                Flag("sandboxie", "With --runtime: require Sandboxie-Plus and profile inside a dedicated box. Never falls back to host execution. Required for zip runtime audits."),
                Flag("engines", "Static pass only: use the prepared extraction engines beside AppWrap (7-Zip for NSIS/wrappers, innoextract for Inno Setup, lessMSI to complement an .msi) to unpack the payload before inventory. Without a prepared engine the audit states its limits, exactly as without this flag."),
                Value("draft", "Also synthesize a DRAFT portable profile from the findings and save it as a wizard profile .json (untrusted until reviewed and saved in the profile workspace)."),
                Value("report", "Write the audit report JSON to this path (default: print findings only)."),
                Value("launch", "For --runtime: the installed app's launch file, or a zip executable relative to its payload folder (default: one inventoried main .exe)."),
                Value("silent-args", "For --runtime with an .exe: the installer's silent arguments as one string, when AppWrap does not know its family's switches."),
                Value("msi-fixture", "Read MSI tables from this recorded fixture JSON instead of msi.dll (for non-Windows hosts and checks)."),
            ], 1, 1, "<source>"),
        ["gui"] = new("gui",
            "appwrap gui",
            "Launch the AppWrap wizard (AppWrap.exe beside the CLI).",
            [], 0, 0, ""),
    };

    /// <summary>Per-command notes appended to generated help (semantics the option list cannot carry).</summary>
    public static IReadOnlyDictionary<string, string[]> Notes { get; } = new Dictionary<string, string[]>
    {
        ["build"] = [
            "Output semantics mirror the wizard: installer/portable take a full file path; both takes a",
            "folder and writes both suggested filenames into it; paf takes a folder and creates the",
            "package folder (named after the app's AppID) inside it. With no --output, the Desktop",
            "folder is used with the suggested filename. --dry-run validates and prints the plan only.",
            "A profile with validation errors stops the build before anything is written (exit 3).",
        ],
        ["test"] = [
            "Testing is an explicit action, exactly as in the wizard: builds never run a packaged app.",
            "The tested app runs once in a throwaway sandbox with a redirected profile; writes to the",
            "profile are portability violations. A failing verdict exits 4; the report is still written.",
            "With --sandboxie the app runs inside a real Sandboxie-Plus box and the report's",
            "observationBackend is \"sandboxie\": the verdict then certifies zero writes escaped the box.",
        ],
        ["audit"] = [
            "The static pass never executes the source: a zip is safely extracted and inventoried, an .msi's tables are read,",
            "an .exe is identified by family (its payload is a stated audit limit, not a guess).",
            "With --engines, prepared extraction engines (third_party\\engines beside AppWrap) unpack",
            "NSIS/Inno/MSI payloads first; a missing or failing engine degrades to the same stated limit.",
            "The runtime pass is the explicit second step, exactly like the test harness: it runs the",
            "vendor installer silently into a scratch folder and the installed app once, on Windows.",
            "Zip runtime auditing requires --sandboxie and runs the chosen payload executable in a box.",
            "With --sandboxie, missing Sandboxie or a failed registry-hive load stops the audit; there",
            "is no host fallback. Without it, setup runtime audits explicitly use the host route.",
            "A synthesized draft profile is never trusted automatically: review it in the profile",
            "workspace (or with 'profile validate') and save it before 'build --profile' uses it.",
        ],
        ["config list"] = ["Documents: appinfo, launcher, installer, project. The project document holds the project's image properties (section Images, keys IconPath and SidebarImagePath) from appwrap.project.json."],
        ["config get"] = ["Documents: appinfo, launcher, installer, project (section Images, keys IconPath and SidebarImagePath)."],
        ["config set"] = ["Documents: appinfo, launcher, installer, project (section Images, keys IconPath and SidebarImagePath; the value is an image path, stored project-relative when the file lives in the project). Unknown documents, sections and keys are usage errors listing the valid choices."],
    };

    public static CliInvocation Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var json = false; var dryRun = false; var verbose = false; var help = false; var version = false;
        var words = new List<string>();
        string? commandKey = null;
        CliCommandSpec? spec = null;
        var options = new Dictionary<string, string?>(StringComparer.Ordinal);
        var positionals = new List<string>();
        var afterTerminator = false;

        void ApplyGlobal(string name)
        {
            switch (name)
            {
                case "json": json = true; break;
                case "dry-run": dryRun = true; break;
                case "verbose": verbose = true; break;
                case "help": help = true; break;
                case "version": version = true; break;
            }
        }

        for (var i = 0; i < args.Length; i++)
        {
            var token = args[i];
            if (!afterTerminator && token == "--") { afterTerminator = true; continue; }
            if (!afterTerminator && token.StartsWith("--", StringComparison.Ordinal) && token.Length > 2)
            {
                var body = token[2..];
                var equals = body.IndexOf('=');
                var name = equals >= 0 ? body[..equals] : body;
                var inlineValue = equals >= 0 ? body[(equals + 1)..] : null;
                // A command's own option wins over a global switch of the same
                // name once the command is known: build's --version <value>
                // (the app version) shadows the global --version flag there.
                var option = spec?.Options.FirstOrDefault(o => o.Name == name);
                if (option is null && GlobalSwitches.Contains(name))
                {
                    if (inlineValue is not null) throw new CliUsageException($"--{name} does not take a value.", commandKey);
                    ApplyGlobal(name);
                    continue;
                }
                if (spec is null) throw new CliUsageException($"--{name} appears before the command it belongs to.", commandKey);
                if (option is null)
                    throw new CliUsageException($"Unknown option --{name} for '{spec.Key}'. Valid options: {string.Join(", ", spec.Options.Select(o => "--" + o.Name))}.", spec.Key);
                if (options.ContainsKey(name)) throw new CliUsageException($"--{name} was given more than once.", spec.Key);
                if (!option.TakesValue)
                {
                    if (inlineValue is not null) throw new CliUsageException($"--{name} does not take a value.", spec.Key);
                    options[name] = null;
                }
                else if (inlineValue is not null) options[name] = inlineValue;
                else
                {
                    if (i + 1 >= args.Length) throw new CliUsageException($"--{name} needs a value.", spec.Key);
                    options[name] = args[++i];
                }
                continue;
            }
            if (spec is null)
            {
                // Still resolving the command path.
                if (words.Count == 0)
                {
                    if (!Roots.Contains(token)) throw new CliUsageException($"Unknown command '{token}'. Commands: {string.Join(", ", Commands.Keys)}.");
                    words.Add(token);
                    if (Commands.TryGetValue(token, out var single)) { spec = single; commandKey = token; }
                    continue;
                }
                var joined = words[0] + " " + token;
                if (!Commands.TryGetValue(joined, out var pair))
                    throw new CliUsageException($"'{words[0]}' needs a subcommand: {string.Join(", ", Commands.Keys.Where(k => k.StartsWith(words[0] + " ", StringComparison.Ordinal)).Select(k => k[(words[0].Length + 1)..]))}.");
                spec = pair; commandKey = joined;
                continue;
            }
            positionals.Add(token);
        }

        if (spec is null && words.Count != 0)
            throw new CliUsageException($"'{words[0]}' needs a subcommand: {string.Join(", ", Commands.Keys.Where(k => k.StartsWith(words[0] + " ", StringComparison.Ordinal)).Select(k => k[(words[0].Length + 1)..]))}.");
        if (spec is not null && !help && (positionals.Count < spec.MinPositionals || positionals.Count > spec.MaxPositionals))
            throw new CliUsageException(spec.MaxPositionals == 0
                ? $"'{spec.Key}' takes no arguments; got {positionals.Count}. Usage: {spec.Usage}"
                : $"'{spec.Key}' expects {spec.PositionalsHint}; got {positionals.Count} argument(s). Usage: {spec.Usage}", spec.Key);
        return new CliInvocation(commandKey ?? "", options, positionals, json, dryRun, verbose, help, version);
    }
}

/// <summary>
/// The build command's options with their defaults resolved. Pure parse-level
/// logic: <see cref="WithProjectDefaults"/> applies the precedence the wizard
/// uses — an explicit flag beats the project file, which beats the built-in
/// default — without touching the file system.
/// </summary>
public sealed record BuildCommandOptions(
    string? Source, string Mode, string? Name, string? Version, string? Launch, string? Output,
    string? FilenameStyle, string? Icon, string? Sidebar,
    bool? StartMenu, bool? Desktop, bool? RunAfter, bool? Startup,
    string? Website, string? Profile, string? Project, bool Overwrite, string? AuditDraft = null)
{
    /// <summary>The wizard's default output mode word.</summary>
    public const string DefaultMode = "installer";

    public static BuildCommandOptions FromInvocation(CliInvocation invocation)
    {
        var mode = (invocation.Value("mode") ?? DefaultMode).ToLowerInvariant();
        if (mode is not ("installer" or "portable" or "both" or "paf"))
            throw new CliUsageException($"--mode must be installer, portable, both or paf; got '{invocation.Value("mode")}'.", "build");
        var style = invocation.Value("filename-style");
        if (style is not null && !IsStyleWord(style))
            throw new CliUsageException($"--filename-style must be one of: {string.Join(", ", PackageNaming.Labels)} (or the style names NameVersionSetup, NameOnly, NameSetupVersion).", "build");
        return new BuildCommandOptions(
            invocation.Value("source"), mode,
            invocation.Value("name"), invocation.Value("version"), invocation.Value("launch"), invocation.Value("output"),
            style, invocation.Value("icon"), invocation.Value("sidebar"),
            invocation.TriState("start-menu"), invocation.TriState("desktop"), invocation.TriState("run-after"),
            invocation.Has("startup") ? true : null,
            invocation.Value("website"), invocation.Value("profile"), invocation.Value("project"),
            invocation.Has("overwrite"), invocation.Value("audit-draft"));
    }

    /// <summary>
    /// Applies project-file defaults under any explicit flags (flags win). The
    /// project image arguments arrive already resolved to absolute paths against
    /// the project root by the router, so they compose with flag paths directly.
    /// </summary>
    public BuildCommandOptions WithProjectDefaults(string? projectName, string? projectStyle, string? projectIcon = null, string? projectSidebar = null) => this with
    {
        Name = Name ?? (string.IsNullOrWhiteSpace(projectName) ? null : projectName),
        FilenameStyle = FilenameStyle ?? (projectStyle is not null && IsStyleWord(projectStyle) ? projectStyle : null),
        Icon = Icon ?? projectIcon,
        Sidebar = Sidebar ?? projectSidebar,
    };

    /// <summary>The effective filename style: the given word, or the wizard default.</summary>
    public PackageNamingStyle Style => ParseStyle(FilenameStyle);

    public static bool IsStyleWord(string word) =>
        PackageNaming.Labels.Any(l => string.Equals(l, word, StringComparison.OrdinalIgnoreCase))
        || (Enum.TryParse<PackageNamingStyle>(word, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed));

    public static PackageNamingStyle ParseStyle(string? word)
    {
        if (word is null) return PackageNamingStyle.NameVersionSetup;
        for (var i = 0; i < PackageNaming.Labels.Count; i++)
            if (string.Equals(PackageNaming.Labels[i], word, StringComparison.OrdinalIgnoreCase)) return (PackageNamingStyle)i;
        if (Enum.TryParse<PackageNamingStyle>(word, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)) return parsed;
        throw new CliUsageException($"Unknown filename style '{word}'.", "build");
    }
}

/// <summary>Help text generated from the command table, so help can never drift from the parser.</summary>
public static class CliHelp
{
    public static IReadOnlyList<string> TopLevel()
    {
        var lines = new List<string>
        {
            "AppWrap command line — every wizard capability as a command, for scripts and local AI operators.",
            "",
            "Usage: appwrap <command> [arguments] [options] [--json] [--verbose]",
            "",
            "Commands:",
        };
        foreach (var spec in CliArguments.Commands.Values) lines.Add($"  {spec.Key,-18} {spec.Summary}");
        lines.AddRange([
            "",
            "Global options (accepted anywhere):",
            "  --json       Print one JSON object: { ok, command, exitCode, result, diagnostics, error }.",
            "  --dry-run    For build: validate and print the plan; write nothing.",
            "  --verbose    Progress lines on stderr (stdout stays machine-clean).",
            "  --help       Help for the command (or this text).",
            "  --version    Print the AppWrap version.",
            "",
            "Exit codes: 0 success · 2 usage error · 3 validation failed (nothing written) · 4 build/test failure.",
            "Run 'appwrap <command> --help' for a command's options. Full reference: docs/CLI.md.",
        ]);
        return lines;
    }

    public static IReadOnlyList<string> For(string commandKey)
    {
        if (!CliArguments.Commands.TryGetValue(commandKey, out var spec)) return TopLevel();
        var lines = new List<string> { spec.Usage, "", spec.Summary };
        if (spec.Options.Count != 0)
        {
            lines.Add("");
            lines.Add("Options:");
            foreach (var option in spec.Options)
            {
                var flag = "--" + option.Name + (option.TakesValue ? " <value>" : "");
                lines.Add($"  {flag,-30} {option.Description}");
            }
        }
        if (CliArguments.Notes.TryGetValue(commandKey, out var notes)) { lines.Add(""); lines.AddRange(notes); }
        lines.Add("");
        lines.Add("Global: --json, --dry-run (build), --verbose, --help, --version. Exit codes: 0 ok · 2 usage · 3 validation · 4 failure.");
        return lines;
    }
}
