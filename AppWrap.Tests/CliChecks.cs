using System.IO;
using System.Text.Json;
using AppWrap.Cli;
using AppWrap.Core;
using AppWrap.Core.Projects;

/// <summary>
/// Command-line interface checks: the parse layer (flags, defaults, usage
/// errors, project/flag precedence), the JSON envelope of a validate run
/// against a scaffolded fixture project, a config get/set round-trip through
/// the router, a profile validation, and a dry-run build that must write
/// nothing. The router is driven directly with a temporary environment, so
/// no console and no packaging backend is involved.
/// </summary>
internal static class CliChecks
{
    public static async Task RunAsync(string evidence, Action<string, bool> check, Action<string, Action> reject)
    {
        void RejectUsage(string name, Action action)
        {
            try { action(); } catch (CliUsageException) { check(name, true); return; }
            throw new Exception("FAIL: accepted " + name);
        }

        // --- Parsing -------------------------------------------------------
        var parsed = CliArguments.Parse(["build", "--source", "C:\\Apps\\Foo", "--mode", "portable", "--no-desktop", "--json"]);
        check("cli parse command and globals", parsed.CommandKey == "build" && parsed.Json && !parsed.DryRun);
        var options = BuildCommandOptions.FromInvocation(parsed);
        check("cli parse options", options.Source == "C:\\Apps\\Foo" && options.Mode == "portable"
            && options.Desktop == false && options.StartMenu == null && options.RunAfter == null);
        var bare = BuildCommandOptions.FromInvocation(CliArguments.Parse(["build", "--source", "X"]));
        check("cli build defaults", bare.Mode == "installer" && bare.Style == PackageNamingStyle.NameVersionSetup && !bare.Overwrite);
        check("cli parse subcommand", CliArguments.Parse(["project", "validate", "Somewhere"]).CommandKey == "project validate");
        check("cli parse equals form", CliArguments.Parse(["build", "--source=X"]).Value("source") == "X");
        check("cli build version option shadows global flag", BuildCommandOptions.FromInvocation(
            CliArguments.Parse(["build", "--source", "X", "--version", "2.5"])).Version == "2.5");
        check("cli global version flag elsewhere", CliArguments.Parse(["--version"]).Version
            && CliArguments.Parse(["project", "validate", "X", "--version"]).Version);
        RejectUsage("cli unknown flag rejected", () => CliArguments.Parse(["build", "--bogus"]));
        RejectUsage("cli unknown command rejected", () => CliArguments.Parse(["frobnicate"]));
        RejectUsage("cli missing subcommand rejected", () => CliArguments.Parse(["project"]));
        RejectUsage("cli unknown subcommand rejected", () => CliArguments.Parse(["project", "frobnicate", "X"]));
        RejectUsage("cli missing option value rejected", () => CliArguments.Parse(["build", "--source"]));
        RejectUsage("cli duplicate option rejected", () => CliArguments.Parse(["build", "--source", "A", "--source", "B"]));
        RejectUsage("cli missing positional rejected", () => CliArguments.Parse(["config", "get", "Only", "Three", "Args"]));
        RejectUsage("cli conflicting flags rejected", () => BuildCommandOptions.FromInvocation(
            CliArguments.Parse(["build", "--source", "X", "--start-menu", "--no-start-menu"])));
        RejectUsage("cli bad mode rejected", () => BuildCommandOptions.FromInvocation(
            CliArguments.Parse(["build", "--source", "X", "--mode", "zip"])));
        RejectUsage("cli bad filename style rejected", () => BuildCommandOptions.FromInvocation(
            CliArguments.Parse(["build", "--source", "X", "--filename-style", "Fancy"])));

        // --- Project defaults precedence (parse level) ---------------------
        var fromProject = BuildCommandOptions.FromInvocation(CliArguments.Parse(["build", "--source", "X"]))
            .WithProjectDefaults("Project Name", "NameOnly");
        check("cli project supplies defaults", fromProject.Name == "Project Name" && fromProject.Style == PackageNamingStyle.NameOnly);
        var flagsWin = BuildCommandOptions.FromInvocation(CliArguments.Parse(
                ["build", "--source", "X", "--name", "Flag Name", "--filename-style", "Name-Setup-Version"]))
            .WithProjectDefaults("Project Name", "NameOnly");
        check("cli flags override project", flagsWin.Name == "Flag Name" && flagsWin.Style == PackageNamingStyle.NameSetupVersion);
        var badProjectStyle = BuildCommandOptions.FromInvocation(CliArguments.Parse(["build", "--source", "X"]))
            .WithProjectDefaults(null, "NotAStyle");
        check("cli invalid stored style ignored", badProjectStyle.Style == PackageNamingStyle.NameVersionSetup && badProjectStyle.Name == null);

        // --- Router against a scaffolded fixture project -------------------
        var environment = new CliEnvironment(evidence, Path.Combine(evidence, "cli-state"), "test");
        var fixture = Path.Combine(evidence, "cli-project");
        ProjectScaffolder.CreateBlank(fixture, "Cli Fixture");

        var validate = await CommandRouter.ExecuteAsync(CliArguments.Parse(["project", "validate", fixture, "--json"]), environment, CancellationToken.None);
        check("cli validate clean scaffold", validate.ExitCode == 0 && validate.Ok);
        using (var document = JsonDocument.Parse(validate.ToJson()))
        {
            var root = document.RootElement;
            check("cli json envelope shape", root.GetProperty("ok").GetBoolean()
                && root.GetProperty("command").GetString() == "project validate"
                && root.GetProperty("exitCode").GetInt32() == 0
                && root.GetProperty("error").ValueKind == JsonValueKind.Null
                && root.GetProperty("diagnostics").ValueKind == JsonValueKind.Array
                && root.GetProperty("result").GetProperty("root").GetString() == Path.GetFullPath(fixture)
                && root.GetProperty("result").GetProperty("projectFile").GetBoolean());
        }
        var info = await CommandRouter.ExecuteAsync(CliArguments.Parse(["project", "info", fixture]), environment, CancellationToken.None);
        check("cli project info", info.ExitCode == 0 && info.Data is not null
            && info.Data["name"]!.GetValue<string>() == "Cli Fixture"
            && info.Data["inferred"]!.GetValue<bool>() == false);

        // --- Config round-trip ----------------------------------------------
        var get = await CommandRouter.ExecuteAsync(CliArguments.Parse(["config", "get", fixture, "appinfo", "Details", "Name"]), environment, CancellationToken.None);
        check("cli config get", get.ExitCode == 0 && get.Data!["value"]!.GetValue<string>() == "Cli Fixture");
        var set = await CommandRouter.ExecuteAsync(CliArguments.Parse(["config", "set", fixture, "appinfo", "Details", "Publisher", "CLI Test Publisher"]), environment, CancellationToken.None);
        check("cli config set", set.ExitCode == 0);
        var getAgain = await CommandRouter.ExecuteAsync(CliArguments.Parse(["config", "get", fixture, "appinfo", "Details", "Publisher"]), environment, CancellationToken.None);
        check("cli config round-trip", getAgain.Data!["value"]!.GetValue<string>() == "CLI Test Publisher");
        check("cli config persisted to disk", ProjectConfigWorkspace.Load(fixture).GetField(ProjectDocumentKind.AppInfo, "Details", "Publisher") == "CLI Test Publisher");
        var unknownDocument = await CommandRouter.ExecuteAsync(CliArguments.Parse(["config", "get", fixture, "bogus", "Details", "Name"]), environment, CancellationToken.None);
        check("cli config unknown document is usage", unknownDocument.ExitCode == 2);
        var unknownKey = await CommandRouter.ExecuteAsync(CliArguments.Parse(["config", "set", fixture, "appinfo", "Details", "NoSuchKey", "x"]), environment, CancellationToken.None);
        check("cli config unknown key is usage", unknownKey.ExitCode == 2 && (unknownKey.Error?.Contains("Name") ?? false));
        var badBoolean = await CommandRouter.ExecuteAsync(CliArguments.Parse(["config", "set", fixture, "appinfo", "License", "Shareable", "maybe"]), environment, CancellationToken.None);
        check("cli config boolean validated", badBoolean.ExitCode == 3);

        // --- Profile validation ----------------------------------------------
        var goodProfile = Path.Combine(evidence, "good-profile.ini");
        File.WriteAllText(goodProfile, "[Launch]\r\nProgramExecutable=CliApp.exe\r\n");
        var profileOk = await CommandRouter.ExecuteAsync(CliArguments.Parse(["profile", "validate", goodProfile]), environment, CancellationToken.None);
        check("cli profile valid", profileOk.ExitCode == 0 && profileOk.Diagnostics.Count == 0);
        var badProfile = Path.Combine(evidence, "bad-profile.ini");
        File.WriteAllText(badProfile, "[Bogus]\r\nX=1\r\n");
        var profileBad = await CommandRouter.ExecuteAsync(CliArguments.Parse(["profile", "validate", badProfile]), environment, CancellationToken.None);
        check("cli profile invalid is validation failure", profileBad.ExitCode == 3 && profileBad.Diagnostics.Any(d => d.Code == "UnknownSection"));

        // --- Dry-run build writes nothing ------------------------------------
        var source = Path.Combine(evidence, "cli-source");
        Directory.CreateDirectory(source);
        File.Copy(Environment.ProcessPath!, Path.Combine(source, "CliApp.exe"));
        var outputFolder = Path.Combine(evidence, "cli-out");
        Directory.CreateDirectory(outputFolder);
        var outputFile = Path.Combine(outputFolder, "app.exe");
        var dryRun = await CommandRouter.ExecuteAsync(
            CliArguments.Parse(["build", "--source", source, "--output", outputFile, "--dry-run"]), environment, CancellationToken.None);
        check("cli dry-run build validates", dryRun.ExitCode == 0 && dryRun.Data!["dryRun"]!.GetValue<bool>());
        check("cli dry-run writes nothing", !File.Exists(outputFile) && Directory.GetFiles(outputFolder).Length == 0);
        var dryBad = await CommandRouter.ExecuteAsync(
            CliArguments.Parse(["build", "--source", source, "--version", "1.x", "--output", outputFile, "--dry-run"]), environment, CancellationToken.None);
        check("cli dry-run invalid input is validation failure", dryBad.ExitCode == 3 && !File.Exists(outputFile));
    }
}
