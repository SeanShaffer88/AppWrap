# Portable upgrade guard pre-execution review

Verdict: **approved for first process-control execution against only the declared owned lifecycle fixtures**. This is a source/fixture trust gate, not final runtime qualification. Auditor remains read-only with respect to host UI/processes; Implementer is the sole operator.

## Exact reviewed helper

- Source `native/UpgradeGuard/UpgradeGuard.cpp`, SHA256 **7D2C6833B7753347C779D75573EE320BDA1806D62767697D243C8213AD667787**.
- Executable `native/UpgradeGuard/AppWrap.UpgradeGuard.exe`, **112640 bytes**, SHA256 **EA6AADD1B3591E23641B2100DD5EB4F8D291C71A9FA2F16461E7D30FB1A9CEEF**.
- Independently hashed both exact files and statically inspected executable with Get-DllInfo: helper exit **0**, valid **x86** PE, USER32/SHELL32/KERNEL32 imports. Static PE inspection never loaded/executed the helper.
- Read ordinary installed MSVC/Windows SDK x86 build recipe: /MT /GS /W4 /WX /O2 /EHsc, DYNAMICBASE/NXCOMPAT and WINDOWS subsystem with standard wWinMain/CommandLineToArgvW/LocalFree. No custom CRT startup, runtime compiler or new external runtime.

## Reviewed boundaries

Canonical input paths reject traversal, device/UNC/ADS syntax, excessive lengths and reparse components; existing root must be a directory. Exact expected launcher and App/Payload selected executable paths remain under the chosen installation root. Basenames filter candidates only: QueryFullProcessImageName then compares full paths; limited-query/synchronize handles and PID/start-time checks establish exact target identity before window actions.

Handle RAII closes resources on early failures, vector growth and normal completion. No process creation, forced termination, elevation, network or persistent state. Payload windows receive bounded graceful WM_CLOSE first; matching shims get time to exit naturally and are considered only afterward. A noncooperative/headless process yields failure, not a force kill. Re-enumeration precedes success, with bounded attempts and a ten-second checked deadline; ordinary Windows calls are not claimed as a hard real-time barrier against OS/filesystem stalls or external relaunch after return.

Auditor requested and verified two targeted corrections before final hash approval: enumeration checks the shared deadline and checks it again before an empty-target success; an existing regular file cannot masquerade as the installation root. The final Windows subsystem avoids adding a console window to the installer flow.

Read UpgradeGuardGenerator.cs and PortableBuild embedding/settings coupling: generated trusted CustomCodePreInstall NSH embeds exact resource, invokes directly with discrete quoted selected-root/launcher/payload arguments, leaves OUTDIR unchanged and balances the register stack, aborts on launch/nonzero failure before payload changes. CloseEXE=NONE is emitted through the same trusted configuration path, never alone or from source-provided hooks. Source custom scripts remain rejected. Actual final package embedding/hash/order and failure semantics remain runtime/static-package gates.

## Negative refusal fixture approval

Independently read owned `work/lifecycle-20261121-9f6a82c1/native/Refuser.cpp` and its build recipe. It performs no file/registry/network/process writes, ignores WM_CLOSE and has a checked 30-second timer that destroys only its own window and exits naturally.

Source SHA256 **D50995B61EB4EBBE5498B91C68EC88E1BD20F5DB450587824550872ED81641B3**; executable SHA256 **38055ED75C56BA4053F41B79DF0714E47E9418109CAF26A17CC458D093D625D9**. Both independently hash-match. Static Get-DllInfo exits0, valid x64 PE, USER32/KERNEL32 imports. Approved only for the separate owned negative installation-root fixture and natural timeout recovery; no forced kill of it or other processes.

## Pending actual gates

Guarded package fresh installation, root-shim and direct/multiple payload upgrade, same-name foreign instance preservation, selected-destination isolation, refusal timeout abort preserving existing payload/Data hashes, malformed/reparse input rejection, executed embedded-helper identity, source/recipe/notices packaging and final independent evidence/diff review. The earlier unexplained stock WM_CLOSE incident is not counted as successful automatic closure.

## Corrected hook and actual runtime qualification

The final trusted hook leaves OUTDIR unchanged and uses pinned NSIS File /oname=$PLUGINSDIR syntax. It balances R1 on both outcomes. The earlier restore-OUTDIR form was replaced because static 7z extraction placed the root launcher under a virtual register path; the strict root-launcher extraction assertion was retained. Full suite 230 checks passed, exit0, run 20261121T223618610Z-a97272c349314a619d049940bc101142.

Independently read guard-final-fresh-result.json, guard-final-before-install-click.json, guard-final-running-result.json and guard-final-upgrade-result.json. Fresh v1 installer exit0; final v2 installer exit0, no driver stop/timeout. Held payload PID16036 and root shim PID18920 were alive immediately before Install, then payload recorded wm-close at heartbeat72 (about36seconds, below300second lifetime), both exited, installed v2 hash matched F5190D3D3CBD64C7D955B6273BAC4DF58B71EBCB90E7CFDC8415DD2CA4447071 and Data marker remained unchanged. This supports installer-driven graceful closure, unlike the earlier unexplained stock incident.

Exact tested final v2 PAF SHA256 B38C63D9587E5B830ECD2B8DAD740CB531F6BE694ABBCCAA28DFCAF314D0784E. Independently streamed its embedded $PLUGINSDIR/AppWrap.UpgradeGuard.exe using static 7z extraction: exit0,112640bytes,MZ header,SHA256 EA6AADD1B3591E23641B2100DD5EB4F8D291C71A9FA2F16461E7D30FB1A9CEEF, identical to approved helper. No installer/helper execution by auditor.

Read supporting multiple-selected, foreign-instance, refusal, invalid-path and relocation evidence. Two direct selected payloads closed; a same-basename process at another root remained alive and unchanged. Refusal returned4 while the process was alive, GUI updater aborted2, all18 existing files hash-identical, then the fixture exited naturally at30seconds. File-root/traversal/reparse inputs returned2. Relocated root launch retained arguments/cwd/resources/settings. These supporting cases used the preceding hook form with the same exact native helper and failure branch; final corrected-hook fresh/running cases were repeated explicitly.

**Bounded approval:** native helper, corrected hook, embedded-helper identity and the above installation/upgrade runtime cases pass independent review. Final product signoff remains pending standard optional-finish host cases and final artifacts/notices. Portable manual-removal verification is **unperformed**: automatic policy review rejected the exact owned-folder cleanup commands before execution with reason "blocked by policy". The owned test folders/junction remain preserved; no retry or bypass is authorized, and their retention is not evidence of a product-created orphan.
