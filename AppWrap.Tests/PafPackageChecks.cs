using System.IO;
using AppWrap.Build;
using AppWrap.Core;
using AppWrap.Core.PortableProfiles;
using AppWrap.Core.Projects;
using AppWrap.Generators;

/// <summary>
/// Phase 3 (PAF Package output) checks. The tree-builder level is exercised with
/// launcher compilation disabled: staging a package folder needs no Windows
/// compiler, while the pinned makensis/PAL compiles remain Windows-only (covered
/// by the real portable compile checks in Program). The transactional folder
/// publisher is exercised directly, like BatchArtifactPublisher in BatchChecks.
/// </summary>
internal static class PafPackageChecks
{
    public static async Task RunAsync(string root, string evidence, AppWrapInput input, Action<string, bool> check, Action<string, Action> reject)
    {
        var work = Path.Combine(evidence, "paf-package"); Directory.CreateDirectory(work);
        var outRoot = Path.Combine(work, "out"); Directory.CreateDirectory(outRoot);
        var folderOut = Path.Combine(outRoot, PackageNaming.GetFolderName(input.AppName));

        // --- Destination validation shape ------------------------------------
        var pafInput = input with { Mode = OutputMode.PafPackage, OutputPath = folderOut };
        InputRules.Validate(pafInput);
        check("paf package folder destination validates", true);
        check("paf folder name matches generator package id", PackageNaming.GetFolderName(input.AppName) == PortableAppsGenerator.PackageId(input));
        reject("paf package exe destination rejected", () => InputRules.Validate(pafInput with { OutputPath = folderOut + ".exe" }));
        reject("paf package destination inside source rejected", () => InputRules.Validate(pafInput with { OutputPath = Path.Combine(input.SourceFolder, "pkg") }));
        reject("paf package destination containing source rejected", () => InputRules.Validate(pafInput with { OutputPath = Path.GetDirectoryName(Path.GetFullPath(input.SourceFolder))! }));
        var fileDestination = Path.Combine(outRoot, "occupied"); File.WriteAllText(fileDestination, "x");
        reject("paf package destination occupied by a file rejected", () => InputRules.Validate(pafInput with { OutputPath = fileDestination }));
        reject("file output still requires exe name", () => InputRules.Validate(input with { OutputPath = Path.Combine(outRoot, "noext") }));
        reject("paf package build into app folder rejected", () =>
        {
            var service = new BuildService(root, Path.Combine(work, "state-boundary"));
            service.BuildAsync(pafInput with { OutputPath = Path.Combine(root, "PafPackageOut") }, CancellationToken.None).GetAwaiter().GetResult();
        });

        // --- Fixture ----------------------------------------------------------
        var fixture = Path.Combine(work, "fixture"); Directory.CreateDirectory(Path.Combine(fixture, "bin"));
        File.Copy(Environment.ProcessPath!, Path.Combine(fixture, "bin", "Demo.exe"));
        File.WriteAllText(Path.Combine(fixture, "readme.txt"), "PAF package fixture; never run by AppWrap.");
        Directory.CreateDirectory(Path.Combine(fixture, "resources"));
        File.WriteAllText(Path.Combine(fixture, "resources", "settings.json"), "{\"fixture\":true}");
        var fixtureInput = new AppWrapInput(fixture, input.AppName, input.AppVersion, "bin\\Demo.exe", true, false, folderOut, OutputMode.PafPackage);

        async Task<string> Stage(string stageName, AppWrapInput stageInput, bool paf)
        {
            var stage = Path.Combine(work, stageName); Directory.CreateDirectory(stage);
            var payload = Path.Combine(stage, "payload");
            BuildService.Snapshot(stageInput.SourceFolder, payload, CancellationToken.None);
            return await PortableBuild.StagePackageAsync(stageInput, payload, stage, work, CancellationToken.None, pafPackage: paf, compileLauncher: false);
        }

        // --- PAF package tree --------------------------------------------------
        var stagePaf = Path.Combine(work, "stage-paf");
        var package = await Stage("stage-paf", fixtureInput, paf: true);
        string InPackage(params string[] parts) => Path.Combine(new[] { package }.Concat(parts).ToArray());
        check("paf package has appinfo", File.Exists(InPackage("App", "AppInfo", "appinfo.ini")));
        check("paf package has installer.ini beside appinfo", File.Exists(InPackage("App", "AppInfo", "installer.ini")));
        check("paf package payload retained", File.ReadAllBytes(InPackage("App", "Payload", "bin", "Demo.exe")).SequenceEqual(File.ReadAllBytes(Path.Combine(fixture, "bin", "Demo.exe")))
            && File.Exists(InPackage("App", "Payload", "readme.txt")) && File.Exists(InPackage("App", "Payload", "resources", "settings.json")));
        check("paf package has help and notices", File.Exists(InPackage("help.html")) && File.Exists(InPackage("Other", "AppWrap-LICENSE.txt")) && File.Exists(InPackage("Other", "AppWrap-NATIVE-NOTICES.txt")));
        check("paf package has empty Data placeholder", Directory.Exists(InPackage("Data")) && !Directory.EnumerateFileSystemEntries(InPackage("Data")).Any());
        var hookPath = InPackage("Other", "Source", "PortableApps.comInstallerCustom.nsh");
        var guardInPackage = InPackage("Other", "Source", "AppWrap.UpgradeGuard.exe");
        check("paf package carries first-party guard hook and helper", File.Exists(hookPath) && File.Exists(guardInPackage));
        check("paf package guard helper matches staged helper", File.ReadAllBytes(guardInPackage).SequenceEqual(File.ReadAllBytes(Path.Combine(stagePaf, "AppWrap.UpgradeGuard.exe"))));
        var hook = File.ReadAllText(hookPath);
        check("paf package hook addresses helper package-relatively", hook.Contains(PortableBuild.PafGuardHelperPath, StringComparison.Ordinal) && !hook.Contains(stagePaf, StringComparison.Ordinal));
        var installerText = File.ReadAllText(InPackage("App", "AppInfo", "installer.ini"));
        check("paf package keeps guard coupling (NONE only with hook)", installerText.Contains("CloseEXE=NONE", StringComparison.Ordinal) && File.Exists(hookPath));
        var appInfoText = File.ReadAllText(InPackage("App", "AppInfo", "appinfo.ini"));
        check("paf package appinfo targets format 3.9", appInfoText.Contains("Type=PortableApps.comFormat", StringComparison.Ordinal) && appInfoText.Contains("Version=3.9", StringComparison.Ordinal));
        // The fixture mirrors the official installer's expected input: a package
        // folder whose App/AppInfo carries appinfo.ini + installer.ini and whose
        // payload sits under App. The whole tree must validate at PAF 3.9.
        var diagnostics = PafValidator.Validate(package);
        check("paf package passes PafValidator with zero errors", diagnostics.All(d => d.Severity != PafDiagnosticSeverity.Error));

        // --- Existing portable staging is unchanged ----------------------------
        var packageFile = await Stage("stage-file", fixtureInput with { Mode = OutputMode.PortableApps }, paf: false);
        string InFilePackage(params string[] parts) => Path.Combine(new[] { packageFile }.Concat(parts).ToArray());
        check("portable staging appinfo identical to paf staging", File.ReadAllBytes(InFilePackage("App", "AppInfo", "appinfo.ini")).SequenceEqual(File.ReadAllBytes(InPackage("App", "AppInfo", "appinfo.ini"))));
        check("portable staging installer.ini identical to paf staging", File.ReadAllBytes(InFilePackage("App", "AppInfo", "installer.ini")).SequenceEqual(File.ReadAllBytes(InPackage("App", "AppInfo", "installer.ini"))));
        check("portable staging has no Data placeholder or loose guard helper", !Directory.Exists(InFilePackage("Data")) && !File.Exists(InFilePackage("Other", "Source", "AppWrap.UpgradeGuard.exe")));
        check("portable staging hook keeps build-machine helper path", File.ReadAllText(InFilePackage("Other", "Source", "PortableApps.comInstallerCustom.nsh")).Contains(Path.Combine(Path.GetDirectoryName(packageFile)!, "AppWrap.UpgradeGuard.exe"), StringComparison.Ordinal));

        // --- Pal profile staging ------------------------------------------------
        var profile = PortableProfileCodec.FromIniTexts("[Launch]\nProgramExecutable=Payload\\bin\\Demo.exe\n", "", null, null, PortableExecutionKind.Pal);
        var palPackage = await Stage("stage-pal", fixtureInput with { Profile = profile }, paf: true);
        var launcherIni = Path.Combine(palPackage, "App", "AppInfo", "Launcher", PortableAppsGenerator.PackageId(fixtureInput) + ".ini");
        check("paf package with profile has launcher ini", File.Exists(launcherIni) && File.ReadAllText(launcherIni).Contains("ProgramExecutable=Payload\\bin\\Demo.exe", StringComparison.Ordinal));
        check("paf package with profile passes PafValidator with zero errors", PafValidator.Validate(palPackage).All(d => d.Severity != PafDiagnosticSeverity.Error));

        // --- Payload hook refusal is unchanged ----------------------------------
        var hooked = Path.Combine(work, "hooked"); Directory.CreateDirectory(Path.Combine(hooked, "Other", "Source"));
        File.Copy(Path.Combine(fixture, "bin", "Demo.exe"), Path.Combine(hooked, "Demo.exe"));
        File.WriteAllText(Path.Combine(hooked, "Other", "Source", "evil.nsh"), "; source-supplied hook");
        var hookedInput = new AppWrapInput(hooked, input.AppName, input.AppVersion, "Demo.exe", true, false, folderOut, OutputMode.PafPackage);
        foreach (var paf in new[] { true, false })
        {
            var stage = Path.Combine(work, "stage-hook-" + paf); Directory.CreateDirectory(stage);
            var payload = Path.Combine(stage, "payload");
            BuildService.Snapshot(hooked, payload, CancellationToken.None);
            var refused = false;
            try { await PortableBuild.StagePackageAsync(hookedInput, payload, stage, work, CancellationToken.None, pafPackage: paf, compileLauncher: false); }
            catch (UserInputException) { refused = true; }
            check("payload installer hook refused (pafPackage: " + paf + ")", refused);
        }

        // --- Folder publication --------------------------------------------------
        var commitRoot = Path.Combine(work, "commit"); Directory.CreateDirectory(commitRoot);
        string MakeTree(string name, string markerText)
        {
            var dir = Path.Combine(commitRoot, name);
            Directory.CreateDirectory(Path.Combine(dir, "App", "AppInfo"));
            File.WriteAllText(Path.Combine(dir, "App", "AppInfo", "appinfo.ini"), markerText);
            Directory.CreateDirectory(Path.Combine(dir, "Data"));
            return dir;
        }
        var staged = MakeTree("staged-v1", "version one");
        var stagedSnapshot = SourceSnapshot.Compute(staged);
        var destination = Path.Combine(commitRoot, "DestPortable");
        var published = await FolderArtifactPublisher.PublishAsync(staged, stagedSnapshot.Sha256!, stagedSnapshot.TotalBytes, destination, FolderOutputIdentity.Capture(destination), CancellationToken.None);
        check("folder publish creates destination with staged hash", Directory.Exists(destination) && SourceSnapshot.Compute(destination).Sha256 == stagedSnapshot.Sha256 && published.Result.Sha256 == stagedSnapshot.Sha256 && published.Result.OutputPath == destination);
        check("folder publish leaves no owned temp folders", !Directory.EnumerateDirectories(commitRoot, ".appwrap-*").Any());

        File.WriteAllText(Path.Combine(destination, "marker.txt"), "user edit after publish");
        var existing = FolderOutputIdentity.Capture(destination);
        var staged2 = MakeTree("staged-v2", "version two");
        var staged2Snapshot = SourceSnapshot.Compute(staged2);
        var republished = await FolderArtifactPublisher.PublishAsync(staged2, staged2Snapshot.Sha256!, staged2Snapshot.TotalBytes, destination, existing, CancellationToken.None);
        check("folder publish replaces existing folder", File.ReadAllText(Path.Combine(destination, "App", "AppInfo", "appinfo.ini")) == "version two" && !File.Exists(Path.Combine(destination, "marker.txt")) && republished.Result.Sha256 == staged2Snapshot.Sha256);
        check("folder publish removes backup after success", !Directory.EnumerateDirectories(commitRoot, ".appwrap-*.backup").Any());

        var destination2 = Path.Combine(commitRoot, "DestRollback");
        Directory.CreateDirectory(Path.Combine(destination2, "App"));
        File.WriteAllText(Path.Combine(destination2, "App", "keep.txt"), "original");
        var original2 = FolderOutputIdentity.Capture(destination2);
        var staged3 = MakeTree("staged-v3", "version three");
        var staged3Snapshot = SourceSnapshot.Compute(staged3);
        var rolledBack = false;
        try
        {
            await FolderArtifactPublisher.PublishAsync(staged3, staged3Snapshot.Sha256!, staged3Snapshot.TotalBytes, destination2, original2, CancellationToken.None,
                beforeReplace: () => throw new InvalidOperationException("Simulated commit failure."));
        }
        catch (BuildFailureException) { rolledBack = true; }
        check("folder publish failure rolls back", rolledBack && File.ReadAllText(Path.Combine(destination2, "App", "keep.txt")) == "original" && !Directory.EnumerateDirectories(commitRoot, ".appwrap-*").Any());

        var identity = FolderOutputIdentity.Capture(destination2);
        File.WriteAllText(Path.Combine(destination2, "App", "changed.txt"), "changed after capture");
        reject("folder identity detects destination change", () => identity.Verify());
    }
}
