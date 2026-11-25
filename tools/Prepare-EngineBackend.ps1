param(
    [string]$ArchiveDir,
    [string]$SevenZipPath
)
$ErrorActionPreference='Stop'

# Pinned official extraction-engine releases (pipeline stage 1). These values
# mirror the ExtractionEngineLock in AppWrap.Core (AppWrap.Core/Pipeline/
# PipelineLocks.cs) and docs/PIPELINE.md; keep all three in sync.
# Hash honesty: none of the three projects publishes a SHA-256 for these
# archives on a source AppWrap can cite, so no SHA-256 is pinned here.
# innoextract's official site publishes an MD5, which IS verified below.
# For every archive the SHA-256 actually seen is recorded in the backend's
# provenance.json as an observed (not pinned) value.
$engines=@(
    @{ Kind='SevenZip';    Folder='7zip';       Version='26.06';
       ArchiveName='7z2606-extra.7z';
       DownloadUrl='https://github.com/ip7z/7zip/releases/download/26.06/7z2606-extra.7z';
       Md5=$null; Executable='7za.exe' },
    @{ Kind='InnoExtract'; Folder='innoextract'; Version='1.9';
       ArchiveName='innoextract-1.9-windows.zip';
       DownloadUrl='https://constexpr.org/innoextract/files/innoextract-1.9-windows.zip';
       Md5='72d0d0dd874b6236eaa44411f4470ee1'; Executable='innoextract.exe' },
    @{ Kind='LessMsi';     Folder='lessmsi';     Version='2.12.9';
       ArchiveName='lessmsi-v2.12.9.zip';
       DownloadUrl='https://github.com/activescott/lessmsi/releases/download/v2.12.9/lessmsi-v2.12.9.zip';
       Md5=$null; Executable='lessmsi.exe' }
)

$targetRoot=Join-Path $PSScriptRoot '../third_party/engines'
foreach($engine in $engines){
    if(Test-Path (Join-Path $targetRoot $engine.Folder)){
        throw "Engine backend destination already exists: $($engine.Folder); preserve it and select a clean checkout."
    }
}

if(-not $SevenZipPath){
    $sevenZipCommand=Get-Command '7z.exe' -ErrorAction SilentlyContinue
    if($sevenZipCommand){$SevenZipPath=$sevenZipCommand.Source}
    elseif(Test-Path 'C:\Program Files\7-Zip\7z.exe'){$SevenZipPath='C:\Program Files\7-Zip\7z.exe'}
    else{throw '7-Zip was not found (needed to open the 7-Zip Extra archive). Install 7-Zip or pass -SevenZipPath.'}
}

$tempRoot=Join-Path $env:TEMP ('appwrap-engines-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempRoot | Out-Null
try {
    foreach($engine in $engines){
        if($ArchiveDir){
            $archive=Join-Path $ArchiveDir $engine.ArchiveName
            if(-not (Test-Path $archive)){throw "Engine archive not found: $archive"}
        } else {
            $archive=Join-Path $tempRoot $engine.ArchiveName
            Invoke-WebRequest -Uri $engine.DownloadUrl -OutFile $archive
        }
        if($engine.Md5){
            $md5=(Get-FileHash -Algorithm MD5 -LiteralPath $archive).Hash.ToLowerInvariant()
            if($md5 -ne $engine.Md5){throw "$($engine.Kind) archive MD5 mismatch. Expected $($engine.Md5), found $md5. Nothing was installed for this engine."}
        }
        $observedSha256=(Get-FileHash -Algorithm SHA256 -LiteralPath $archive).Hash.ToLowerInvariant()

        # Static extraction only: downloaded archives are never executed.
        $extract=Join-Path $tempRoot ('extract-'+$engine.Folder)
        if($engine.ArchiveName.EndsWith('.zip')){
            Expand-Archive -LiteralPath $archive -DestinationPath $extract
        } else {
            & $SevenZipPath 'x' '-y' "-o$extract" $archive | Out-Null
            if($LASTEXITCODE -ne 0){throw "7-Zip extraction of $($engine.ArchiveName) failed with exit code $LASTEXITCODE."}
        }
        if(-not (Get-ChildItem -LiteralPath $extract -Recurse -Filter $engine.Executable -File)){
            throw "The extracted $($engine.Kind) archive does not contain $($engine.Executable)."
        }

        $destination=Join-Path $targetRoot $engine.Folder
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $extract '*') -Destination $destination -Recurse -Force
        # Some archives nest the payload one folder deep; flatten if the
        # executable is not at the backend root EngineBackend looks at.
        if(-not (Test-Path (Join-Path $destination $engine.Executable))){
            $nested=Get-ChildItem -LiteralPath $destination -Recurse -Filter $engine.Executable -File | Select-Object -First 1
            $nestedRoot=$nested.Directory.FullName
            Get-ChildItem -LiteralPath $nestedRoot | Copy-Item -Destination $destination -Recurse -Force
        }

        $provenance=[ordered]@{
            engine=$engine.Kind
            version=$engine.Version
            archive=$engine.ArchiveName
            downloadUrl=$engine.DownloadUrl
            sha256Pinned=$false
            sha256Observed=$observedSha256
            md5Verified=[bool]$engine.Md5
            preparedUtc=[DateTime]::UtcNow.ToString('o')
        }
        $provenance | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $destination 'provenance.json') -Encoding UTF8
        Write-Host "Prepared $($engine.Kind) $($engine.Version) -> $destination"
    }
} finally {
    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}
