# AppWrap roadmap

What is implemented, what is planned, and what is intentionally out of scope.
Evidence levels use the vocabulary of
`docs/portable-profiles-qualification.md`: **Verified** (an automated check
ran and passed in the named environment), **Configuration validated** (model,
validation and build-boundary logic implemented and check-covered on a
non-Windows host; no Windows compiler or runtime involved), **Unverified**
(not exercised in the available environment).

## Implemented

### Portable-profiles roadmap (P0–P6)

Typed portable profiles over a lossless INI document model: capability
catalog and strict validator (P0), the real PAL path with the pinned,
hash-verified PAL 2.2.9 / NSIS 2.46.5-Unicode backend (P1), typed launcher
rule views (P2), the immutable operation planner (P3), the installer surface
with guard-safe merge and guard-order-blocked families (P4), the WPF
portable-profile workspace in the wizard (P5), and the generated
corresponding-source/license bundle carried by PAL packages (P6).
Evidence: **Configuration validated** throughout; backend preparation
**Verified** against the pinned hashes; PAL compilation, runtime behavior
and WPF layout on Windows **Unverified** (full record:
`docs/portable-profiles-qualification.md`).

### Development Suite expansion (Phases 1–6)

- **Phase 1 — PAF project model.** `appwrap.project.json` (schema v3,
  forward-only migration chain v1→v2→v3, unknown fields preserved), the
  bounded source snapshot hash, and `PafValidator` with field-level
  diagnostics. The wizard loads or infers a project for every chosen folder;
  the project file is excluded from every package payload.
  **Configuration validated** (`docs/project-model.md`).
- **Phase 2 — Configuration surface.** Guided editors for a project's
  appinfo, launcher and installer documents over the lossless INI model,
  inline `PafValidator` diagnostics, and the off-by-default, per-project
  Advanced mode that alone unlocks direct editing of the project's own
  `Custom.nsh` / `PortableApps.comInstallerCustom.nsh`. No packaging path
  reads the flag. **Configuration validated**.
- **Phase 3 — PAF Package output.** A fourth output mode producing the
  uncompiled package folder for the official PortableApps.com Installer,
  alongside the compiled AppWrap Installer default; the two share staging
  and their generated documents are byte-identical. **Configuration
  validated**; compilation of the folder by the official installer
  **Unverified** (`docs/PAF-COMPLIANCE.md`).
- **Phase 4 — Runtime test harness.** An explicit, separate **Test
  package…** action: runs a finished package once in a throwaway sandbox
  with a redirected profile, diffs file snapshots, and writes
  `test-report.json` flagging profile writes as portability violations.
  Builds never execute a packaged app. **Configuration validated** for the
  report/violation logic; real-app runs on Windows **Unverified**
  (`docs/TEST-HARNESS.md`).
- **Phase 5 — Project templates.** **New project from template…**: a Blank
  PAF app scaffold that passes `PafValidator` with TODO warnings only, and
  the Wrapped binary template (the existing choose-a-folder flow with the
  origin recorded). Scaffolding writes project files only.
  **Configuration validated**.
- **Phase 6 — Compliance documentation.** `docs/PAF-COMPLIANCE.md`
  finalized against the shipped surfaces, this roadmap, and
  `ComplianceChecks`, which pins the compliance matrices to the generator
  output, the editor field catalogue and the capability catalog so a
  supported field cannot regress silently.

### Command-line interface

`AppWrap.Cli` exposes every wizard and Development Suite capability as
commands — `build` (all four output modes), `project validate|info|create`,
`config list|get|set`, `profile validate`, `test`, and `gui` — thin over the
same Core/Build operations, with a `--json` envelope (result plus structured
diagnostics), `--dry-run` builds, and a 0/2/3/4 exit-code contract, so
scripts and local AI operators can drive AppWrap without the GUI. It ships
beside `AppWrap.exe` in the same distribution folder and resolves backends
the same way. **Configuration validated**: argument parsing, the JSON
envelope, config round-trips, profile validation and dry-run builds are
check-covered and smoke-run on a non-Windows host; real packaging runs on
Windows **Unverified** (full reference: `docs/CLI.md`).

**Dual-mode executable (v0.2.3, pending release).** The CLI is now a
library hosted in-process by `AppWrap.exe`: one self-contained executable
is both the wizard and the CLI, so a release carries a single .NET runtime
instead of two. `CliModeDetector` routes the command line conservatively —
no arguments or an unrecognized first argument opens the wizard; a command
name or global flag runs the CLI — and the shipped `AppWrap.Cli.exe` is a
tiny native shim (`native/AppWrapCliShim.c`) that forwards to
`AppWrap.exe --cli …` and propagates the exit code, keeping the two entry
points interchangeable. The router's `gui` command, when hosted, reports
that the GUI is this executable and exits 0. **Configuration validated**:
the mode detector is check-covered for both sides of the routing line;
Windows runtime **Unverified**.

### Project image properties

The launcher icon and the sidebar image are first-class project properties
(project schema v3: `iconPath` / `sidebarImagePath` in
`appwrap.project.json`, stored project-relative when the file lives in the
project and absolute when it does not), not per-build picks that vanish when
the wizard closes. Reopening a project restores the wizard's icon/sidebar
steps from them; a build records the effective picks back, the same way the
filename style is recorded. The configuration editors expose both on a
Project tab, `PafValidator` checks them field-by-field (missing file,
escaping path, unsupported picture type, the imaging layer's 32 MB bound),
the CLI reaches them through `config … project Images …`, `project info`
reports them, and `build` resolves flag > project > none. Inference picks
up the conventional `App/AppInfo/appicon.ico` only; the sidebar has no
conventional in-package location and is never inferred. The sidebar applies
to the compiled portable output only: the official PortableApps.com
Installer has no package-side sidebar input (its wizard deletes any
package-side copy and compiles with its own installation's bitmap), so a
PAF Package folder never carries one (`docs/PAF-COMPLIANCE.md`).
**Configuration validated**: model round-trip, the v2→v3 migration,
validator diagnostics, editor surface and CLI precedence are check-covered;
WPF interaction on Windows **Unverified**.

### Portable-conversion audit

Converting a setup file or portable zip into a *proper* portable — one
whose launcher redirects the app's data and cleans up its registry
footprint — starts with an audit, in two passes (`docs/AUDIT.md`). The
**static pass** executes nothing: a zip is safely extracted and
inventoried (data-location heuristics, embedded-config hardcoded paths,
`.reg` files parsed into registry findings), an MSI's File/Registry/
Shortcut/Directory/ServiceInstall tables are read through `msi.dll`
behind the `IMsiReader` seam (fixture reader for checks and non-Windows
hosts), and an installer .exe is identified by family with its payload
recorded as an explicit audit limit rather than a guess. The **runtime
pass** extends the test harness with registry observation — HKCU/HKLM
`Software` snapshot diffs in the same report shape, classified as
launcher-backup / host leakage / machine-wide — and, for a conversion,
silently installs the source into a scratch tree, launches the installed
app once, and merges the observed registry writes into the report.
Findings synthesize a **draft portable profile** over the existing model
(`[RegistryKeys]` save/restore, covered `[RegistryValueWrite]` for exact
string values, `[DirectoriesMove]`/`[FilesMove]` redirects; services,
drivers, hardcoded paths and audit limits stay unmapped and visible),
validated by the ordinary validator/planner and opened in the profile
workspace for review — imports never grant trust, and the draft builds
only after the user saves it. Wizard surface: **Convert a setup or
zip…** on step 1; CLI: `audit` plus `build --audit-draft`.
**Configuration validated** (classification, .reg/MSI-fixture parsing,
snapshot diffing, synthesis and the CLI surface are check-covered);
the real msi.dll reader, the registry provider, silent installs and all
Windows runtime behavior **Unverified**.

### Four-stage conversion pipeline (Sandboxie)

The audit grew into the four-stage pipeline of `docs/PIPELINE.md`.
**Stage 0 (bootstrap):** AppWrap Setup checks for Sandboxie-Plus and —
per SEAN's explicit 2026-11-25 decision, a scoped override of the
standing zero-cloud / no-silent-elevation rules for this feature only —
silently downloads the pinned release (1.18.8, hash-compared against
the lock; the hash itself is recorded Unverified because the project
publishes none AppWrap can cite) and installs it with its driver when
missing or outdated; any failure leaves AppWrap working, is stated
plainly in the install log and on the final page, and the profiler
falls back to host observation. **Stage 1 (ingestion):** 7-Zip,
innoextract and lessMSI are pinned optional backends
(`tools/Prepare-EngineBackend.ps1`, `third_party/engines`), wired into
the static audit behind the engine seam — NSIS/Inno/MSI payloads are
unpacked and inventoried; a missing or failing engine degrades to the
stated audit limit. **Stage 2 (sandboxed profiling):** the runtime
pass can run the vendor installer and app inside a dedicated
Sandboxie box and harvest box files (mapped to PAL paths) plus the
box registry hive into the same findings; without Sandboxie it falls
back to the host route and the result names the backend.
**Stage 3 (synthesis):** drafts gained leftover-vendor-folder
`[DirectoriesCleanupIfEmpty]` rules, `[FileWrite]` Replace rules for
tokenizable hardcoded paths in payload config files, and a composited
branding splash (icon + app name) when no sidebar image is supplied.
**Stage 4 (leak verification):** the test harness runs the packaged
app in a clean Sandboxie box (`test --sandboxie`); the verdict then
certifies zero writes escaped the box, and the report records the
observation backend. **Configuration validated** (`PipelineChecks`:
locks, bootstrap decisions, engine planning, path mapping, box config
and harvest, synthesis extensions, fake-sandbox verdicts); every
Windows runtime element — the bootstrap's download/install, Start.exe
driving, hive loading, real engine processes, splash rasterization —
**Unverified**.

### Backend provenance pin

The stale `modifiedWizardSha256` pin in
`third_party/portableapps/provenance.json` was refreshed from a real
PowerShell run of the committed `tools/Prepare-PortableBackend.ps1` (the
script deterministically produces the pinned value from the verified
official Installer 3.9.24 archive), and v0.2.0-preview shipped with the
backend passing its provenance check.

## Planned

- **The `AppWrapPreMutation` gate.** The DownloadFiles, CopyLocalFiles and
  LegacyVersions installer families and plugin/common-files mode are typed,
  discoverable and configuration-checked, but blocked
  (`GuardOrderBlocked`) because they can act before the upgrade guard's
  safe point in the install sequence. Implementing and qualifying the
  reviewed earlier-safe gate in the generated installer (contract:
  `docs/portable-profiles-contract.md`) is the prerequisite for enabling
  any of them; until then they stay blocked, visibly, rather than being
  silently dropped.
- **Host runtime fixture qualification.** The portable-profiles
  qualification record lists what only a Windows host can prove: an actual
  PAL compile and a real source bundle inside a package, the runtime
  fixture suite (moves, registry save/restore, cleanup, recovery), guard
  variants and PAL cleanup waits with live processes, WPF layout at
  680×560, and the test harness against real packaged applications.

## Intentionally out of scope

- **Distribution through package managers.** AppWrap ships as a direct
  download ZIP only — no winget, Scoop or Chocolatey packages.
- **Executing source-supplied actions during a build.** Builds never run
  a source folder's installer, download actions or custom hooks; a
  payload carrying custom build scripts is rejected, and the blocked
  installer families above stay blocked until their gate exists. The
  only executions in the product are the two explicit, separate user
  actions that exist to observe behavior: the test harness's single
  launch of a finished package, and the conversion audit's runtime pass
  (a silent install into a scratch tree plus one launch of the installed
  app) — neither is reachable from any build path.
- **Registry virtualization in the test harness.** The harness observes
  the registry by before/after snapshot diffing of the HKCU/HKLM
  `Software` roots; it does not virtualize or continuously monitor the
  registry and makes no clean-machine guarantee
  (`docs/TEST-HARNESS.md`).
- **Certifying portability or redistribution rights.** The conversion
  audit drafts a profile that redirects an app's data and registry
  writes, but AppWrap does not certify that the result is fully portable
  (findings it could not map stay visible as unmapped), or grant rights
  to redistribute a packaged app; unknown license information stays
  unknown/conservative.
- **Replacing the PortableApps.com Platform.** AppWrap produces packages;
  it does not distribute, install, update or run portable apps for end
  users (`docs/PAF-COMPLIANCE.md`).
