@echo off
rem Rebuilds AppWrap.Cli.exe (the dual-mode CLI shim) with the MSVC x64
rem toolset, mirroring native/UpgradeGuard/Build.cmd. Static CRT (/MT),
rem console subsystem, ASLR and DEP. The binary shipped in this folder
rem was cross-compiled with MinGW-w64 GCC instead; both routes produce a
rem dependency-free x64 console executable (see native/README.md).
call "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvarsamd64.bat" >nul
if errorlevel 1 exit /b %errorlevel%
rc.exe /nologo /fo"%~dp0AppWrapCliShim.res" "%~dp0AppWrapCliShim.rc"
if errorlevel 1 exit /b %errorlevel%
cl.exe /nologo /std:c17 /W4 /WX /O2 /MT /GS /DUNICODE /D_UNICODE "%~dp0AppWrapCliShim.c" "%~dp0AppWrapCliShim.res" /Fo"%~dp0AppWrapCliShim.obj" /Fe"%~dp0AppWrap.Cli.exe" /link /SUBSYSTEM:CONSOLE /DYNAMICBASE /NXCOMPAT
exit /b %errorlevel%
