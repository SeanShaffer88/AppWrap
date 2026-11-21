# Independent lifecycle audit contract

Status: intake/provenance review only. User now authorizes local install/upgrade/uninstall of uniquely owned generated fixtures and targeted fixes after reproduction. Earlier compilation/static evidence did not qualify lifecycle behavior.

## Provenance gate before execution

The previous `AppWrap.Tests/Program.cs` copies `Environment.ProcessPath` into `sample/bin/Demo.exe`; this is the test executable's apphost, not a genuine standalone application. `companion.dll` is deliberately text. These are appropriate compile-only fixtures and must not be treated as a runnable lifecycle probe. Missing managed dependencies may fail launch; supplying them could run the regression harness with unrelated fixture effects.

Build a new genuine owned probe with complete source/build/hash evidence and distinguishable v1/v2 artifacts. Its public test behavior must explicitly cover selected arguments (including spaces/quotes), current working directory, exit code, bounded log/settings writes, and an owned hold/open-file mode for running-instance upgrade checks. No network, elevation, autorun, services, scheduled tasks or unrelated process actions. Independently inspect its source and exact executable hash before any run.

## Bounded ownership and absence gate

Record exact unique fixture app name, stable SafeId/AppId, install path, selected main executable, two versions, generated installer/package hashes, source roots, portable install root and owned process identity. Before execution assert absence of each intended host target. Abort on preexisting or ambiguous state; choose a new unique identity rather than overwriting it.

| Boundary | Scope to establish before action | Required evidence |
|---|---|---|
| Standard files | Exact per-user `LocalAppData/Programs/<SafeId>` or explicit same owned fixture override | Before absence; installed v1/v2 file hashes and declared files; no sibling directory cleanup |
| Uninstall registration | Exact AppWrap-derived HKCU uninstall key, relevant registry views only | Before absence; v1/v2 values and uninstall command identity; removed after uninstall |
| Shortcuts | Exact unique Start Menu/Desktop `.lnk` paths generated from fixture name | Before absence; target/working directory; selected/default states; removal after uninstall |
| Application data | New unshipped owned settings sentinel, with writer documented | Byte identity across upgrade and intended retention after uninstall, separated from orphan state |
| Portable package | New owned workspace destination and complete extracted package root | Actual PAF invocation/install result; v1/v2 payload identity; root launcher argv/cwd/exit; settings preservation |
| Processes | Explicit PID, start time, image path and fixture version for each launched probe/installer | Running-instance close outcome, bounded timeout, exact process termination; no name-based sweeping |
| Temporary/evidence | Declared fixture workspace, logs, package staging and recovery paths | Preserve evidence; distinguish cleaned app-owned staging from expected retained test artifacts |

## Lifecycle gates

1. Inspect current generated scripts and exact fixture package metadata before execution. No source custom hooks, source downloads or user-supplied scripts may run.
2. Install standard v1 as current user with actual executable result/log and explicit destination; confirm registration, files, shortcut targets and application launch.
3. Launch the exact installed probe to exercise cwd/argv/exit and create user settings. For hold mode, establish PID/path/start time before upgrading.
4. Upgrade with same AppId/name to v2; observe whether own running process is closed, v2 installed into same identity/path, settings remain byte-identical and no duplicate uninstall record or shortcut appears. Report timeout/reboot-required/locked-file outcomes honestly.
5. Uninstall through the exact installed uninstaller after validating its hash/path and uninstall command. Verify known installer files/shortcuts/registry removed and expected unshipped user data retained. Do not label intentional retained user data an orphan.
6. Actually install/upgrade the portable PAF in the selected owned workspace. Launch the genuine root shim and verify payload version, selected working directory, raw arguments and propagated exit. Exercise settings preservation and running-instance behavior only within declared scope.
7. Portable format does not imply a registered uninstaller. If no uninstaller exists, qualify removal as bounded manual portable-folder removal after owned processes stop, with before/after evidence; do not invent registration behavior.
8. Reproduce any observed defect before implementation. Target the responsible generator/launcher/boundary, rerun affected lifecycle checks and meaningful regression tests, and independently review final diff/evidence.
9. Before cleanup verify resolved targets remain below the explicit owned root with ownership evidence; preserve fixture/source/logs and user evidence. Never elevate, stop unrelated processes, delete registry ancestors or sweep global caches.

## Current source hypotheses, not runtime findings

- Standard script declares per-user install, stable name-derived identity, CloseApplications=yes, RestartApplications=no, postinstall launch and shortcut paths. Those declarations do not prove actual running-instance closure, silent behavior or lifecycle correctness.
- Standard uninstaller should preserve unknown unshipped user files; shipped same-name configuration can still be replaced or removed. Application-specific migration/isolation must not be inferred.
- Portable settings specify RemoveAppDirectory/OtherDirectory/DataDirectory=false. Actual update behavior must be observed.
- Root portable shim waits for payload and should preserve raw argv/cwd/exit. The upstream running check targets the root launcher; direct payload launch and automatic process closure are separate unqualified cases.
- Portable installer has optional download/global Internet Settings branches in its general source; safe generated metadata does not enable them. Unexercised source branches are not observed writes.

## Reporting limits

Separate observed host changes from declarations and inferences. A bounded before/after inventory cannot prove no writes elsewhere; ordinary Windows execution history/cache is not app-owned cleanup state. No clean-machine, mixed-DPI, exhaustive tracing, accessibility, signature trust or public-release licensing qualification is implied by this local lifecycle pass.

Skill grounding: app-state-footprint-auditor requires static ownership first, exact authorized live targets and observed/documented/inferred distinctions; code-review-and-quality requires independent review of final changes and evidence; security-and-hardening requires constrained paths, trusted execution and ownership before destructive operations.
