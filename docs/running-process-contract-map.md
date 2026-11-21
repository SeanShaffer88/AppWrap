# Running-process behavior: pinned source map

Read-only research for lifecycle qualification. This is evidence and possible boundary mapping, **not approval to implement a speculative fix**. Actual fixture reproduction determines whether a change is needed. Source is hash-pinned PortableApps Installer3.9.24 extracted under `work/backend-research/extracted`; no host UI/process action was taken for this research.

## PortableApps actual behavior

- `Other/Source/InstallerWizard.nsi` lines 811–814 reads optional `[CheckRunning] CloseEXE`, defaulting to the preceding `[Control] Start` value, and emits `CHECKRUNNING`; `CloseName` changes displayed name. The name CloseEXE does not mean an automatic close action exists.
- `App/installer/PortableApps.comInstaller.nsi` `.onInit` lines 858–867 checks `ProcessExists(CHECKRUNNING)` and loops an OK/Cancel running-warning message. OK retries; Cancel aborts. This occurs before welcome/directory pages and the installation Section.
- Directory precheck lines 1001–1005 displays running warning. `LeaveDirectory` lines 1030–1035 detects an existing destination and a running checked name, displays warning and aborts leaving the page.
- `App/installer/PortableApps.comInstallerProcFunc.nsh` `LLProcessExists_` lines 700–723 enumerates Toolhelp processes and compares `PROCESSENTRY32W.szExeFile` with the supplied name. It does not compare executable full path, installation directory, process parent or creation time. A same-basename launcher from another installation can therefore block this package as a name collision.
- The helper file defines CloseProcess and TerminateProcess, but the actual installer source never calls them for CHECKRUNNING. Stock behavior is warn/refuse/retry, not safely automatic shutdown. No automatic child-payload closure is established.
- Root AppWrap launch shim's ExecWait keeps its process alive while child payload runs. Thus a normally launched payload should keep the checked root name present; directly launched child without shim is not necessarily detected by stock name guard. Terminating the shim alone does not prove child termination and must never authorize overwriting a still-running payload.

## Hook ordering and boundaries

`PortableApps.comInstaller.nsi` includes source custom code around lines 417–422, but custom code **execution** `CustomCodePreInstall` occurs only at lines 1683–1684 inside the installation Section, after startup/directory running guards. It cannot resolve the initial blocked launcher by itself. `CustomCodeOptionalCleanup` and PostInstall are also not startup-close hooks. No documented installer.ini key in the pinned public format supplies a path-scoped pre-page shutdown operation.

`CloseEXE=NONE` bypasses existing name guard. It is not an acceptable standalone solution: any replacement must prove path-scoped owned launcher+payload closure before file mutation, preserve cancellation/failure and avoid unrelated same-name processes. Global process-name kill, forcing payload overwrite, or treating warning dismissal/manual fixture close as automatic-close proof are prohibited. These constraints describe safety requirements, not a chosen implementation.

If reproduced behavior violates the user's automatic-owned-instance-close requirement, controller/architect must approve the smallest evidence-grounded seam (potentially a minimal distinctly marked installer startup patch, or an independently audited outer operation with explicit target ownership) before implementation. Do not insert generic custom code, additional helpers or process tracking merely because those are possible. A change that removes stock checks must include tests for direct payload launch, multiple own instances, same-name foreign process, graceful-close timeout/cancel and no partial overwrite.

## Inno standard setup and uninstall

Current official `CloseApplications` documentation says setup uses Windows Restart Manager to detect files being updated, and `yes` prompts interactively or closes in silent mode. `force` can lose unsaved work and is not the chosen default. `RestartApplications=no` controls restart independently; it must remain no in the existing generator. This is file-resource detection, not broad process-name closure.

The directive's documented behavior concerns Setup. No verified documentation establishes that this alone automatically closes running applications during uninstall. `AppMutex` supports Setup/Uninstall startup refusal when an application creates the matching mutex, not generic automatic closure; AppWrap cannot assume an arbitrary payload creates that mutex. Current generator does not emit AppMutex or uninstall code. Observe running-app uninstall separately if requested; do not extrapolate successful setup upgrade to uninstall behavior or add a fake mutex contract.

Primary sources retrieved2026-11-21:

- https://jrsoftware.org/inno-setup/documentation/topic_setup_closeapplications.htm
- https://jrsoftware.org/inno-setup/documentation/topic_setup_restartapplications.htm
- https://jrsoftware.org/inno-setup/documentation/topic_setup_appmutex.htm
- https://jrsoftware.org/inno-setup/documentation/topic_uninstcmdline.htm

## Required result wording

Report actual observed state: blocked with warning; closed gracefully by installer; manually stopped for recovery; force-stopped exact owned PID; upgrade completed after manual closure; or failed/incomplete. Only the actual automatic closure path meets that requirement. A stock refusal can still prove a safe guard, while remaining a product limitation. Keep these findings separate from lifecycle test results until executed.
