param([Parameter(Mandatory)][string]$ExtractedPath)
$ErrorActionPreference='Stop'
$target=Join-Path $PSScriptRoot '../third_party/portableapps'
if(Test-Path $target){throw 'Backend destination already exists; preserve it and select a clean checkout.'}
New-Item -ItemType Directory -Path $target | Out-Null
Copy-Item -LiteralPath (Join-Path $ExtractedPath 'App') -Destination $target -Recurse
Copy-Item -LiteralPath (Join-Path $ExtractedPath 'Other') -Destination $target -Recurse
Copy-Item -LiteralPath (Join-Path $ExtractedPath 'help.html') -Destination $target
$source=Join-Path $target 'Other/Source/InstallerWizard.nsi'
$original=(Get-Content -Raw -LiteralPath $source).Replace("`r`n","`n")
Copy-Item -LiteralPath $source -Destination ($source+'.upstream')
$modified=$original.Replace('!define APPNAME "PortableApps.com Installer"','!define APPNAME "AppWrap Packaging Builder (modified)"').Replace('OutFile "..\..\PortableApps.comInstaller.exe"','OutFile "..\..\AppWrapPackagingBuilder.exe"').Replace('VIAddVersionKey CompanyName "PortableApps.com"','VIAddVersionKey CompanyName "AppWrap contributors (modified wizard)"').Replace('VIAddVersionKey OriginalFilename "PortableApps.comInstaller.exe"','VIAddVersionKey OriginalFilename "AppWrapPackagingBuilder.exe"')
$modified=';Modified by AppWrap contributors, 2026-11-21: silent wrapper and relative launch target validation.'+"`r`n"+$modified
$modified=$modified.Replace('Unicode true',"Unicode true`r`nSilentInstall silent")
$guard=@'
${GetParameters} $R0
${If} $R0 == ""
  SetErrorLevel 2
  Quit
${EndIf}
'@
$modified=$modified.Replace('${GetParameters} $R0',$guard)
$init=@'
	StrCpy $bolInteractiveMode "false"
	StrCpy $bolAutomaticCompile "true"
	StrCpy $bolSkipWelcomePage "true"
	StrCpy $bolPluginInstaller "false"
	StrCpy $strAppInfoINIFile "$strInstallAppDirectory\App\AppInfo\appinfo.ini"
	StrCpy $strInstallerINIFile "$strInstallAppDirectory\App\AppInfo\installer.ini"
	${IfNot} ${FileExists} "$strAppInfoINIFile"
		SetErrorLevel 2
		Quit
	${EndIf}
'@
$modified=$modified.Replace(';=== Pre-Fill Path with Directory',$init+"`r`n`t;=== Pre-Fill Path with Directory")
$old=@'
	${IfNot} ${FileExists} "$strInstallAppDirectory\*.exe"
	${AndIf} $bolPluginInstaller != "true"
		${WriteErrorToLog} "No EXE in $strInstallAppDirectory."
	${EndIf}
'@
$new=@'
	ReadINIStr $R0 $strAppInfoINIFile "Control" "Start"
	${IfNot} ${FileExists} "$strInstallAppDirectory\$R0"
		${WriteErrorToLog} "The selected launch file is missing."
	${EndIf}
'@
$modified=$modified.Replace($old.Replace("`n","`r`n"),$new.Replace("`n","`r`n"))
# Match newline style independently; upstream releases may use LF or CRLF.
$modified=$modified.Replace($old.Replace("`r`n","`n"),$new.Replace("`r`n","`n"))
$modified=$modified.Replace('MessageBox MB_OK|MB_ICONEXCLAMATION "Unable to delete file: $0\$strInstallerFilename.paf.exe.  Please be sure the file is not in use"',';AppWrap: noninteractive error is recorded on the following line.')
$end=@'
	${If} $bolErrorOccurred == "true"
		SetErrorLevel 1
	${Else}
		SetErrorLevel 0
	${EndIf}
'@
$modified=$modified.Replace('${TBProgress_State} NoProgress', '${TBProgress_State} NoProgress'+"`r`n"+$end)
if($modified.Contains('${WriteErrorToLog} "No EXE in $strInstallAppDirectory."')){throw 'Root executable patch did not apply.'}
[IO.File]::WriteAllText($source,$modified,[Text.UTF8Encoding]::new($true))
& (Join-Path $target 'App/nsis/makensis.exe') /V2 $source
if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}
$patch=Join-Path $PSScriptRoot '../docs/portable-wizard.patch'
$diffRoot=Join-Path $env:TEMP ('appwrap-diff-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $diffRoot | Out-Null
$before=Join-Path $diffRoot 'InstallerWizard.upstream.nsi'; $after=Join-Path $diffRoot 'InstallerWizard.nsi'
[IO.File]::WriteAllText($before,$original,[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText($after,$modified,[Text.UTF8Encoding]::new($false))
& git diff --no-index --text -- $before $after | Set-Content -LiteralPath $patch
exit 0
