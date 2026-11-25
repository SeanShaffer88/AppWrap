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
        var imageDefaults = BuildCommandOptions.FromInvocation(CliArguments.Parse(["build", "--source", "X"]))
            .WithProjectDefaults(null, null, "C:\\Proj\\icon.ico", "C:\\Proj\\side.bmp");
        check("cli project supplies image defaults", imageDefaults.Icon == "C:\\Proj\\icon.ico" && imageDefaults.Sidebar == "C:\\Proj\\side.bmp");
        var imageFlagsWin = BuildCommandOptions.FromInvocation(CliArguments.Parse(
                ["build", "--source", "X", "--icon", "F.ico", "--sidebar", "S.bmp"]))
            .WithProjectDefaults(null, null, "C:\\Proj\\icon.ico", "C:\\Proj\\side.bmp");
        check("cli image flags override project", imageFlagsWin.Icon == "F.ico" && imageFlagsWin.Sidebar == "S.bmp");

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

        // --- Config: the project document's image properties ------------------
        var artDir = Path.Combine(fixture, "art");
        Directory.CreateDirectory(artDir);
        var iconFile = Path.Combine(artDir, "icon.png");
        File.WriteAllText(iconFile, "fixture png bytes");
        var listProject = await CommandRouter.ExecuteAsync(CliArguments.Parse(["config", "list", fixture, "project"]), environment, CancellationToken.None);
        check("cli config list project document", listProject.ExitCode == 0 && listProject.Data!["fields"] is System.Text.Json.Nodes.JsonArray { Count: 2 });
        var setIcon = await CommandRouter.ExecuteAsync(CliArguments.Parse(["config", "set", fixture, "project", "Images", "IconPath", iconFile]), environment, CancellationToken.None);
        check("cli config set project icon stores relative", setIcon.ExitCode == 0 && setIcon.Data!["value"]!.GetValue<string>() == "art/icon.png");
        var getIcon = await CommandRouter.ExecuteAsync(CliArguments.Parse(["config", "get", fixture, "project", "Images", "IconPath"]), environment, CancellationToken.None);
        check("cli config get project icon", getIcon.ExitCode == 0 && getIcon.Data!["value"]!.GetValue<string>() == "art/icon.png"
            && getIcon.Data!["resolved"]!.GetValue<string>() == Path.GetFullPath(iconFile));
        check("cli config project icon persisted", ProjectStore.Load(fixture)?.IconPath == "art/icon.png");
        var infoWithIcon = await CommandRouter.ExecuteAsync(CliArguments.Parse(["project", "info", fixture]), environment, CancellationToken.None);
        check("cli project info shows icon", infoWithIcon.ExitCode == 0
            && infoWithIcon.Data!["icon"]!["path"]!.GetValue<string>() == "art/icon.png"
            && infoWithIcon.Data["icon"]!["exists"]!.GetValue<bool>()
            && infoWithIcon.Data["sidebar"]!["path"] is null);
        var unknownImageKey = await CommandRouter.ExecuteAsync(CliArguments.Parse(["config", "set", fixture, "project", "Images", "BannerPath", "x.png"]), environment, CancellationToken.None);
        check("cli config unknown project key is usage", unknownImageKey.ExitCode == 2 && (unknownImageKey.Error?.Contains("iconPath") ?? false));

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

        // --- Audit ------------------------------------------------------------
        check("cli parse audit command", CliArguments.Parse(["audit", "setup.msi"]).CommandKey == "audit");
        var auditSource = Path.Combine(evidence, "audit-source");
        Directory.CreateDirectory(auditSource);
        File.WriteAllBytes(Path.Combine(auditSource, "CliApp.exe"), [0x4D, 0x5A]);
        File.WriteAllText(Path.Combine(auditSource, "settings.ini"), "[app]\n");
        var auditZip = Path.Combine(evidence, "audit-sample.zip");
        System.IO.Compression.ZipFile.CreateFromDirectory(auditSource, auditZip);
        var draftPath = Path.Combine(evidence, "audit-draft.json");
        var audit = await CommandRouter.ExecuteAsync(
            CliArguments.Parse(["audit", auditZip, "--draft", draftPath, "--json"]), environment, CancellationToken.None);
        check("cli audit zip succeeds with draft", audit.ExitCode == 0 && File.Exists(draftPath)
            && audit.Data!["payloadFolder"] is not null && audit.Data["draftProfile"]!.GetValue<string>() == Path.GetFullPath(draftPath));
        var draftValidate = await CommandRouter.ExecuteAsync(CliArguments.Parse(["profile", "validate", draftPath]), environment, CancellationToken.None);
        check("cli audit draft validates as a profile", draftValidate.ExitCode == 0);
        var auditBad = await CommandRouter.ExecuteAsync(CliArguments.Parse(["audit", Path.Combine(evidence, "missing.zip")]), environment, CancellationToken.None);
        check("cli audit missing source is validation failure", auditBad.ExitCode == 3);
    }
}
