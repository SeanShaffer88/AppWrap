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
  !define VERSION "0.2.2"
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

!define MUI_FINISHPAGE_RUN "$INSTDIR\AppWrap.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Open AppWrap"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

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
