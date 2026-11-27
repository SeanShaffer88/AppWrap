param([string]$Destination, [string]$Version='0.2.5')
$ErrorActionPreference='Stop'
$repository=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if(!$Destination){$Destination=[IO.Path]::GetFullPath((Join-Path $repository "../../outputs/AppWrap-$Version-win-x64"))}
$Destination=[IO.Path]::GetFullPath($Destination)
if(Test-Path -LiteralPath $Destination){throw 'Publish destination must be new. Preserve the previous artifact and choose a different destination.'}

# Sandboxie is a runtime dependency for the sandboxed profiling feature.
# Prepare the pinned installer and its source/license bundle at build time;
# the installed AppWrap setup later runs only these local files.
& (Join-Path $PSScriptRoot 'Prepare-SandboxieBackend.ps1')

# Fail before publishing if any required runtime packaging engine is absent.
# The audit extraction engines are optional and continue to degrade explicitly.
$requiredBackends=@(
  'third_party/innosetup/ISCC.exe',
  'third_party/innosetup/license.txt',
  'third_party/portableapps/AppWrapPackagingBuilder.exe',
  'third_party/portableapps/App/nsis/makensis.exe',
  'third_party/portableapps/App/nsis/Include/MUI2.nsh',
  'third_party/portableapps/App/nsis/Include/WordFunc.nsh',
  'third_party/portableapps/App/nsis/COPYING',
  'third_party/portableapps/Other/Source/License.txt',
  'third_party/pal/App/NSIS/makensis.exe',
  'third_party/pal/App/NSIS/COPYING',
  'third_party/pal/Other/Source/PortableApps.comLauncher.nsi',
  'third_party/pal/Other/Source/License.txt',
  'third_party/sandboxie/Sandboxie-Plus-x64-v1.18.8.exe',
  'third_party/sandboxie/Sandboxie-v1.18.8-source.zip',
  'third_party/sandboxie/LICENSE.Classic',
  'third_party/sandboxie/sha256-checksums.txt',
  'third_party/sandboxie/provenance.json'
)
foreach($relativePath in $requiredBackends){
  if(-not (Test-Path -LiteralPath (Join-Path $repository $relativePath))){
    throw "Required runtime dependency is missing: $relativePath. Prepare the backend before publishing."
  }
}

$dotnet=(Get-Command dotnet.exe -ErrorAction Stop).Source
Push-Location $repository
try {
  & $dotnet 'publish' 'AppWrap.Gui/AppWrap.Gui.csproj' '-c' 'Release' '-r' 'win-x64' '--self-contained' 'true' '-p:PublishSingleFile=true' '-p:IncludeNativeLibrariesForSelfExtract=false' "-p:Version=$Version" '-o' $Destination
  if($LASTEXITCODE -ne 0){throw "dotnet publish failed with exit code $LASTEXITCODE."}
} finally {
  Pop-Location
}
# Dual-mode since 0.2.3: AppWrap.Cli is a library hosted by AppWrap.exe, so
# there is no second self-contained publish. AppWrap.Cli.exe in the
# distribution is the tiny native shim (native/AppWrap.Cli.exe, source
# native/AppWrapCliShim.c) that forwards to AppWrap.exe --cli.
Copy-Item -LiteralPath (Join-Path $repository 'native/AppWrap.Cli.exe') -Destination (Join-Path $Destination 'AppWrap.Cli.exe')
New-Item -ItemType Directory -Path (Join-Path $Destination 'third_party') | Out-Null
foreach($backend in @('innosetup','portableapps','pal','engines')){
  Copy-Item -LiteralPath (Join-Path $repository "third_party/$backend") -Destination (Join-Path $Destination 'third_party') -Recurse
}
Copy-Item -LiteralPath (Join-Path $repository 'third_party/sandboxie') -Destination (Join-Path $Destination 'third_party') -Recurse
foreach($file in @('README.md','LICENSE','THIRD-PARTY-NOTICES.md')){Copy-Item -LiteralPath (Join-Path $repository $file) -Destination $Destination}
New-Item -ItemType Directory -Path (Join-Path $Destination 'docs') | Out-Null
New-Item -ItemType Directory -Path (Join-Path $Destination 'native/UpgradeGuard') | Out-Null
Copy-Item -LiteralPath (Join-Path $repository 'native/UpgradeGuard/UpgradeGuard.cpp'),(Join-Path $repository 'native/UpgradeGuard/README.md'),(Join-Path $repository 'native/UpgradeGuard/AppWrap.UpgradeGuard.exe'),(Join-Path $repository 'native/UpgradeGuard/provenance.json'),(Join-Path $repository 'native/UpgradeGuard/pe-evidence.json') -Destination (Join-Path $Destination 'native/UpgradeGuard')
Copy-Item -LiteralPath (Join-Path $repository 'docs/architecture.md'),(Join-Path $repository 'docs/portable-wizard.patch') -Destination (Join-Path $Destination 'docs')
$runtimeLicenseRoot=Join-Path $env:USERPROFILE '.nuget/packages'
$licenses=Join-Path $Destination 'licenses'
New-Item -ItemType Directory -Path (Join-Path $licenses 'dotnet-runtime'),(Join-Path $licenses 'windowsdesktop-runtime') | Out-Null
foreach($name in @('LICENSE.TXT','THIRD-PARTY-NOTICES.TXT')){Copy-Item -LiteralPath (Join-Path $runtimeLicenseRoot "microsoft.netcore.app.runtime.win-x64/10.0.15/$name") -Destination (Join-Path $licenses 'dotnet-runtime')}
Copy-Item -LiteralPath (Join-Path $runtimeLicenseRoot 'microsoft.windowsdesktop.app.runtime.win-x64/10.0.15/LICENSE') -Destination (Join-Path $licenses 'windowsdesktop-runtime')
$files=Get-ChildItem -LiteralPath $Destination -File -Recurse | ForEach-Object { [pscustomobject]@{path=[IO.Path]::GetRelativePath($Destination,$_.FullName).Replace('\','/');bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash} }
@{schemaVersion=1;platform='win-x64';version=$Version;files=@($files)} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $Destination 'manifest.json')
Write-Output "Published: $Destination"
