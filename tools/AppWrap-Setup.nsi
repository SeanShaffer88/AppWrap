; AppWrap-Setup.nsi — per-user NSIS installer for AppWrap itself.
;
; Build (from the repository root, with the distribution folder assembled
; by tools/Publish-AppWrap.ps1):
;   makensis /DPAYLOAD=<assembled distribution folder> /DOUTFILE=<output Setup.exe> ^
;            /DUNINSTALL_LIST=<generated uninstall file list> tools/AppWrap-Setup.nsi
;
; PAYLOAD and OUTFILE are required. UNINSTALL_LIST is an optional include
; file with one `Delete "$INSTDIR\..."` line per payload file (deepest
; paths first) followed by `RMDir` lines for the payload directories; the
; release assembly generates it from the distribution manifest.json so the
; uninstaller removes exactly the shipped file set. Without it, the
; uninstaller falls back to removing the shipped top-level layout.
;
; Data handling: AppWrap keeps its runtime state in a Data folder beside
; the executables (portable semantics). The uninstaller NEVER deletes
; $INSTDIR\Data or anything else it did not install; the final RMDir of
; $INSTDIR is non-recursive, so the folder stays in place whenever user
; data or user-added files remain.

Unicode true
RequestExecutionLevel user
SetCompressor /SOLID lzma

!ifndef VERSION
  !define VERSION "0.2.3"
!endif
!ifndef PAYLOAD
  !define PAYLOAD "..\outputs\AppWrap-${VERSION}-win-x64"
!endif
!ifndef OUTFILE
  !define OUTFILE "AppWrap-v${VERSION}-x64-Setup.exe"
!endif

Name "AppWrap"
Caption "AppWrap ${VERSION} Setup"
OutFile "${OUTFILE}"
InstallDir "$LOCALAPPDATA\Programs\AppWrap"
InstallDirRegKey HKCU "Software\AppWrap" "InstallDir"

VIProductVersion "${VERSION}.0"
VIAddVersionKey "ProductName" "AppWrap"
VIAddVersionKey "ProductVersion" "${VERSION}"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "CompanyName" "SeanShaffer88"
VIAddVersionKey "FileDescription" "AppWrap ${VERSION} Setup"
VIAddVersionKey "LegalCopyright" "Copyright (c) Sean Shaffer. MIT License."

!include "MUI2.nsh"
!include "WordFunc.nsh"
!insertmacro VersionCompare

; --- Sandboxie-Plus bootstrap pin (pipeline stage 0) ---------------------
; These values mirror the ONE lock: SandboxieLock.Pinned in
; AppWrap.Core/Pipeline/PipelineLocks.cs, documented in docs/PIPELINE.md.
; Keep all three in sync. The x64 SHA-256 is the value independent mirrors
; compute for the file served from the official release; the Sandboxie
; project publishes no hash AppWrap can cite, so the lock marks it
; Unverified — the bootstrap still refuses a download that does not match
; it. No ARM64 hash is pinned, so on ARM64 hosts the bootstrap does NOT
; install automatically (it says so in the log and on the final page).
!define SBIE_VERSION "1.18.8"
!define SBIE_URL_X64 "https://github.com/sandboxie-plus/Sandboxie/releases/download/v1.18.8/Sandboxie-Plus-x64-v1.18.8.exe"
!define SBIE_URL_ARM64 "https://github.com/sandboxie-plus/Sandboxie/releases/download/v1.18.8/Sandboxie-Plus-ARM64-v1.18.8.exe"
!define SBIE_SHA256_X64 "f27078ff74a07906fd51476079e1a7a8f5e20c8bb75a9d2ae86169d47179ae76"
!define SBIE_SILENT_ARGS "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-"

Var SandboxieNote

!define MUI_ICON "..\AppWrap.Gui\Assets\AppWrap.ico"
!define MUI_UNICON "..\AppWrap.Gui\Assets\AppWrap.ico"
!define MUI_FINISHPAGE_RUN "$INSTDIR\AppWrap.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Open AppWrap"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!define MUI_PAGE_CUSTOMFUNCTION_SHOW AppWrapFinishShow
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

Function AppWrapFinishShow
  ${If} $SandboxieNote != ""
    SendMessage $mui.FinishPage.Text ${WM_SETTEXT} 0 "STR:$SandboxieNote"
  ${EndIf}
FunctionEnd

; Writes one line to the install log (DetailPrint) and to
; $INSTDIR\Data\sandboxie-bootstrap.log. Expects the line in $SandboxieNote
; when it should also land on the final page; this function takes the line
; in $0 and preserves $SandboxieNote.
Function SbieLog
  DetailPrint "$0"
  CreateDirectory "$INSTDIR\Data"
  FileOpen $9 "$INSTDIR\Data\sandboxie-bootstrap.log" a
  FileSeek $9 0 END
  FileWrite $9 "$0$\r$\n"
  FileClose $9
FunctionEnd

Function BootstrapSandboxie
  ; Detection: the official installer's uninstall key (AppId
  ; "Sandboxie-Plus"), the SbieSvc service key, and Start.exe on disk.
  ReadRegStr $0 HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Sandboxie-Plus_is1" "DisplayVersion"
  ReadRegStr $1 HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Sandboxie-Plus_is1" "Inno Setup: App Path"
  ReadRegStr $2 HKLM "SYSTEM\CurrentControlSet\Services\SbieSvc" "ImagePath"
  ${If} $1 == ""
    StrCpy $1 "$PROGRAMFILES64\Sandboxie-Plus"
  ${EndIf}
  StrCpy $4 "absent" ; start.exe state
  ${If} ${FileExists} "$1\Start.exe"
    StrCpy $4 "present"
  ${EndIf}

  ${If} $2 != ""
  ${AndIf} $4 == "present"
    ${If} $0 == ""
      StrCpy $SandboxieNote "Sandboxie-Plus is installed (version unreadable; left as-is). Sandboxed profiling is available."
      StrCpy $0 "$SandboxieNote"
      Call SbieLog
      Return
    ${EndIf}
    ${VersionCompare} "$0" "${SBIE_VERSION}" $R0
    ${If} $R0 != 2
      StrCpy $SandboxieNote "Sandboxie-Plus $0 is installed and meets the pinned minimum ${SBIE_VERSION}. Sandboxed profiling is available."
      StrCpy $0 "$SandboxieNote"
      Call SbieLog
      Return
    ${EndIf}
    StrCpy $0 "Sandboxie-Plus $0 is older than the pinned ${SBIE_VERSION}; upgrading it for sandboxed profiling."
    Call SbieLog
  ${Else}
    StrCpy $0 "Sandboxie-Plus is not installed; installing the pinned ${SBIE_VERSION} for sandboxed profiling."
    Call SbieLog
  ${EndIf}

  ; Architecture: Sandboxie must match the host, not this installer.
  ReadRegStr $3 HKLM "SYSTEM\CurrentControlSet\Control\Session Manager\Environment" "PROCESSOR_ARCHITECTURE"
  ${If} $3 == "ARM64"
    StrCpy $SandboxieNote "Sandboxie-Plus was NOT installed automatically: no ARM64 installer hash is pinned yet. AppWrap works without it; install Sandboxie-Plus ${SBIE_VERSION} (ARM64) manually to enable sandboxed profiling."
    StrCpy $0 "$SandboxieNote"
    Call SbieLog
    Return
  ${EndIf}

  ; Download + verify + silent-install via a small PowerShell script
  ; (PowerShell ships with every supported Windows). Exit codes:
  ; 0 installed, 10 download failed, 11 hash mismatch, 12 installer failed.
  InitPluginsDir
  FileOpen $9 "$PLUGINSDIR\sbie-bootstrap.ps1" w
  FileWrite $9 "$$ErrorActionPreference='Stop'$\r$\n"
  FileWrite $9 "try { Invoke-WebRequest -Uri '${SBIE_URL_X64}' -OutFile '$PLUGINSDIR\Sandboxie-Plus-x64-v${SBIE_VERSION}.exe' -UseBasicParsing } catch { exit 10 }$\r$\n"
  FileWrite $9 "$$h=(Get-FileHash -Algorithm SHA256 -LiteralPath '$PLUGINSDIR\Sandboxie-Plus-x64-v${SBIE_VERSION}.exe').Hash.ToLowerInvariant()$\r$\n"
  FileWrite $9 "if($$h -ne '${SBIE_SHA256_X64}'){ exit 11 }$\r$\n"
  FileWrite $9 "$$p=Start-Process -FilePath '$PLUGINSDIR\Sandboxie-Plus-x64-v${SBIE_VERSION}.exe' -ArgumentList '${SBIE_SILENT_ARGS}' -Wait -PassThru$\r$\n"
  FileWrite $9 "if($$p.ExitCode -ne 0){ exit 12 }$\r$\n"
  FileWrite $9 "exit 0$\r$\n"
  FileClose $9
  ExecWait 'powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$PLUGINSDIR\sbie-bootstrap.ps1"' $R1
  ${If} $R1 == 0
    StrCpy $SandboxieNote "Sandboxie-Plus ${SBIE_VERSION} was installed for sandboxed profiling (download verified against its pinned SHA-256)."
  ${ElseIf} $R1 == 10
    StrCpy $SandboxieNote "Sandboxie-Plus was NOT installed: the download from the official release failed. AppWrap works without it; sandboxed profiling will use the host fallback until Sandboxie-Plus is installed."
  ${ElseIf} $R1 == 11
    StrCpy $SandboxieNote "Sandboxie-Plus was NOT installed: the downloaded installer's SHA-256 did not match the pinned value, so it was refused. AppWrap works without it; sandboxed profiling will use the host fallback."
  ${ElseIf} $R1 == 12
    StrCpy $SandboxieNote "Sandboxie-Plus was NOT installed: its installer reported a failure. AppWrap works without it; sandboxed profiling will use the host fallback until Sandboxie-Plus is installed."
  ${Else}
    StrCpy $SandboxieNote "Sandboxie-Plus was NOT installed: the bootstrap step failed (exit $R1). AppWrap works without it; sandboxed profiling will use the host fallback until Sandboxie-Plus is installed."
  ${EndIf}
  StrCpy $0 "$SandboxieNote"
  Call SbieLog
FunctionEnd


Section "AppWrap" SecMain
  SectionIn RO

  SetOutPath "$INSTDIR"
  File /r "${PAYLOAD}\*.*"

  ; Start Menu shortcuts.
  CreateDirectory "$SMPROGRAMS\AppWrap"
  CreateShortcut "$SMPROGRAMS\AppWrap\AppWrap.lnk" "$INSTDIR\AppWrap.exe" "" "$INSTDIR\AppWrap.exe" 0
  CreateShortcut "$SMPROGRAMS\AppWrap\Uninstall AppWrap.lnk" "$INSTDIR\Uninstall.exe" "" "$INSTDIR\Uninstall.exe" 0

  WriteUninstaller "$INSTDIR\Uninstall.exe"

  ; Per-user Add/Remove Programs registration.
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\AppWrap" "DisplayName" "AppWrap"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\AppWrap" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\AppWrap" "Publisher" "SeanShaffer88"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\AppWrap" "DisplayIcon" "$INSTDIR\AppWrap.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\AppWrap" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\AppWrap" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\AppWrap" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\AppWrap" "NoModify" 1
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\AppWrap" "NoRepair" 1
SectionEnd

Section "Sandboxie-Plus profiling runtime" SecSandboxie
  SectionIn RO

  ; AppWrap's sandboxed profiler (docs/PIPELINE.md) needs Sandboxie-Plus.
  ; Per SEAN's 2026-11-25 decision this setup bootstraps it: a missing or
  ; outdated install is downloaded from the official release, verified
  ; against the pinned SHA-256, and installed silently (its Inno Setup
  ; silent form; Windows still shows its own UAC prompt for the kernel
  ; driver — that prompt is the operating system's, not AppWrap's).
  ; Any failure leaves AppWrap fully installed and working with the host
  ; fallback, and says so plainly here, in the log file, and on the
  ; final page. The decision logic mirrors SandboxieBootstrapPlanner.
  Call BootstrapSandboxie
SectionEnd

Section "Uninstall"
  ; Shortcuts and registration go first.
  Delete "$SMPROGRAMS\AppWrap\AppWrap.lnk"
  Delete "$SMPROGRAMS\AppWrap\Uninstall AppWrap.lnk"
  RMDir "$SMPROGRAMS\AppWrap"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\AppWrap"
  DeleteRegKey HKCU "Software\AppWrap"

  ; Remove exactly the shipped payload. The generated list skips Data\
  ; (user state) by construction; the fallback removes the shipped
  ; top-level layout only and likewise leaves Data\ alone.
!ifdef UNINSTALL_LIST
  !include "${UNINSTALL_LIST}"
!else
  Delete "$INSTDIR\AppWrap.exe"
  Delete "$INSTDIR\AppWrap.Cli.exe"
  Delete "$INSTDIR\*.pdb"
  Delete "$INSTDIR\*.dll"
  Delete "$INSTDIR\README.md"
  Delete "$INSTDIR\LICENSE"
  Delete "$INSTDIR\THIRD-PARTY-NOTICES.md"
  Delete "$INSTDIR\manifest.json"
  RMDir /r "$INSTDIR\third_party"
  RMDir /r "$INSTDIR\docs"
  RMDir /r "$INSTDIR\tools"
  RMDir /r "$INSTDIR\licenses"
  RMDir /r "$INSTDIR\native"
!endif

  Delete "$INSTDIR\Uninstall.exe"

  ; Non-recursive on purpose: succeeds only when nothing but the removed
  ; payload was present. If Data\ or user-added files remain, $INSTDIR
  ; (and the user data in it) stays untouched.
  RMDir "$INSTDIR"
SectionEnd
