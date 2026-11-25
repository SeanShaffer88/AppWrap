param(
    [string]$ArchivePath,
    [string]$SevenZipPath
)
$ErrorActionPreference='Stop'

# Pinned official PortableApps.com Launcher (PAL) release. These values are the
# EngineLock in AppWrap.Core and the evidence in docs/portable-capability-matrix.md;
# anything that does not match them byte-for-byte is refused, never substituted.
$pinnedVersion='2.2.9'
$pinnedArchiveName='PortableApps.comLauncher_2.2.9.paf.exe'
$pinnedPafSha256='7bc93e47b885d5e953fa2082a2e65169ae98128026ac4d747f774a23bd01345b'
$pinnedNsisVersion='v2.46.5-Unicode'
$pinnedMakensisSha256='47cd625d2d90f0cfdff411ba18c39c88ab7e9b28730fc81e5e14ed66ead6fb21'
$sourceUrl='https://portableapps.com/apps/development/portableapps.com_launcher'
$downloadUrl='https://portableapps.com/redir2/?a=PortableApps.comLauncher&s=s&d=pa&f=PortableApps.comLauncher_2.2.9.paf.exe'

$target=Join-Path $PSScriptRoot '../third_party/pal'
if(Test-Path $target){throw 'PAL backend destination already exists; preserve it and select a clean checkout.'}

if(-not $SevenZipPath){
    $sevenZipCommand=Get-Command '7z.exe' -ErrorAction SilentlyContinue
    if($sevenZipCommand){$SevenZipPath=$sevenZipCommand.Source}
    elseif(Test-Path 'C:\Program Files\7-Zip\7z.exe'){$SevenZipPath='C:\Program Files\7-Zip\7z.exe'}
    else{throw '7-Zip was not found. Install 7-Zip or pass -SevenZipPath.'}
}

$tempRoot=Join-Path $env:TEMP ('appwrap-pal-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempRoot | Out-Null
try {
    if($ArchivePath){
        $paf=$ArchivePath
        if(-not (Test-Path $paf)){throw "PAL archive not found: $ArchivePath"}
    } else {
        $paf=Join-Path $tempRoot $pinnedArchiveName
        Invoke-WebRequest -Uri $downloadUrl -OutFile $paf
    }
    $pafHash=(Get-FileHash -Algorithm SHA256 -LiteralPath $paf).Hash.ToLowerInvariant()
    if($pafHash -ne $pinnedPafSha256){throw "PAL archive hash mismatch. Expected $pinnedPafSha256, found $pafHash. Nothing was installed."}

    # Static extraction only: the downloaded PAF installer is never executed.
    $extract=Join-Path $tempRoot 'extracted'
    & $SevenZipPath 'x' '-y' "-o$extract" $paf | Out-Null
    if($LASTEXITCODE -ne 0){throw "7-Zip extraction failed with exit code $LASTEXITCODE."}

    $makensis=Join-Path $extract 'App/NSIS/makensis.exe'
    if(-not (Test-Path $makensis)){throw 'The extracted PAL archive does not contain App/NSIS/makensis.exe.'}
    $makensisHash=(Get-FileHash -Algorithm SHA256 -LiteralPath $makensis).Hash.ToLowerInvariant()
    if($makensisHash -ne $pinnedMakensisSha256){throw "Bundled makensis hash mismatch. Expected $pinnedMakensisSha256, found $makensisHash. Nothing was installed."}
    if(-not (Test-Path (Join-Path $extract 'Other/Source/PortableApps.comLauncher.nsi'))){throw 'The extracted PAL archive does not contain Other/Source/PortableApps.comLauncher.nsi.'}

    New-Item -ItemType Directory -Path (Join-Path $target 'App') | Out-Null
    Copy-Item -LiteralPath (Join-Path $extract 'App/NSIS') -Destination (Join-Path $target 'App') -Recurse
    if(Test-Path (Join-Path $extract 'App/AppInfo')){Copy-Item -LiteralPath (Join-Path $extract 'App/AppInfo') -Destination (Join-Path $target 'App') -Recurse}
    New-Item -ItemType Directory -Path (Join-Path $target 'Other') | Out-Null
    Copy-Item -LiteralPath (Join-Path $extract 'Other/Source') -Destination (Join-Path $target 'Other') -Recurse

    $extractedFiles=(Get-ChildItem -LiteralPath $extract -Recurse -File).Count
    $provenance=[ordered]@{
        version=$pinnedVersion
        pafSha256=$pafHash
        nsisVersion=$pinnedNsisVersion
        makensisSha256=$makensisHash
        source=$sourceUrl
        extractedFiles=$extractedFiles
        note='Prepared locally by tools/Prepare-PalBackend.ps1 from the hash-pinned official archive; the archive was statically extracted with 7-Zip and never executed. Backend binaries are not committed.'
    }
    [IO.File]::WriteAllText((Join-Path $target 'provenance.json'), ($provenance | ConvertTo-Json -Depth 3), [Text.UTF8Encoding]::new($false))
} finally {
    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}
