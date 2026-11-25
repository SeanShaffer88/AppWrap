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

- **Phase 1 — PAF project model.** `appwrap.project.json` (schema v2,
  forward-only migration, unknown fields preserved), the bounded source
  snapshot hash, and `PafValidator` with field-level diagnostics. The wizard
  loads or infers a project for every chosen folder; the project file is
  excluded from every package payload. **Configuration validated**
  (`docs/project-model.md`).
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
- **PortableApps backend provenance-pin refresh (repository maintenance).**
  The `modifiedWizardSha256` pinned in
  `third_party/portableapps/provenance.json` does not match what the
  committed `tools/Prepare-PortableBackend.ps1` produces from the verified
  official Installer 3.9.24 archive: the archive hash matches its pin and
  the extracted wizard source is provably the upstream source, but faithful
  runs of the committed script (real PowerShell, both line-ending
  variants) yield different modified-wizard hashes than the pin. The
  backend must be re-prepared by a real run of the script and the pin (and
  the regenerated `docs/portable-wizard.patch`) updated from that run
  before the next release is assembled; no release ships a backend that
  fails its own provenance check.
- **Host runtime fixture qualification.** The portable-profiles
  qualification record lists what only a Windows host can prove: an actual
  PAL compile and a real source bundle inside a package, the runtime
  fixture suite (moves, registry save/restore, cleanup, recovery), guard
  variants and PAL cleanup waits with live processes, WPF layout at
  680×560, and the test harness against real packaged applications.

## Intentionally out of scope

- **Distribution through package managers.** AppWrap ships as a direct
  download ZIP only — no winget, Scoop or Chocolatey packages.
- **Executing source installers or source-supplied actions.** AppWrap
  never runs a source folder's installer, download actions or custom
  hooks; a payload carrying custom build scripts is rejected, and the
  blocked installer families above stay blocked until their gate exists.
- **Registry observation in the test harness.** Detection is file-system
  snapshot diffing only; the harness does not virtualize or monitor the
  registry and makes no clean-machine guarantee
  (`docs/TEST-HARNESS.md`).
- **Certifying portability or redistribution rights.** AppWrap does not
  redirect an app's registry/profile writes, certify that an app is
  portable, or grant rights to redistribute a packaged app; unknown
  license information stays unknown/conservative.
- **Replacing the PortableApps.com Platform.** AppWrap produces packages;
  it does not distribute, install, update or run portable apps for end
  users (`docs/PAF-COMPLIANCE.md`).
