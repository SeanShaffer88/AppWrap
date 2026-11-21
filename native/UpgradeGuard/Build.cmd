@echo off
call "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvarsamd64_x86.bat" 10.0.26100.0 -vcvars_ver=14.44 >nul
if errorlevel 1 exit /b %errorlevel%
cl.exe /nologo /std:c++20 /W4 /WX /O2 /MT /EHsc /GS /DUNICODE /D_UNICODE UpgradeGuard.cpp /FoUpgradeGuard.obj /FeAppWrap.UpgradeGuard.exe user32.lib shell32.lib /link /SUBSYSTEM:WINDOWS /DYNAMICBASE /NXCOMPAT
exit /b %errorlevel%
