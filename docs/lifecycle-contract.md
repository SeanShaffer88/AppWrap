# Bounded local installer lifecycle qualification

User explicitly authorized executing generated setup and portable packages locally, including install, upgrade, uninstall and targeted fixes. This plan bounds that authorization to newly created owned fixtures, their unique per-user installation identity and workspace portable destinations. Controller approves the plan before execution. No elevation, machine-wide settings, driver/service changes, fake platform process or unrelated application closure.

## Existing evidence and intake

Checkout reported by controller: main `5bd5926`, clean at intake; recheck status before changes and preserve concurrent work. Prior `work/AppWrap-ui-e2e-paired` packages are compilation/UI evidence, not trusted runnable app fixtures. Auditor established prior Demo.exe was copied `Environment.ProcessPath` of the test harness (TestsProgram.cs line31) and companion.dll is inert text. Do not execute those packages/payloads as benign lifecycle apps. Record hashes and this exclusion reason; replace them with a purpose-built fixture.

Activated `C:\Users\Sean Shaffer\.agents\skills\verify-and-stop\SKILL.md`: smallest sufficient proof set, reuse matching results, report pass/fail/unavailable accurately, no unrelated polish. Toolkit catalog and selected README sections govern execution; selected helper help was read for Invoke-DevBuild, Get-ProjectInfo and Test-WindowsUi. Execution agent reads any further helper help before first use. Native direct CLI/process/UI fallback is appropriate where a helper cannot drive installer windows. `Test-WindowsUi` targets exact owned PID with `-AllowActions` and does not certify accessibility.

## Trusted fixture and ownership

Create a new unique `AppWrapLifecycle-<runid>` product name, stable across v1/v2, distinct from all real products. Use a new workspace `work/lifecycle-<runid>` tree for source, evidence, two published fixture versions, built artifacts and portable install root. Obtain actual source/toolchain/build exit/hashes before execution. Prefer a small native Win32 fixture if an installed verified compiler is available; otherwise installed .NET10 WPF/WinForms self-contained win-x64 is acceptable. No new SDK/runtime install required. Keep source and build recipe reviewable and independently audited.

Fixture requirements: v1/v2 have distinct file/product version and executable hashes; same stable executable/product identity. A real window/message loop supports normal close so Restart Manager/running-app behavior can be observed. A bounded hold mode keeps the executable running long enough for upgrade; automatic maximum lifetime prevents forgotten process. Accept probe arguments with spaces/quotes and a requested exit code. Write a bounded JSON/text evidence record containing version, PID/start time, argv, cwd and normal close reason, plus an owned settings marker. All fixture writes are restricted to the compiled unique test root or explicitly owned installed fixture/Data paths; no network, registry writes, global hooks or arbitrary command execution. Flush/dispose resources and use an exit path that can close held executable/read handles normally. Timed fixture heartbeat is test instrumentation, not product monitoring.

Build both v1/v2 packages through the actual AppWrap public Build API (prefer paired API to exercise it). Inputs include nested selected executable, sibling dependency/data asset and a v1-only harmless obsolete asset. Verify actual package hashes and provenance. For standard shortcut tests, turn on both supported shortcuts in this fixture build; separately validate blueprint default Start Menu on/Desktop off and postinstall Open selected behavior in generated script/UI. For portable settings, fixture deliberately targets package-root `Data`; this validates that specific fixture contract, not arbitrary-app portabilization.

Before any install inspect ONLY exact expected targets:

- Default standard path `%LOCALAPPDATA%\Programs\<InputRules.SafeId(unique-name)>`.
- Exact HKCU uninstall entry derived from generated AppId (`AppWrap-<safeid>_is1`) in relevant registry view; read actual script/manifest rather than guess a broader registry search.
- Exact current-user Desktop and Start Menu `.lnk` files named for the unique fixture.
- New portable parent and package AppID path in workspace; fixture settings/evidence paths.

Require expected destinations/registry/shortcuts absent. If any unexpected preexisting object exists, choose a fresh run identity instead of deleting or replacing it. Save a scoped baseline/evidence manifest before installation. Do not enumerate home/authentication directories, all registry hives or all user processes. Process operations must use owned PID+start time/path, never generic executable-name kill.

## Verified command interfaces

Current official Inno7 documentation confirms standard setup invocation with separate argv:

`<v1-setup.exe> /VERYSILENT /SUPPRESSMSGBOXES /SP- /NORESTART /LOG=<unique-owned-install-log>`

Use the actual default per-user installation path first; avoid `/DIR` override that would bypass the default-path gate. `/DIR=<explicit-owned-absolute-path>` is supported for a separate relocation test if needed. `/CLOSEAPPLICATIONS` and `/LOGCLOSEAPPLICATIONS` are supported, but first observe generated `CloseApplications=yes` defaults, then use diagnostics flags only to diagnose/fix a failure. Never force close unrelated processes; `/FORCECLOSEAPPLICATIONS` is not the initial test. Silent setup's `[Run] skipifsilent` prevents automatic probe launch; launch the installed owned probe explicitly.

Uninstaller:

`<exact-installed-unins000.exe> /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG=<unique-owned-uninstall-log>`

Read the exact owned uninstall executable path from installed evidence; do not execute a registry command string through a shell. Always `/NORESTART`, including very-silent operations, because default very-silent behavior can otherwise reboot. Capture process exit code and wait boundedly; inspect actual artifacts instead of treating exit0 alone as lifecycle success.

Pinned PortableApps 3.9.24 generated installer source `App/installer/PortableApps.comInstaller.nsi` `.onInit` calls `SetSilent normal` (line440); generic NSIS `/S` is **not a standalone silent-install contract** here. It supports `/DESTINATION=<parent-with-trailing-backslash>` and directly appends AppID to that value (line515 onward). The `/SILENT=true`, `/AUTOCLOSE=true`, `/HIDEINSTALLER=true` automation path requires detection of a genuine running PortableApps.com Platform with expected metadata; do not fabricate one or install a platform solely to automate tests.

Run the owned PAF installer interactively with `/DESTINATION=<owned-portable-parent>\`, driving only its exact process/window through documented UI automation. Confirm displayed destination stays within the owned parent before Install. Handle the actual language/welcome/destination/finish flow; screenshot evidence and process exit code required. Do not rewrite the produced installer to create a silent shortcut. Source also supports `/LOG=true` but inspect exact log location/version before relying on it; a workspace before/after file inventory remains authoritative.

## Standard lifecycle sequence

1. Install v1 into its default unique per-user destination. Verify exe/resource hashes, version, exact HKCU uninstall metadata, and both owned shortcut targets/arguments/working directories. Launch installed probe via direct process and at least one shortcut; verify cwd/argv and requested exit code. Running through a shortcut does not need shell composition of user input.
2. Write harmless user settings marker through fixture; create a separately recognizable untracked user file under owned install tree to test preservation. Record both hashes. Launch v1 in bounded hold mode and capture exact PID/start/path, then upgrade using v2 setup while v1 is running. Observe normal owned process closure, successful replacement and no restart when generated RestartApplications=no. On timeout stop only still-owned fixture/installer after capturing diagnostics; never infer live-upgrade success from a later manual retry.
3. Verify v2 executable/resources/version, unchanged settings markers, stable install path/AppId and single uninstall registration, shortcut validity and launch behavior. Record actual v1-only asset semantics. Inno `[Files]` upgrades do not automatically remove arbitrary obsolete files; a remaining known v1 asset is an observed limitation requiring a narrow documented fix if needed, not permission to recursively wipe install data.
4. Close probe normally, uninstall through exact installed uninstaller. Verify installed tracked payload, owned shortcuts and uninstall registration removed. Verify untracked/user settings marker preserved. Remaining directory containing preserved user files is expected, not an orphan-cleanup failure. Copy retained marker evidence, then remove only owned test fixture paths if cleanup is desired; never present manual cleanup as uninstaller behavior.
5. If relevant defaults need GUI proof, run a separate fresh owned identity interactively and observe desktop/Open selections before completing; do not claim defaults from a silent install that skipped those pages. No Windows-startup setting is applicable to this probe.

## Portable lifecycle sequence

1. GUI-install v1 under the exact new workspace parent. Compare all payload hashes against snapshot (root shim, nested exe, root DLL/resource now under App/Payload), metadata and icons/sidebar assets as inspectable. Confirm no new Windows uninstall entry or shortcuts, according to scoped baseline.
2. Launch root shim with arguments containing spaces/quotes and explicit exit code. Verify actual payload cwd, raw forwarding result and returned child exit. Fixture writes its marker in package-root Data; preserve marker hash and a harmless Data file.
3. Launch v1 shim/payload hold mode and upgrade with v2 PAF installer. Observe actual running-app detection/closure. The current shim waits for payload, so root shim and child may both be alive; ensure updater does not terminate only shim and then overwrite/delete still-running payload. A blocked or locked upgrade is a reproducible bug to fix at a narrow boundary, not a pass. Stop only exact owned PIDs if recovery is required.
4. Verify v2 content/version, old executable/resources replaced, package-root Data marker unchanged, obsolete App content semantics, and repeated root-shim cwd/argv/exit. Test relocation by copying the closed owned package to another new workspace path with spaces, then launch and verify specific fixture Data behavior; do not claim different-drive support unless tested.
5. Portable packages have no Windows uninstaller. After all owned processes close, removal means deleting the owned installed package directory, with marker evidence copied first. Verify exact directory removal and no scoped registry/shortcut delta. Do not call this an uninstaller test. Record any host state the official installer/probe actually left; do not assert a global zero-footprint claim from scoped checks.

## Failure/fix/retest gates and final evidence

Installer/setup/probe processes use finite timeouts, captured exits and exact owned identities. Any unexpected destination/elevation request/preexisting target stops dependent action; preserve evidence, choose new identity or target the smallest fix. Fix only reproduced product defects. Rebuild affected packages, refresh hashes, rerun failing lifecycle plus directly affected regression gates, and obtain independent Auditor signoff on final source and evidence. Do not reuse prior artifact success for rebuilt bytes.

Write one machine-readable result manifest with source revision, fixture source/build commands and exits, input/generated artifact hashes, baseline paths/registry identities, installer/probe command argv and exit codes, installed hashes, shortcut metadata, v1/v2 process closure evidence, marker preservation, uninstall/removal results, cleanup/recovery leftovers, failed/unperformed checks and actual host limits. UI tool lacks exit code where applicable: record null with reason, never invent0. Keep installer logs and screenshot paths absolute under this run; outputs final user-facing report should link only outputs deliverables.

Acceptance: fresh standard install, actual running upgrade, data preservation, launch/shortcut checks and standard uninstall all pass for final package bytes; fresh portable install, actual running upgrade, Data preservation, shim/relocation checks and owned removal pass. Any failed gate remains incomplete until fixed/retested. No general certification for arbitrary third-party payloads, every Windows version, clean machines, mixed DPI or all host locations is implied.

## Primary references

- https://jrsoftware.org/inno-setup/documentation/topic_setupcmdline.htm — current setup switches and reboot/closing behavior (retrieved 2026-11-21).
- https://jrsoftware.org/inno-setup/documentation/topic_uninstcmdline.htm — current uninstall switches (retrieved 2026-11-21).
- Hash-pinned 3.9.24 official source in `work/backend-research/extracted/App/installer/PortableApps.comInstaller.nsi` — actual standalone destination/silent/platform behavior; actual installed source takes precedence over generic NSIS recollection.
