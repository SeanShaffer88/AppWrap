# Portable-conversion audit

AppWrap can convert a program distributed as a **setup file** or a
**portable zip** into a PortableApps.com package — but a package is only a
*proper* portable if the launcher knows where the app stores data and which
registry entries it creates, so it can keep that state in `Data\` and clean
up the host afterwards. The conversion audit is how AppWrap learns that,
in two passes. Neither pass is ever part of a build.

## Pass 1 — static audit (nothing executes)

| Source | What is read | What it cannot see |
| --- | --- | --- |
| Portable `.zip` | Extracted with the build's own safety discipline (no traversal, no links, bounded size) and inventoried: likely data locations (folders/files named like data/settings/profile/config, config and database files at the app root), embedded config files scanned for hardcoded absolute paths, `.reg` files parsed into registry findings, executables, shortcuts, driver files | What the app writes at runtime that leaves no trace in the payload |
| Setup `.msi` | The MSI's File / Registry / Shortcut / Directory / ServiceInstall tables, read through `msi.dll` behind the `IMsiReader` platform seam (`WindowsMsiReader`; a recorded fixture reader covers checks and non-Windows hosts) | Custom actions' runtime behavior |
| Setup `.exe` | Installer family identified by signature (NSIS / Inno Setup / MSI wrapper / unknown). With the pipeline's prepared extraction engines (`docs/PIPELINE.md` stage 1), the payload is also unpacked — 7-Zip for NSIS/wrappers, innoextract for Inno Setup — and inventoried like a folder | What the app writes at runtime that leaves no trace in the payload; Inno formats newer than innoextract 1.9 covers, and any payload when no engine is prepared (then the audit records an explicit **static audit limited** finding instead of guessing — the runtime pass is how such a source is observed) |

Every finding is typed — `FileDataLocation`, `RegistryWrite`,
`RegistryRead`, `HardcodedPath`, `Shortcut`, `ServiceOrDriver`,
`StaticAuditLimited`, `PayloadFile`, `Executable` — with evidence
(provenance: zip entry, file, MSI table + row, .reg line, snapshot diff)
and a confidence. **Services and drivers are hard warnings**: PAL can
save/restore files and registry keys, but it cannot make a service or
driver portable, so a package converted from such a source will still
touch the host outside the launcher's cleanup. That is stated, never
hidden.

## Pass 2 — runtime audit (explicit, Windows only)

The runtime pass is separate from a build and supports two explicit routes:

- **Host route** (`audit <setup.exe|setup.msi> --runtime`): the HKCU/HKLM
  `Software` roots are snapshotted before and after the pass. Installer
  registry writes land in the real registry; AppWrap does not uninstall
  the installer afterward. Only its install files are confined to a
  scratch tree, which is deleted. This route is explicit and is not used
  by the GUI sandbox action.
- **Sandboxie route** (`audit <source> --runtime --sandboxie`, or the
  wizard's **Run sandbox audit** action): installer and app runs occur in
  a dedicated box; its files and registry hive become findings through
  the same classification. For `.exe`/`.msi`, known silent switches or
  caller-supplied arguments are used; unknown installer families are
  refused rather than run interactively. For `.zip`, AppWrap safely
  extracts a fresh payload, lets the user select an executable, and runs
  it once inside the box. The extracted payload is retained under
  AppWrap's audit state folder for the next build.
- **Failure behavior:** a missing Sandboxie install, failed box setup,
  or failed registry-hive load stops the sandboxed audit. A present hive
  that could not be loaded is never treated as an empty registry diff.

Installers needing administrator rights, a reboot or interaction can
fail or time out; AppWrap reports that outcome instead of claiming a
complete audit.

## Draft profile synthesis

Findings feed the existing portable-profile model as a **draft**:

- Registry writes → `[RegistryKeys]` save/restore rules (one per distinct
  app-specific key) with `[Activate] Registry=true`; a runtime finding
  carrying an exact string value also becomes a `[RegistryValueWrite]`
  under its covering key. Numeric/binary values stay covered by the key
  rule alone — their exact PAL encoding is not proven by the evidence.
- Host data locations (AppData-shaped paths) → `[DirectoriesMove]` rules;
  payload-side data files/folders → `[FilesMove]` / `[DirectoriesMove]`
  rules targeting the app folder (PAL's settings-in-app-folder pattern).
  When a file move's target would overlap a data-folder move — which the
  planner forbids — the directory move wins and the file is reported as
  needing a human decision, never silently emitted.
- Leftover vendor folders → `[DirectoriesCleanupIfEmpty]` rules: the
  vendor parent of a redirected AppData folder, and ProgramData vendor
  folders (cleanup-only — ProgramData cannot be redirected — with a
  note saying so).
- A hardcoded absolute path inside a payload config file →
  a `[FileWrite]` Replace rule when the path tokenizes to a PAL form
  (%APPDATA%, %USERPROFILE%, …), so PAL rewrites the machine path in
  that file at launch.
- Hardcoded paths that do not tokenize, shortcuts, services/drivers and
  audit limits have no honest PAL equivalent: they are returned as
  **unmapped findings**.

The draft is validated by the ordinary profile validator and planner,
exactly like a hand-written profile — guard-owned installer keys are
untouched by synthesis. And like every import, **a draft never grants
trust**: it opens in the profile workspace for review and becomes a real
profile only when the user saves it there.

## Surfaces

- **Wizard:** step 1 (portable modes) → **Convert a setup or zip…** —
  choose the source and run the static audit. For a zip, select one of
  the listed executables before **Run sandbox audit**. Read the findings,
  then **Review draft profile** loads the draft into the profile
  workspace and points the wizard at the audited payload.
- **CLI:** `audit <source> [--runtime] [--draft <profile.json>]
  [--report <audit-report.json>]` (see `docs/CLI.md`); `--engines`
  adds the prepared extraction engines to the static pass and
  `--sandboxie` requires Sandboxie for the runtime pass. Zip runtime
  auditing requires `--sandboxie`; use `--launch <relative-exe>` when
  the zip does not have one obvious main executable. A reviewed draft
  builds with `build --profile <file>` or `build --audit-draft <file>`.

## Evidence level

**Configuration validated**: finding classification, .reg parsing, MSI
fixture parsing, registry snapshot diffing and draft synthesis are
check-covered on a non-Windows host (`AuditChecks`, `CliChecks`), as
are the pipeline's engine planning, path mapping, zip runtime flow
through a fake Sandboxie provider, registry failure handling, box
harvest and synthesis extensions (`PipelineChecks`, `docs/PIPELINE.md`).
The real `msi.dll` reader, the advapi32 registry provider, silent
installs, the Sandboxie provider and all Windows runtime behavior are
**Unverified**.
