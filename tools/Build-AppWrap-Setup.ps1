param(
    [string]$Destination,
    [string]$InstallerPath,
    [string]$Version = '0.2.5'
)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $Destination) {
    $Destination = Join-Path $repository "../../outputs/AppWrap-$Version-win-x64"
}
$Destination = [IO.Path]::GetFullPath($Destination)
if (-not $InstallerPath) {
    $InstallerPath = Join-Path (Split-Path -Parent $Destination) "AppWrap-v$Version-x64-Setup.exe"
}
$InstallerPath = [IO.Path]::GetFullPath($InstallerPath)
if (Test-Path -LiteralPath $InstallerPath) {
    throw 'Setup output already exists. Preserve the previous artifact and choose a new path.'
}
$relativeInstallerPath = [IO.Path]::GetRelativePath($Destination, $InstallerPath)
if (-not $relativeInstallerPath.StartsWith('..' + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal) -and
    -not [IO.Path]::IsPathRooted($relativeInstallerPath)) {
    throw 'Place the setup executable outside the payload folder so it cannot be packed into itself.'
}
New-Item -ItemType Directory -Path (Split-Path -Parent $InstallerPath) -Force | Out-Null

& (Join-Path $PSScriptRoot 'Publish-AppWrap.ps1') -Destination $Destination -Version $Version
if ($LASTEXITCODE -ne 0) { throw "Publish failed with exit code $LASTEXITCODE." }

$manifestPath = Join-Path $Destination 'manifest.json'
$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
$manifestFiles = @($manifest.files)
$deleteLines = [System.Collections.Generic.List[string]]::new()
$directorySet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($entry in $manifestFiles) {
    $relativePath = [string]$entry.path
    if ([string]::IsNullOrWhiteSpace($relativePath) -or
        $relativePath.StartsWith('Data/', [StringComparison]::OrdinalIgnoreCase)) {
        continue
    }
    if ($relativePath.Contains('"') -or $relativePath.Contains('$') -or
        $relativePath.Contains("`r") -or $relativePath.Contains("`n")) {
        throw "Payload path cannot be safely represented in the NSIS uninstall list: $relativePath"
    }
    $windowsPath = $relativePath.Replace('/', '\')
    $deleteLines.Add('Delete "$INSTDIR\' + $windowsPath + '"')
    $parts = $relativePath -split '/'
    for ($index = 1; $index -lt $parts.Length; $index++) {
        [void]$directorySet.Add(($parts[0..($index - 1)] -join '\'))
    }
}
foreach ($directory in ($directorySet | Sort-Object @{ Expression = { ($_ -split '\\').Count }; Descending = $true }, @{ Expression = { $_ }; Descending = $true })) {
    $deleteLines.Add('RMDir "$INSTDIR\' + $directory + '"')
}

$tempRoot = Join-Path $env:TEMP ('appwrap-setup-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempRoot | Out-Null
try {
    $uninstallList = Join-Path $tempRoot 'uninstall-list.nsh'
    [IO.File]::WriteAllLines($uninstallList, [string[]]$deleteLines.ToArray(), [Text.UTF8Encoding]::new($true))

    # Use the compiler already bundled with the PortableApps runtime backend.
    # No separate NSIS installation is needed to produce AppWrap Setup.
    $compiler = Join-Path $Destination 'third_party/portableapps/App/nsis/makensis.exe'
    if (-not (Test-Path -LiteralPath $compiler)) {
        throw 'Bundled NSIS compiler is missing from the PortableApps backend.'
    }
    $script = Join-Path $PSScriptRoot 'AppWrap-Setup.nsi'
    & $compiler "/DVERSION=$Version" "/DPAYLOAD=$Destination" "/DOUTFILE=$InstallerPath" "/DUNINSTALL_LIST=$uninstallList" $script
    if ($LASTEXITCODE -ne 0) { throw "NSIS failed with exit code $LASTEXITCODE." }
    if (-not (Test-Path -LiteralPath $InstallerPath) -or (Get-Item -LiteralPath $InstallerPath).Length -lt 1000000) {
        throw 'NSIS did not produce a complete setup executable.'
    }
    Write-Output "Setup created: $InstallerPath"
    Write-Output "Payload: $Destination"
} finally {
    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}
