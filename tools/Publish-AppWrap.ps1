param([string]$Destination)
$ErrorActionPreference='Stop'
$Version='0.2.1'
$repository=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if(!$Destination){$Destination=[IO.Path]::GetFullPath((Join-Path $repository "../../outputs/AppWrap-$Version-win-x64"))}
if(Test-Path -LiteralPath $Destination){throw 'Publish destination must be new. Preserve the previous artifact and choose a different destination.'}
$dotnet=(Get-Command dotnet.exe -ErrorAction Stop).Source
& 'C:/Users/Sean Shaffer/Documents/DevTools/Invoke-DevBuild.ps1' -ProjectPath $repository -Executable $dotnet -ArgumentList @('publish','AppWrap.Gui/AppWrap.Gui.csproj','-c','Release','-r','win-x64','--self-contained','true','-p:PublishSingleFile=true','-p:IncludeNativeLibrariesForSelfExtract=false','-o',$Destination) -ArtifactPath @((Join-Path $Destination 'AppWrap.exe')) -TimeoutSeconds 240
if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}
& 'C:/Users/Sean Shaffer/Documents/DevTools/Invoke-DevBuild.ps1' -ProjectPath $repository -Executable $dotnet -ArgumentList @('publish','AppWrap.Cli/AppWrap.Cli.csproj','-c','Release','-r','win-x64','--self-contained','true','-p:PublishSingleFile=true','-p:IncludeNativeLibrariesForSelfExtract=false','-o',$Destination) -ArtifactPath @((Join-Path $Destination 'AppWrap.Cli.exe')) -TimeoutSeconds 240
if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}
New-Item -ItemType Directory -Path (Join-Path $Destination 'third_party') | Out-Null
foreach($backend in @('innosetup','portableapps','pal')){
  Copy-Item -LiteralPath (Join-Path $repository "third_party/$backend") -Destination (Join-Path $Destination 'third_party') -Recurse
}
foreach($file in @('README.md','LICENSE','THIRD-PARTY-NOTICES.md')){Copy-Item -LiteralPath (Join-Path $repository $file) -Destination $Destination}
New-Item -ItemType Directory -Path (Join-Path $Destination 'docs'),(Join-Path $Destination 'tools') | Out-Null
New-Item -ItemType Directory -Path (Join-Path $Destination 'native/UpgradeGuard') | Out-Null
Copy-Item -LiteralPath (Join-Path $repository 'native/UpgradeGuard/UpgradeGuard.cpp'),(Join-Path $repository 'native/UpgradeGuard/Build.cmd'),(Join-Path $repository 'native/UpgradeGuard/README.md'),(Join-Path $repository 'native/UpgradeGuard/AppWrap.UpgradeGuard.exe'),(Join-Path $repository 'native/UpgradeGuard/provenance.json'),(Join-Path $repository 'native/UpgradeGuard/pe-evidence.json') -Destination (Join-Path $Destination 'native/UpgradeGuard')
Copy-Item -LiteralPath (Join-Path $repository 'docs/architecture.md'),(Join-Path $repository 'docs/portable-wizard.patch') -Destination (Join-Path $Destination 'docs')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Prepare-PortableBackend.ps1') -Destination (Join-Path $Destination 'tools')
$runtimeLicenseRoot=Join-Path $env:USERPROFILE '.nuget/packages'
$licenses=Join-Path $Destination 'licenses'
New-Item -ItemType Directory -Path (Join-Path $licenses 'dotnet-runtime'),(Join-Path $licenses 'windowsdesktop-runtime') | Out-Null
foreach($name in @('LICENSE.TXT','THIRD-PARTY-NOTICES.TXT')){Copy-Item -LiteralPath (Join-Path $runtimeLicenseRoot "microsoft.netcore.app.runtime.win-x64/10.0.15/$name") -Destination (Join-Path $licenses 'dotnet-runtime')}
Copy-Item -LiteralPath (Join-Path $runtimeLicenseRoot 'microsoft.windowsdesktop.app.runtime.win-x64/10.0.15/LICENSE') -Destination (Join-Path $licenses 'windowsdesktop-runtime')
$files=Get-ChildItem -LiteralPath $Destination -File -Recurse | ForEach-Object { [pscustomobject]@{path=[IO.Path]::GetRelativePath($Destination,$_.FullName).Replace('\','/');bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash} }
@{schemaVersion=1;platform='win-x64';version=$Version;files=@($files)} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $Destination 'manifest.json')
Write-Output "Published: $Destination"
exit 0
