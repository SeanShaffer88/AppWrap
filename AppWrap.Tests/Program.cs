using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Media.Imaging;
using AppWrap.Core;
using AppWrap.Generators;
using AppWrap.Imaging;
using AppWrap.Build;

internal static class Program
{
    private static readonly List<string> Passed = [];
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.FirstOrDefault() == "--hold") { Console.WriteLine("owned child started"); Thread.Sleep(30000); return 0; }
        if (args.FirstOrDefault() == "--descendant") { Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, ArgumentList = { "--hold" } }); return 0; }
        if (args.FirstOrDefault() == "--flood") { Console.Write(new string('x', 200000)); Console.Error.Write(new string('y', 200000)); return 0; }
        if (args.FirstOrDefault() == "--fail") return 7;
        try { Run(args).GetAwaiter().GetResult(); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex); if (ex is BuildFailureException failure) Console.Error.WriteLine(failure.Details); return 1; }
    }
    private static void Check(string name, bool condition) { if (!condition) throw new Exception("FAIL: " + name); Passed.Add(name); Console.WriteLine("PASS: " + name); }
    private static void Reject(string name, Action action) { try { action(); } catch (UserInputException) { Check(name, true); return; } throw new Exception("FAIL: accepted " + name); }
    private static async Task Run(string[] args)
    {
        var root = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
        var evidence = Path.GetFullPath(Path.Combine(root, "../AppWrap-verification", DateTime.UtcNow.ToString("yyyyMMddTHHmmss") + "-" + Guid.NewGuid().ToString("N")[..6]));
        Directory.CreateDirectory(evidence);
        var source = Path.Combine(evidence, "sample"); Directory.CreateDirectory(Path.Combine(source, "bin"));
        File.Copy(Environment.ProcessPath!, Path.Combine(source, "bin", "Demo.exe"));
        File.WriteAllText(Path.Combine(source, "readme.txt"), "Owned compile-only sample; never run by AppWrap.");
        File.WriteAllText(Path.Combine(source, "companion.dll"), "Original DLL fixture bytes; never loaded.");
        Directory.CreateDirectory(Path.Combine(source, "resources")); File.WriteAllText(Path.Combine(source, "resources", "settings.json"), "{\"fixture\":true}");
        var input = new AppWrapInput(source, "AppWrap Test", "1.2", "bin\\Demo.exe", true, false, Path.Combine(evidence, "standard.exe"));
        InputRules.Validate(input);
        NamingChecks.Run(Check, Reject);
        StandardOptionsChecks.Run(input, Check, Reject);
        UpgradeGuardChecks.Run(input, Check, Reject);
        await BackendLogChecks.Run(evidence, Check);
        Check("nested executable discovery", InputRules.Executables(source).SequenceEqual(["bin\\Demo.exe"]));
        Check("four-part numeric version", InputRules.PackageVersion("01.2") == "1.2.0.0");
        foreach (var bad in new[] { "", "1.2.3.4.5", "1.x", "1..2", "65536", "-1" }) Reject("invalid version " + bad, () => InputRules.PackageVersion(bad));
        foreach (var bad in new[] { "x\r\n[Run]", "{#1+1}", "CON", "NUL.txt", "bad/name" }) Reject("unsafe name " + bad.Replace("\r\n", "newline"), () => InputRules.Validate(input with { AppName = bad }));
        Reject("literal preprocessor injection", () => InnoSetupGenerator.Literal("folder{#Exec('calc') }"));
        Reject("output inside source", () => InputRules.Validate(input with { OutputPath = Path.Combine(source, "out.exe") }));
        Reject("launch path escape", () => InputRules.Validate(input with { MainExecutable = "..\\outside.exe" }));
        var script = InnoSetupGenerator.Generate(input, "C:\\plain", "C:\\out");
        var newer = InnoSetupGenerator.Generate(input with { AppVersion = "2.0" }, "C:\\plain", "C:\\out");
        Check("stable upgrade identity", script.Split('\n').Single(l => l.Contains("AppId=", StringComparison.Ordinal)) == newer.Split('\n').Single(l => l.Contains("AppId=", StringComparison.Ordinal)));
        Check("literal braces and quotes escaped", InnoSetupGenerator.Literal("x{app}\"y") == "x{{app}\"\"y");
        Check("desktop default unchecked task", script.Contains("Name: desktopicon; Description: \"Create a Desktop shortcut (optional)\"; Flags: unchecked", StringComparison.Ordinal));
        Check("postinstall launch defaults selected", script.Contains("Flags: nowait postinstall skipifsilent", StringComparison.Ordinal));
        var metadata = PortableAppsGenerator.Generate(input, "[Details]\nPublisher=My Publisher\n[License]\nShareable=true\n");
        Check("portable existing publisher preserved", metadata.Contains("Publisher=My Publisher", StringComparison.Ordinal));
        Check("unknown licensing conservative", metadata.Contains("OpenSource=false", StringComparison.Ordinal) && metadata.Contains("Shareable=true", StringComparison.Ordinal));
        Check("portable native root launcher metadata", metadata.Contains("Start=" + PortableAppsGenerator.PackageId(input) + ".exe", StringComparison.Ordinal));
        Check("portable nested launch retained", LaunchShimGenerator.Generate(input, "C:\\launch.exe", "C:\\icon.ico").Contains("App\\Payload\\bin\\Demo.exe", StringComparison.Ordinal));
        Reject("launcher dollar preprocessor injection", () => LaunchShimGenerator.Generate(input with { MainExecutable = "${evil}.exe" }, "C:\\launch.exe", "C:\\icon.ico"));
        Reject("portable metadata injection", () => PortableAppsGenerator.Generate(input, "[Details]\nPublisher=$PLUGINSDIR\n"));
        var images = Path.Combine(evidence, "images"); ImageAssets.WriteIcons(null, images);
        using (var reader = new BinaryReader(File.OpenRead(Path.Combine(images, "appicon.ico"))))
        {
            reader.ReadUInt16(); Check("ICO type", reader.ReadUInt16() == 1); var count = reader.ReadUInt16(); Check("seven ICO frames", count == 7);
            var frames = new List<(int, int)>();
            for (int i = 0; i < count; i++) { var width = reader.ReadByte(); reader.ReadBytes(3); reader.ReadUInt16(); var bits = reader.ReadUInt16(); reader.ReadInt32(); reader.ReadInt32(); frames.Add((width == 0 ? 256 : width, bits)); }
            Check("six icon bit-depth pairs", new[] { 16, 32, 48 }.All(s => frames.Contains((s, 8)) && frames.Contains((s, 32))));
            Check("256 icon frame", frames.Contains((256, 32)));
        }
        foreach (int size in new[] { 16, 32, 75, 128, 256 }) { var image = ImageAssets.Load(Path.Combine(images, $"appicon_{size}.png")); Check("PNG dimensions " + size, image.PixelWidth == size && image.PixelHeight == size); }
        var transparent = System.Windows.Media.Imaging.BitmapSource.Create(2, 2, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, new byte[16], 8);
        var alphaPath = Path.Combine(images, "transparent.png"); var alphaEncoder = new PngBitmapEncoder(); alphaEncoder.Frames.Add(BitmapFrame.Create(transparent)); using (var file = File.Create(alphaPath)) alphaEncoder.Save(file);
        var alphaIcons = Path.Combine(images, "alpha"); ImageAssets.WriteIcons(alphaPath, alphaIcons); var alphaPixels = new byte[16 * 16 * 4]; ImageAssets.Load(Path.Combine(alphaIcons, "appicon_16.png")).CopyPixels(alphaPixels, 16 * 4, 0);
        Check("selected icon alpha preserved", alphaPixels.Where((_, i) => i % 4 == 3).All(a => a == 0));
        var bmp = Path.Combine(images, "sidebar.bmp"); ImageAssets.WriteSidebar(Path.Combine(images, "appicon_128.png"), bmp);
        using (var reader = new BinaryReader(File.OpenRead(bmp))) { reader.BaseStream.Position = 18; Check("sidebar dimensions", reader.ReadInt32() == 164 && reader.ReadInt32() == 314); reader.BaseStream.Position = 28; Check("sidebar 24bit", reader.ReadUInt16() == 24); }
        var malformed = Path.Combine(images, "broken.png"); File.WriteAllText(malformed, "bad"); bool invalid = false; try { ImageAssets.Load(malformed); } catch (Exception) { invalid = true; } Check("malformed image rejected", invalid);
        var service = new BuildService(root, Path.Combine(evidence, "state"));
        await service.BuildAsync(input, CancellationToken.None);
        Check("real standard compile", File.Exists(input.OutputPath) && new FileInfo(input.OutputPath).Length > 10000);
        await StandardOptionsChecks.CompileAsync(service, input, evidence, Check);
        var original = File.ReadAllBytes(input.OutputPath);
        using (var cancelled = new CancellationTokenSource()) { cancelled.Cancel(); try { await service.BuildAsync(input, cancelled.Token); } catch (OperationCanceledException) { } }
        Check("cancel preserves existing output", original.SequenceEqual(File.ReadAllBytes(input.OutputPath)));
        try { await service.BuildAsync(input with { OutputPath = Path.Combine(root, "app.exe") }, CancellationToken.None); throw new Exception("accepted own output"); } catch (UserInputException) { Check("own runtime output protected", true); }
        try { await new BuildService(root, Path.Combine(source, "Data")).BuildAsync(input, CancellationToken.None); throw new Exception("accepted recursive state"); } catch (UserInputException) { Check("source-state overlap rejected", true); }
        var portable = input with { Mode = OutputMode.PortableApps, OutputPath = Path.Combine(evidence, "portable.paf.exe") };
        await service.BuildAsync(portable, CancellationToken.None);
        Check("real portable default compile", File.Exists(portable.OutputPath) && new FileInfo(portable.OutputPath).Length > 10000);
        var custom = portable with { LauncherIconPath = Path.Combine(images, "appicon_128.png"), SidebarImagePath = bmp, OutputPath = Path.Combine(evidence, "custom.paf.exe") };
        await service.BuildAsync(custom, CancellationToken.None);
        Check("real portable custom images compile", File.Exists(custom.OutputPath) && new FileInfo(custom.OutputPath).Length > 10000);
        if (args.Length > 1)
        {
            var extracted = Path.Combine(evidence, "extracted");
            var extract = await ProcessRunner.RunAsync(args[1], ["x", "-y", "-o" + extracted, custom.OutputPath], evidence, CancellationToken.None);
            Check("portable static archive extraction", extract.ExitCode == 0);
            foreach (var path in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(source, path); var packed = Path.Combine(extracted, "App", "Payload", relative);
                Check("portable payload retained " + relative, File.Exists(packed) && File.ReadAllBytes(path).SequenceEqual(File.ReadAllBytes(packed)));
            }
            Check("portable root native launcher retained", File.Exists(Path.Combine(extracted, PortableAppsGenerator.PackageId(input) + ".exe")));
            Check("portable carries exact AppWrap MIT notice", File.ReadAllBytes(Path.Combine(root, "LICENSE")).SequenceEqual(File.ReadAllBytes(Path.Combine(extracted, "Other", "AppWrap-LICENSE.txt"))));
            Check("portable carries exact native helper notices", File.ReadAllBytes(Path.Combine(root, "native", "UpgradeGuard", "README.md")).SequenceEqual(File.ReadAllBytes(Path.Combine(extracted, "Other", "AppWrap-NATIVE-NOTICES.txt"))));
        }
        File.Copy(Path.Combine(source, "bin", "Demo.exe"), Path.Combine(source, "bin", "Bob's.exe"));
        await service.BuildAsync(portable with { MainExecutable = "bin\\Bob's.exe", OutputPath = Path.Combine(evidence, "apostrophe.paf.exe") }, CancellationToken.None);
        Check("apostrophe launch path real compile", File.Exists(Path.Combine(evidence, "apostrophe.paf.exe")));
        var flood = await ProcessRunner.RunAsync(Environment.ProcessPath!, ["--flood"], evidence, CancellationToken.None, TimeSpan.FromSeconds(5));
        Check("concurrent bounded log drains", flood.ExitCode == 0 && flood.Log.Length < 270000);
        Check("nonzero process exit retained", (await ProcessRunner.RunAsync(Environment.ProcessPath!, ["--fail"], evidence, CancellationToken.None)).ExitCode == 7);
        using (var cancel = new CancellationTokenSource(200))
        {
            bool cancelled = false; try { await ProcessRunner.RunAsync(Environment.ProcessPath!, ["--hold"], evidence, cancel.Token); } catch (OperationCanceledException) { cancelled = true; }
            Check("running process cancellation", cancelled);
        }
        var watch = Stopwatch.StartNew(); bool timeout = false;
        try { await ProcessRunner.RunAsync(Environment.ProcessPath!, ["--descendant"], evidence, CancellationToken.None, TimeSpan.FromMilliseconds(500)); } catch (TimeoutException) { timeout = true; }
        Check("descendant-pipe deadline", timeout && watch.Elapsed < TimeSpan.FromSeconds(6));
        Check("source unchanged", Directory.GetFiles(source, "*", SearchOption.AllDirectories).Length == 5 && File.ReadAllText(Path.Combine(source, "readme.txt")).StartsWith("Owned compile-only", StringComparison.Ordinal));
        Check("successful staging cleanup", Directory.GetDirectories(Path.Combine(evidence, "state", "Builds")).Length == 0);
        await BatchChecks.Run(root, evidence, input, service, args.Length > 1 ? args[1] : null, Check);
        await LongStageChecks.Run(root, evidence, input, Check);
        File.WriteAllText(Path.Combine(evidence, "results.json"), JsonSerializer.Serialize(new { passed = Passed.Count, checks = Passed, evidence, installersExecuted = false }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"RESULT: {Passed.Count} checks passed. Evidence: {evidence}");
    }
}
