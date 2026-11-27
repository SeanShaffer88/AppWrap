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
  !define VERSION "0.2.5"
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
; publishes in its official release checksum file. AppWrap embeds the
; installer and its corresponding source/license files; setup needs no
; network access. AppWrap supports Windows x64 only.
!define SBIE_VERSION "1.18.8"
!define SBIE_SHA256_X64 "f27078ff74a07906fd51476079e1a7a8f5e20c8bb75a9d2ae86169d47179ae76"
!define SBIE_SILENT_ARGS "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-"

Var SandboxieNote

!define MUI_ICON "../AppWrap.Gui/Assets/AppWrap.ico"
!define MUI_UNICON "../AppWrap.Gui/Assets/AppWrap.ico"
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

Function .onInit
  ReadRegStr $0 HKLM "SYSTEM\CurrentControlSet\Control\Session Manager\Environment" "PROCESSOR_ARCHITECTURE"
  ${If} $0 != "AMD64"
    MessageBox MB_ICONSTOP "AppWrap supports Windows 11 x64 only. This installer requires x64 Windows."
    Abort
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

  ; Only Windows x64 is supported.
  ReadRegStr $3 HKLM "SYSTEM\CurrentControlSet\Control\Session Manager\Environment" "PROCESSOR_ARCHITECTURE"
  ${If} $3 != "AMD64"
    StrCpy $SandboxieNote "Sandboxie-Plus was not installed: AppWrap supports Windows x64 only. The x64 Sandboxie installer was not run on this system."
    StrCpy $0 "$SandboxieNote"
    Call SbieLog
    Return
  ${EndIf}

  ; Verify the embedded official installer, then launch it via PowerShell
  ; (available on supported Windows). Start-Process lets its driver manifest
  ; raise the required Windows UAC prompt. Exit codes: 0 installed,
  ; 10 embedded file missing/read failed, 11 hash mismatch, 12 install failed.
  FileOpen $9 "$PLUGINSDIR\sbie-bootstrap.ps1" w
  FileWrite $9 "$$ErrorActionPreference='Stop'$\r$\n"
  FileWrite $9 "$$f='$PLUGINSDIR\Sandboxie-Plus-x64-v${SBIE_VERSION}.exe'$\r$\n"
  FileWrite $9 "if(-not (Test-Path -LiteralPath $$f)){ exit 10 }$\r$\n"
  FileWrite $9 "try { $$h=(Get-FileHash -Algorithm SHA256 -LiteralPath $$f).Hash.ToLowerInvariant() } catch { exit 10 }$\r$\n"
  FileWrite $9 "if($$h -ne '${SBIE_SHA256_X64}'){ exit 11 }$\r$\n"
  FileWrite $9 "$$p=Start-Process -FilePath $$f -ArgumentList '${SBIE_SILENT_ARGS}' -Wait -PassThru$\r$\n"
  FileWrite $9 "if($$p.ExitCode -ne 0){ exit 12 }$\r$\n"
  FileWrite $9 "exit 0$\r$\n"
  FileClose $9
  ExecWait 'powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$PLUGINSDIR\sbie-bootstrap.ps1"' $R1
  ${If} $R1 == 0
    StrCpy $SandboxieNote "Sandboxie-Plus ${SBIE_VERSION} was installed for sandboxed profiling (embedded installer verified against the official release checksum)."
  ${ElseIf} $R1 == 10
    StrCpy $SandboxieNote "Sandboxie-Plus was NOT installed: the embedded installer was missing or could not be read. AppWrap still works; sandboxed audits stop until Sandboxie-Plus is available."
  ${ElseIf} $R1 == 11
    StrCpy $SandboxieNote "Sandboxie-Plus was NOT installed: the embedded installer's SHA-256 did not match the official release checksum, so it was refused. AppWrap still works; sandboxed audits stop until Sandboxie-Plus is available."
  ${ElseIf} $R1 == 12
    StrCpy $SandboxieNote "Sandboxie-Plus was NOT installed: its installer reported a failure. AppWrap still works; sandboxed audits stop until Sandboxie-Plus is available."
  ${Else}
    StrCpy $SandboxieNote "Sandboxie-Plus was NOT installed: the bootstrap step failed (exit $R1). AppWrap still works; sandboxed audits stop until Sandboxie-Plus is available."
  ${EndIf}
  StrCpy $0 "$SandboxieNote"
  Call SbieLog
FunctionEnd


Section "AppWrap" SecMain
  SectionIn RO

  SetOutPath "$INSTDIR"
  File /r "${PAYLOAD}/*.*"

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
  ; Setup embeds the official release asset, verifies its pinned SHA-256,
  ; and installs it without network access (Inno Setup silent form; Windows
  ; still shows its own UAC prompt for the kernel driver).
  ; Any failure leaves AppWrap fully installed and working; sandboxed
  ; audits stop until Sandboxie is available. Say so plainly here, in the log, and on the
  ; final page. The decision logic mirrors SandboxieBootstrapPlanner.
  InitPluginsDir
  SetOutPath "$PLUGINSDIR"
  File /oname=Sandboxie-Plus-x64-v${SBIE_VERSION}.exe "${PAYLOAD}/third_party/sandboxie/Sandboxie-Plus-x64-v${SBIE_VERSION}.exe"
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

  ; The manifest describes the payload files but cannot hash itself.
  Delete "$INSTDIR\manifest.json"
  Delete "$INSTDIR\Uninstall.exe"

  ; Non-recursive on purpose: succeeds only when nothing but the removed
  ; payload was present. If Data\ or user-added files remain, $INSTDIR
  ; (and the user data in it) stays untouched.
  RMDir "$INSTDIR"
SectionEnd
