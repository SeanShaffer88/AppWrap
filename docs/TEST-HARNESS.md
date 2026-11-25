# Runtime test harness

The test harness answers the one question packaging cannot: when the packaged
app actually runs, does it keep its writes inside its own package? It is a
**separate, explicit user action** — the wizard's **Test package…** panel,
offered after a portable or PAF Package build. Builds never execute a packaged
app, no build path references the harness, and there is no setting that makes
a build run one. Testing needs no administrator rights.

## How a run works

1. **Copy.** The chosen package folder (a built PAF Package folder, or a
   portable app folder extracted from a package) is copied into a throwaway
   tree under the system temp folder, using the build's own bounded snapshot
   copy — the same 50,000-entry / 40-folder / 20 GB limits as a build, with the
   root `appwrap.project.json` likewise excluded. The original is never run
   in place and never modified.
2. **Sandbox.** Beside the copy, the harness creates a sandbox profile
   (`AppData/Roaming`, `AppData/Local`, `Documents`) and a sandbox temp folder.
   If the package copy has no `Data/` folder, an empty one is created, per PAF
   convention.
3. **Launch.** The app's root launcher is resolved from the package's
   `appinfo.ini` `[Control] Start` (falling back to the folder's only root
   `.exe`) and started directly — no shell — with its working directory at the
   package root and these environment variables redirected into the sandbox:
   `USERPROFILE`, `APPDATA`, `LOCALAPPDATA`, `TEMP`, `TMP` (plus `HOMEDRIVE` /
   `HOMEPATH` derived from the profile path). No arguments are passed.
4. **Watch.** The package copy, the sandbox profile and the sandbox temp
   folder are snapshotted (file list + per-file SHA-256) immediately before
   launch and again after the process exits. The diff is the detected writes.
   Snapshot diffing was chosen over a file-system watcher for determinism:
   the same run always produces the same report.
4b. **Watch the registry.** When a registry provider takes part (the
   wizard's Test panel always passes one; the CLI's `test --registry`
   opts in), the `HKCU\Software` and `HKLM\Software` roots are snapshotted
   (keys and values, read-only through advapi32) before launch and after
   exit, and the diff joins the report as `registryChanges`. The registry
   cannot be redirected into the sandbox the way the profile folders are —
   it is the host's real registry — which is precisely why a write there
   is meaningful evidence: PAL's own backup area
   (`HKCU\Software\PortableApps.com\…`) is noted, any other HKCU write is
   **host leakage**, and an HKLM write is **machine-wide**; both are
   violations and fail the verdict. The conversion audit's runtime pass
   (`docs/AUDIT.md`) reuses this same observation around a vendor install.
5. **Classify and report.** Each detected change is classified (below), a
   verdict is computed, and `test-report.json` is written outside the sandbox
   (from the wizard: under AppWrap's `Data/TestReports` state folder). The
   throwaway tree is then deleted — in **all** exit paths: success, failure,
   timeout, crash and cancellation. A cleanup failure is reported in the
   result, never hidden.
6. **Timeout.** A run is limited to 60 seconds by default. On timeout the
   harness first asks the app's window to close, waits briefly, then
   terminates the whole process tree. The report records `timedOut: true`
   and no exit code.

Modes are **Portable** and **PAF Package** — the distinction is recorded in
the report; the mechanics are identical, because both test a package folder.
Testing a standard installer is intentionally out of scope: it would mean
executing an installer, and AppWrap does not execute installers. Note that a
PAF Package folder only has a launchable root file when it was packaged with
a compiled launcher; a folder the official PortableApps.com Installer has not
built yet (and that carries no launcher of its own) is refused with that
explanation before anything is launched.

## Classification and verdict

| Where the change landed | Category | Violation |
| --- | --- | --- |
| Package copy, under `Data/` | Portable data | No — the expected portable write |
| Package copy, anywhere else | Package root | No — noted; the write stays inside the portable root |
| Sandbox profile (any file) | Profile leakage | **Yes** — a portable app must not write to the profile |
| Sandbox temp | Temporary | No — scratch space is what temp is for |

The watched roots are exactly those three folders, so every observable change
falls into one row and nothing outside them can be observed at all.

**Verdict is Pass only when** the app exited on its own with exit code 0,
the run did not time out, and no violation was detected. A crash (non-zero
exit), a killed process, any profile leakage, or — when the registry was
observed — any registry violation is a Fail.

## `test-report.json` schema (version 1)

JSON with camelCase fields; enum values are strings.

| Field | Meaning |
| --- | --- |
| `schemaVersion` | Report schema version (currently `1`) |
| `appName` | The app name given for the run |
| `mode` | `Portable` or `PafPackage` |
| `launchCommand` | The exact command line launched (the sandbox copy's launcher, quoted) |
| `startedUtc` | Run start, ISO 8601 UTC |
| `durationSeconds` | Wall-clock duration of the launch |
| `exitCode` | The app's exit code, or `null` when it was killed / never exited |
| `timedOut` | Whether the run hit the time limit |
| `detectedWrites` | All detected changes: `root` (`Package` / `Profile` / `Temp`), `path` (relative to that root, forward slashes), `change` (`Created` / `Modified` / `Deleted`), `category`, `violation` |
| `violations` | The subset of `detectedWrites` with `violation: true` |
| `registryObserved` | Whether a registry provider took part in this run |
| `registryChanges` | Detected registry changes: `keyPath`, `valueName`, `change` (`KeyCreated` / `KeyDeleted` / `ValueCreated` / `ValueModified` / `ValueDeleted`), `category` (`LauncherBackup` / `HostLeakage` / `MachineWide`), `violation`, `newData`, `valueKind` |
| `registryViolations` | The subset of `registryChanges` with `violation: true` |
| `verdict` | `Pass` or `Fail` |

Paths in the report are relative to their watched root so the report stays
meaningful after the sandbox it describes has been deleted.

## Limitations

- **Coarse, snapshot-based detection.** Only the net difference between the
  before and after snapshots is seen. A file created and deleted again during
  the run leaves no trace; a file rewritten with identical content is not a
  change; a write that happens and is reverted is invisible. There is also an
  inherent race at the boundaries: a process that outlives the main process
  (a detached helper that keeps writing after the launcher exits) can write
  after the after-snapshot, or between exit and snapshot.
- **Registry observation is snapshot-based, like the file watching.**
  There is no registry virtualization and no continuous monitoring: only
  the net difference between the before/after snapshots of the
  `HKCU\Software` and `HKLM\Software` roots is seen (a value written and
  restored inside the run leaves no trace; writes under other roots are
  not observed), and the observed registry is the host's real registry —
  a test run's registry writes are real writes, which the report makes
  visible but AppWrap does not roll back. Treat a Pass as "no file or
  registry evidence against portability in the watched roots", not as
  proof.
- **Redirection is by environment variable.** Apps that resolve profile
  locations through the environment are redirected; an app that hard-codes
  absolute profile paths, or resolves them through APIs that ignore the
  environment, writes to the *real* profile — outside every watched root —
  and those writes are not detected.
- **One launch, no interaction.** The app is started with no arguments and
  left alone until it exits or the limit hits. Apps that need interaction to
  reach the code that writes settings may show a clean report simply because
  that code never ran. An app that runs forever (a tray app) will always end
  as a timeout Fail.
- **No clean-machine guarantee.** The sandbox shares the host's installed
  runtimes, fonts, codecs and machine-wide state. A Pass does not mean the
  package works on a clean machine, under a different user, or at all beyond
  the single launch performed.
- **The tested copy is bounded like a build.** Packages beyond the snapshot
  limits (50,000 entries, 40 nested folders, 20 GB) cannot be tested.
