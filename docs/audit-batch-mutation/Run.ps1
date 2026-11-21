$ErrorActionPreference='Stop'
$helper='C:\Users\Sean Shaffer\Documents\DevTools\Invoke-DevBuild.ps1'
& $helper -ProjectPath $PSScriptRoot -Executable 'C:\Program Files\dotnet\dotnet.exe' -ArgumentList @('run','--project','AuditMutation.csproj','--configuration','Release') -TimeoutSeconds 120
$baselineExit=$LASTEXITCODE
if($baselineExit -ne 0){throw "Baseline failed: $baselineExit"}
$path=Join-Path $PSScriptRoot 'Publisher.cs'
$original=[IO.File]::ReadAllText($path)
$needle='if (committed[i] && File.Exists(artifacts[i].Destination))'
if(-not $original.Contains($needle)){throw 'Exact mutation target absent'}
try {
 [IO.File]::WriteAllText($path,$original.Replace($needle,'if (committed[i] && !File.Exists(artifacts[i].Destination))'))
 & $helper -ProjectPath $PSScriptRoot -Executable 'C:\Program Files\dotnet\dotnet.exe' -ArgumentList @('run','--project','AuditMutation.csproj','--configuration','Release') -TimeoutSeconds 120
 $mutationExit=$LASTEXITCODE
 if($mutationExit -ne 1){throw "Expected mutation assertion failure1, actual $mutationExit"}
} finally {[IO.File]::WriteAllText($path,$original)}
@{baselineExit=$baselineExit;mutationExit=$mutationExit;sharedSourceChanged=$false;isolatedCopyRestored=$true}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $PSScriptRoot 'result.json')
exit 0
