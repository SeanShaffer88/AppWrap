$ErrorActionPreference='Stop'
$helper='C:\Users\Sean Shaffer\Documents\DevTools\Invoke-DevBuild.ps1'
$auditRoot=$PSScriptRoot
& $helper -ProjectPath $auditRoot -Executable 'C:\Program Files\dotnet\dotnet.exe' -ArgumentList @('run','--project','AuditMutation.csproj','--configuration','Release') -TimeoutSeconds 120
$baselineExit=$LASTEXITCODE
if($baselineExit -ne 0){throw "Baseline failed: $baselineExit"}
$path=Join-Path $auditRoot 'Generator.cs'
$original=[IO.File]::ReadAllText($path)
$needle=' || value.Contains("{#", StringComparison.Ordinal)'
if(-not $original.Contains($needle)){throw 'Exact mutation target absent'}
try {
  [IO.File]::WriteAllText($path,$original.Replace($needle,''))
  & $helper -ProjectPath $auditRoot -Executable 'C:\Program Files\dotnet\dotnet.exe' -ArgumentList @('run','--project','AuditMutation.csproj','--configuration','Release') -TimeoutSeconds 120
  $mutationExit=$LASTEXITCODE
  if($mutationExit -ne 1){throw "Expected rejected mutation exit1; actual $mutationExit"}
} finally { [IO.File]::WriteAllText($path,$original) }
[pscustomobject]@{baselineExit=$baselineExit;mutationExit=$mutationExit;sharedSourceChanged=$false;isolatedCopyRestored=$true} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $auditRoot 'result.json')
exit 0
