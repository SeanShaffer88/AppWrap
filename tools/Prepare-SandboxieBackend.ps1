$ErrorActionPreference = 'Stop'

$version = '1.18.8'
$releaseTag = 'v1.18.8'
$installerName = 'Sandboxie-Plus-x64-v1.18.8.exe'
$checksumName = 'sha256-checksums.txt'
$installerSha256 = 'f27078ff74a07906fd51476079e1a7a8f5e20c8bb75a9d2ae86169d47179ae76'
$checksumFileSha256 = 'c98450eb27c5baee9f3365e73b0c1db828bdfebe1b6b057cfc803fb0ff59f013'
$sourceCommit = 'bde0890108a563f5d72eb8e57085be2b37177218'
$sourceArchiveSha256 = '318adc8ee59ff2d8001dcf0c51b72a3e503362cc652ccba94c6432e19c3b6596'
$releaseBase = "https://github.com/sandboxie-plus/Sandboxie/releases/download/$releaseTag"
$checksumUrl = "$releaseBase/$checksumName"
$installerUrl = "$releaseBase/$installerName"
$sourceUrl = "https://github.com/sandboxie-plus/Sandboxie/archive/$sourceCommit.zip"
$target = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../third_party/sandboxie'))

function Get-LowerSha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Test-PreparedBackend {
    $installer = Join-Path $target $installerName
    $checksumFile = Join-Path $target $checksumName
    $sourceArchive = Join-Path $target "Sandboxie-$releaseTag-source.zip"
    $license = Join-Path $target 'LICENSE.Classic'
    $provenancePath = Join-Path $target 'provenance.json'
    if (-not (Test-Path -LiteralPath $installer) -or
        -not (Test-Path -LiteralPath $checksumFile) -or
        -not (Test-Path -LiteralPath $sourceArchive) -or
        -not (Test-Path -LiteralPath $license) -or
        -not (Test-Path -LiteralPath $provenancePath)) {
        return $false
    }

    try {
        $provenance = Get-Content -Raw -LiteralPath $provenancePath | ConvertFrom-Json
        return (Get-LowerSha256 $installer) -eq $installerSha256 -and
            (Get-LowerSha256 $checksumFile) -eq $checksumFileSha256 -and
            (Get-LowerSha256 $sourceArchive) -eq $sourceArchiveSha256 -and
            $provenance.sourceArchiveSha256 -eq $sourceArchiveSha256 -and
            $provenance.version -eq $version -and
            $provenance.sourceCommit -eq $sourceCommit -and
            (Get-Content -Raw -LiteralPath $license).StartsWith('                    GNU GENERAL PUBLIC LICENSE')
    } catch {
        return $false
    }
}

if (Test-PreparedBackend) {
    Write-Output "Sandboxie-Plus $version backend is already prepared and verified."
    return
}

$tempBase = if ($env:TEMP) { $env:TEMP } else { [IO.Path]::GetTempPath() }
$tempRoot = Join-Path $tempBase ('appwrap-sandboxie-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempRoot | Out-Null
try {
    $apiHeaders = @{ 'User-Agent' = 'AppWrap backend preparation'; 'Accept' = 'application/vnd.github+json' }
    $tag = Invoke-RestMethod -Uri "https://api.github.com/repos/sandboxie-plus/Sandboxie/git/ref/tags/$releaseTag" -Headers $apiHeaders
    if ($tag.object.type -ne 'commit' -or $tag.object.sha -ne $sourceCommit) {
        throw "Sandboxie source tag $releaseTag did not resolve to the pinned commit. No files were prepared."
    }

    $checksumsPath = Join-Path $tempRoot $checksumName
    Invoke-WebRequest -Uri $checksumUrl -OutFile $checksumsPath -UseBasicParsing
    $observedChecksumFileSha256 = Get-LowerSha256 $checksumsPath
    if ($observedChecksumFileSha256 -ne $checksumFileSha256) {
        throw "Official Sandboxie checksum file hash mismatch. Expected $checksumFileSha256, found $observedChecksumFileSha256."
    }
    $checksumText = Get-Content -Raw -LiteralPath $checksumsPath
    $assetLine = [regex]::Match($checksumText, '(?m)^([a-f0-9]{64})\s+Sandboxie-Plus-x64-v1\.18\.8\.exe\s*$')
    if (-not $assetLine.Success -or $assetLine.Groups[1].Value -ne $installerSha256) {
        throw "Official Sandboxie checksum file did not contain the pinned x64 installer checksum."
    }

    $installerPath = Join-Path $tempRoot $installerName
    Invoke-WebRequest -Uri $installerUrl -OutFile $installerPath -UseBasicParsing
    $observedInstallerSha256 = Get-LowerSha256 $installerPath
    if ($observedInstallerSha256 -ne $installerSha256) {
        throw "Sandboxie installer hash mismatch. Expected $installerSha256, found $observedInstallerSha256."
    }

    $sourceArchiveName = "Sandboxie-$releaseTag-source.zip"
    $sourceArchivePath = Join-Path $tempRoot $sourceArchiveName
    Invoke-WebRequest -Uri $sourceUrl -OutFile $sourceArchivePath -UseBasicParsing
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($sourceArchivePath)
    try {
        $licenseEntry = $archive.Entries |
            Where-Object { $_.FullName.EndsWith('/LICENSE.Classic', [StringComparison]::Ordinal) } |
            Select-Object -First 1
        if ($null -eq $licenseEntry) {
            throw 'The pinned Sandboxie source archive does not contain LICENSE.Classic.'
        }
        $licensePath = Join-Path $tempRoot 'LICENSE.Classic'
        $inputStream = $licenseEntry.Open()
        $outputStream = [IO.File]::Create($licensePath)
        try {
            $inputStream.CopyTo($outputStream)
        } finally {
            $outputStream.Dispose()
            $inputStream.Dispose()
        }
    } finally {
        $archive.Dispose()
    }

    $licenseText = Get-Content -Raw -LiteralPath $licensePath
    if (-not $licenseText.StartsWith('                    GNU GENERAL PUBLIC LICENSE') -or
        -not $licenseText.Contains('Version 3, 29 June 2007')) {
        throw 'The pinned Sandboxie source archive does not contain the expected GPL-3.0 license text.'
    }

    $observedSourceArchiveSha256 = Get-LowerSha256 $sourceArchivePath
    if ($observedSourceArchiveSha256 -ne $sourceArchiveSha256) {
        throw "Sandboxie source archive hash mismatch. Expected $sourceArchiveSha256, found $observedSourceArchiveSha256."
    }
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    Copy-Item -LiteralPath $installerPath,$checksumsPath,$sourceArchivePath,$licensePath -Destination $target -Force
    $provenance = [ordered]@{
        version = $version
        releaseTag = $releaseTag
        releasePage = "https://github.com/sandboxie-plus/Sandboxie/releases/tag/$releaseTag"
        installer = $installerName
        installerSha256 = $installerSha256
        officialChecksumFile = $checksumName
        officialChecksumFileSha256 = $checksumFileSha256
        sourceCommit = $sourceCommit
        sourceArchive = $sourceArchiveName
        sourceArchiveUrl = $sourceUrl
        sourceArchiveSha256 = $sourceArchiveSha256
        license = 'GPL-3.0; LICENSE.Classic is included beside the source archive.'
    }
    [IO.File]::WriteAllText(
        (Join-Path $target 'provenance.json'),
        ($provenance | ConvertTo-Json -Depth 4),
        [Text.UTF8Encoding]::new($false))

    if (-not (Test-PreparedBackend)) {
        throw 'Prepared Sandboxie backend failed its final verification.'
    }
    Write-Output "Prepared Sandboxie-Plus $version with verified installer, official checksum file, GPL license, and pinned source archive."
} finally {
    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}
