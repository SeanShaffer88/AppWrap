param(
    [string]$PackageOutputDirectory,
    [string]$ConsumerDirectory
)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7 -or -not $IsWindows) { throw 'PowerShell 7 on Windows is required.' }
$repository = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$runId = [Guid]::NewGuid().ToString('N')
if (-not $PackageOutputDirectory) { $PackageOutputDirectory = Join-Path $repository "artifacts/packed-modules/$runId" }
if (-not $ConsumerDirectory) { $ConsumerDirectory = Join-Path (Split-Path $repository -Parent) "AppWrap-packed-consumer/$runId" }
$PackageOutputDirectory = [IO.Path]::GetFullPath($PackageOutputDirectory)
$ConsumerDirectory = [IO.Path]::GetFullPath($ConsumerDirectory)
if (Test-Path -LiteralPath $PackageOutputDirectory) { throw 'Package output must be a new directory.' }
if (Test-Path -LiteralPath $ConsumerDirectory) { throw 'Consumer output must be a new directory.' }
if ($ConsumerDirectory.StartsWith($repository + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'The independent consumer must be outside the repository.' }
$dotnet = (Get-Command dotnet.exe -ErrorAction Stop).Source
New-Item -ItemType Directory -Path $PackageOutputDirectory | Out-Null
New-Item -ItemType Directory -Path $ConsumerDirectory | Out-Null
$evidence = [Collections.Generic.List[object]]::new()
function Run-Dotnet([string[]]$Arguments, [string]$Label) {
    $logPath = Join-Path $ConsumerDirectory "$Label.log"
    & $dotnet @Arguments *> $logPath
    $code = $LASTEXITCODE
    $evidence.Add([pscustomobject]@{ Step = $Label; ExitCode = $code; Log = $logPath; Arguments = $Arguments })
    if ($code -ne 0) { Get-Content -LiteralPath $logPath; throw "$Label failed with exit code $code. Evidence: $logPath" }
}
try {
    foreach ($module in @('Core','Generators','Imaging','Build')) {
        Run-Dotnet @('pack',(Join-Path $repository "AppWrap.$module/AppWrap.$module.csproj"),'-c','Release','--no-build','-o',$PackageOutputDirectory) "pack-$module"
    }
    @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0-windows</TargetFramework><UseWPF>true</UseWPF><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors><NuGetAudit>false</NuGetAudit></PropertyGroup>
  <ItemGroup><PackageReference Include="AppWrap.Core" Version="0.2.5"/><PackageReference Include="AppWrap.Generators" Version="0.2.5"/><PackageReference Include="AppWrap.Imaging" Version="0.2.5"/><PackageReference Include="AppWrap.Build" Version="0.2.5"/></ItemGroup>
</Project>
'@ | Set-Content -LiteralPath (Join-Path $ConsumerDirectory 'Consumer.csproj')
    @'
using AppWrap.Core;
using AppWrap.Generators;
using AppWrap.Imaging;
using AppWrap.Build;
using System.IO;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "owned-smoke");
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "payload"); Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "Sample.exe"), "Inert fixture; never executed.");
        var output = Path.Combine(root, "existing.exe"); File.WriteAllText(output, "Preserve me");
        var input = new AppWrapInput(source, "Packed Consumer", "1.2", "Sample.exe", true, false, output);
        InputRules.Validate(input);
        Check(InputRules.PackageVersion("1.2") == "1.2.0.0", "packed Core version contract");
        Check(StandardInstallerOptions.Normalize(input.StandardOptions) == new StandardInstallerOptions(), "packed additive standard options defaults");
        var options = new StandardInstallerOptions(false, true, true, "http://127.0.0.1:18765/packed");
        Check(StandardInstallerOptions.Normalize(options).DeveloperWebsite == "http://127.0.0.1:18765/packed", "packed pure website validation");
        Check(InputRules.Executables(source).Single() == "Sample.exe", "packed Core discovery");
        Check(PackageNaming.GetFileName("Packed Consumer", "1.2", OutputMode.Standard) == "Packed Consumer-1.2-Setup.exe", "packed canonical standard filename");
        Check(PackageNaming.GetFileName("Packed Consumer", "1.2", OutputMode.PortableApps, PackageNamingStyle.NameSetupVersion) == "Packed Consumer-Portable-1.2.paf.exe", "packed canonical portable filename");
        var script = InnoSetupGenerator.Generate(input, source, root);
        Check(script.Contains("PrivilegesRequired=lowest") && script.Contains("Sample.exe"), "packed standard generator");
        var optionalScript = InnoSetupGenerator.Generate(input with { StandardOptions = options }, source, root);
        Check(optionalScript.Contains("StartupOffered = True") && optionalScript.Contains("skipifsilent unchecked") && !optionalScript.Contains("Flags: nowait postinstall"), "packed standard finish choices");
        var metadata = PortableAppsGenerator.Generate(input);
        Check(metadata.Contains("PackageVersion=1.2.0.0") && metadata.Contains("[Control]"), "packed portable generator");
        var icons = Path.Combine(root, "icons"); ImageAssets.WriteIcons(null, icons);
        Check(ImageAssets.Load(Path.Combine(icons, "appicon_32.png")).PixelWidth == 32, "packed image loading");
        using (var reader = new BinaryReader(File.OpenRead(Path.Combine(icons, "appicon.ico"))))
        { Check(reader.ReadUInt16() == 0 && reader.ReadUInt16() == 1 && reader.ReadUInt16() >= 6, "packed multi-frame icon"); }
        var bmp = Path.Combine(root, "sidebar.bmp"); ImageAssets.WriteSidebar(Path.Combine(icons, "appicon_128.png"), bmp);
        using (var reader = new BinaryReader(File.OpenRead(bmp)))
        { reader.BaseStream.Position = 18; Check(reader.ReadInt32() == 164 && Math.Abs(reader.ReadInt32()) == 314, "packed sidebar dimensions"); reader.BaseStream.Position = 28; Check(reader.ReadUInt16() == 24, "packed sidebar color depth"); }
        var service = new BuildService(Path.Combine(root, "tools"), Path.Combine(root, "state"));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { service.BuildAsync(input, cancelled.Token).GetAwaiter().GetResult(); throw new Exception("Cancellation was ignored"); }
        catch (OperationCanceledException) { }
        Check(File.ReadAllText(output) == "Preserve me", "packed build cancellation preserves output");
        bool invalidPairRejected = false;
        try { service.BuildBothAsync(input, input, CancellationToken.None).GetAwaiter().GetResult(); }
        catch (UserInputException) { invalidPairRejected = true; }
        Check(invalidPairRejected && File.ReadAllText(output) == "Preserve me", "packed paired build rejects invalid mode pair without engine execution");
        Check(typeof(ProcessRunner).GetMethod("RunAsync") != null, "packed process adapter export");
        Console.WriteLine("PASS: all four actual packed modules consumed without project references.");
    }
    private static void Check(bool success, string description)
    { if (!success) throw new Exception(description); Console.WriteLine("PASS: " + description); }
}
'@ | Set-Content -LiteralPath (Join-Path $ConsumerDirectory 'Program.cs')
    $escapedSource = [Security.SecurityElement]::Escape($PackageOutputDirectory)
    "<configuration><packageSources><clear/><add key='local-artifacts' value='$escapedSource'/></packageSources></configuration>" | Set-Content -LiteralPath (Join-Path $ConsumerDirectory 'NuGet.Config')
    $consumerProject = Join-Path $ConsumerDirectory 'Consumer.csproj'
    Run-Dotnet @('restore',$consumerProject,'--source',$PackageOutputDirectory,'--packages',(Join-Path $ConsumerDirectory 'packages'),'--ignore-failed-sources') 'restore-consumer'
    Run-Dotnet @('run','--project',$consumerProject,'-c','Release','--no-restore') 'run-consumer'
    $packages = Get-ChildItem -LiteralPath $PackageOutputDirectory -Filter '*.nupkg' | ForEach-Object { [pscustomobject]@{ Path = $_.FullName; Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } }
    [pscustomobject]@{ Passed = $true; PackageDirectory = $PackageOutputDirectory; ConsumerDirectory = $ConsumerDirectory; Packages = @($packages); Steps = @($evidence) } | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $ConsumerDirectory 'result.json')
    Get-Content -LiteralPath (Join-Path $ConsumerDirectory 'run-consumer.log')
    "Evidence: $(Join-Path $ConsumerDirectory 'result.json')"
}
catch {
    [pscustomobject]@{ Passed = $false; Error = $_.Exception.Message; Steps = @($evidence) } | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $ConsumerDirectory 'result.json')
    throw
}
