# Native lifecycle probe execution trust review

Verdict: **approved for the exact owned local lifecycle test scope**, not a lifecycle pass. User explicitly authorized this test; controller approved lifecycle-contract.md. Auditor approval sent directly to Implementer before any fixture/installer execution.

## Exact source and binaries

Run root: `C:/Users/Sean Shaffer/Documents/Codex/2026-11-21/c/work/lifecycle-20261121-9f6a82c1`.

Product: `AppWrapLifecycle-20261121-9f6a82c1`; executable `AwProbe9f6a82c1.exe`.

Independently read `native/Probe.cpp`, `native/Config.h`, `Build-Native.ps1`, `Build-v1.cmd`, `native/probe1.rsp`, identity.json and actual v1/v2 build result manifests. Actual builds use installed MSVC 14.44, Windows SDK 10.0.26100, C++20, /MT /W4 /WX /O2, Windows subsystem, DYNAMICBASE/NXCOMPAT, user32/shell32. No dependency or SDK installation.

Independently SHA256 hashed:

| Artifact | SHA256 |
|---|---|
| native/Probe.cpp | F3C721D2DE1DFA058D2D6E84EB897BE7032F02C21F51B1E5E11FC51AB625F209 |
| v1-source/bin/AwProbe9f6a82c1.exe | E3B06492AADCC9FC859086B11C877A12496DB59F69CA416A286E626549864A4B |
| v2-source/bin/AwProbe9f6a82c1.exe | F5190D3D3CBD64C7D955B6273BAC4DF58B71EBCB90E7CFDC8415DD2CA4447071 |

Both binaries are 325120 bytes. Actual build process/runner exits **0** in DevTools runs `20261121T215847167Z-a43ad630924f4dceb745946b394d66ce` and `20261121T215850087Z-692d17682d5c4bb49770b76172c44e1e`.

Independent static Get-DllInfo reads return **exit 0**, valid PE32+ x64, no CLR header, imports USER32.dll/SHELL32.dll/KERNEL32.dll. Static import inventory is not signature trust or exhaustive dynamic loader qualification; source review supports the stated fixture behavior.

## Reviewed execution boundaries

Config.h permits writes strictly below the exact run root above or `C:/Users/Sean Shaffer/AppData/Local/Programs/AppWrapLifecycle202611219f6a82c1-54419841B98E`. `weakly_canonical` plus case-insensitive separator-bounded comparison checks executable and write paths. The executable does not call shell commands, launch other processes, access network or write registry. It reads only its selected resource/settings files and records bounded JSON.

Arguments are captured with CommandLineToArgvW; `--record` remains subject to the compiled path allowlist, `--exit` permits 0..125, and `--hold` uses an owned Win32 message loop. Normal close/session events record reason and exit. Probe settings are created only if absent; resource checks distinguish v1/v2 and sibling/root resources. The test contract deliberately locates portable settings at package-root Data.

Auditor requested and verified two fixture reliability corrections before approval: SetTimer return failures now cause owned-window destruction/exit rather than silently defeating the 300-second lifetime cap; filesystem/logging exceptions are caught within the Win32 callback boundary. No AppWrap product-generator fix was inferred or made from these fixture corrections.

## Independent before-absence observation

Before sending execution approval, independently read only the exact targets from identity.json:

- Default per-user standard root absent.
- Unique Desktop shortcut absent.
- Unique Start Menu shortcut absent.
- Exact HKCU uninstall key `Software/Microsoft/Windows/CurrentVersion/Uninstall/AppWrap-AppWrapLifecycle202611219f6a82c1-54419841B98E_is1` absent in both Registry32 and Registry64 views.

Read-only audit shell command exited **0**; both static PE helper calls exited **0**. Implementer must still retain its own fresh baseline, generated package hashes/commands, exact owned process identities and runtime outcomes. Approval does not permit unrelated process closure, elevation, broad registry/filesystem cleanup or executing the earlier compile-only Demo.exe fixture.

Actual install/upgrade/uninstall, portable install/update/removal, marker preservation, launcher forwarding and final cleanup audit remain pending.
