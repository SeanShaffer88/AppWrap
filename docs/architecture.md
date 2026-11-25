# AppWrap architecture

This document describes the architecture as built. Its claims mirror
`docs/ROADMAP.md` exactly — every implemented item there is described here
with the same evidence level, and nothing is claimed here that the ROADMAP
does not claim. Evidence vocabulary (from
`docs/portable-profiles-qualification.md`): **Verified** (an automated
check ran and passed in the named environment), **Configuration validated**
(model, validation and build-boundary logic implemented and check-covered
on a non-Windows host; no Windows compiler or runtime involved),
**Unverified** (not exercised in the available environment).

Historical contract and audit records (`docs/audit-*.md`,
`docs/*-contract.md`, `docs/product-blueprint.md`) remain in the tree as
the record of how the design was reached; where they differ from this
document, this document and the code win.

## Module map

| Module | Responsibility | Depends on |
|---|---|---|
| `AppWrap.Core` | Immutable build input (`AppWrapInput`), folder/executable inventory, boundary validation (`InputRules`), naming (`PackageNaming`, `WizardPaths`), standard-installer options; `PortableProfiles` (lossless INI model, profile model/codec, capability catalog, validators, typed rule views, operation planner, profile workspace state); `Projects` (PAF project model, store, snapshot, validator, configuration workspace, scaffolder); `TestHarness` (pure snapshot/diff/classify/report model) | .NET only |
| `AppWrap.Generators` | Deterministic generators: Inno Setup script, portable metadata (`PortableAppsGenerator`), launcher INI, launch shim, upgrade-guard hook and installer settings | Core |
| `AppWrap.Imaging` | Icon set generation (ICO frames + PNG companions) and the 164×314 24-bit sidebar BMP conversion; bounded image loading | Windows imaging (WPF) |
| `AppWrap.Build` | Safe staging, bounded child processes, cancellation, transactional artifact publication (files and the PAF folder), the PAL compiler path, the source-bundle writer, the runtime test harness's launching half | Core, Generators, Imaging |
| `AppWrap.Gui` | The linear wizard (six standard, seven portable/PAF, eight paired steps), the portable-profile workspace, the project configuration panel, the template and test panels, themes | Core, Build |
| `AppWrap.Cli` | Every wizard/suite capability as commands with a `--json` envelope and a 0/2/3/4 exit-code contract; parse and routing layers free of console I/O | Core, Build |
| `AppWrap.Tests` | The check suite (also driven on non-Windows hosts through a harness that file-includes the portable checks with an imaging stub) | all of the above |

No module import starts processes, timers, downloads or telemetry. The UI
uses one build snapshot; editing is disabled during a build. No packaging
path executes a packaged app, a source installer, a source download action
or source-supplied custom code.

## Boundary and process contracts

`AppWrapInput` captures source folder, app name, version, source-relative
selected executable, shortcut booleans, output path, mode, optional image
paths, optional typed standard completion actions, and an optional portable
profile. Versions normalize to four numeric components for package metadata
(one to four entered components accepted). Names reject controls/CR/LF/NUL
and generator-grammar specials; literal escaping is backend-specific and
user values are never interpolated into executable script or preprocessor
statements.

Recursive inventory is bounded (50,000 entries, 40 nested folders, 20 GB)
and cancellation-aware. Reparse points/junctions are rejected in the source
and in relevant write ancestors. The chosen executable must resolve
strictly within the source; source and output must be disjoint. Source
copying takes a fresh bounded snapshot (`BuildService.Snapshot`) and never
follows links; the root `appwrap.project.json` is the one entry it skips.

Build states: Idle → Validating → Staging → Compiling → Committing →
Succeeded, with Cancelled/Failed from any pre-commit stage. One build at a
time, in a unique private work directory. Compiler stdout/stderr are
captured concurrently into a bounded diagnostic tail. Cancellation and
timeout stop only the owned process tree. Publication is transactional in
the handled-failure sense — pending file/folder plus owned backup, identity
checked before commit, rollback or precise recovery paths on failure — and
is explicitly **not** an atomic transaction across process termination or
power loss.

## Implemented areas

### Portable profiles (P0–P6) — Configuration validated

Typed portable profiles over the lossless `IniDocument` model (comments,
ordering, repeated keys and line endings round-trip byte-for-byte).
`CapabilityCatalog` is the pinned-PAL 2.2.9 key/section/token knowledge;
`PortableProfileValidator` is strict (unknown/deprecated/unsupported keys,
duplicate singletons, sequential gaps, uncovered registry writes, unsafe
wait/force-cleanup, broad targets, token rules). Imported profiles never
grant trust. P2/P3 derive typed launcher rule views and an immutable
operation plan with cross-rule diagnostics. P4 adds the installer surface:
guard-owned keys can never be overridden (`GuardSettingOverride`), the
profile's installer document merges guard-first, and the DownloadFiles,
CopyLocalFiles, LegacyVersions and plugin/common-files families are typed
and configuration-checked but blocked (`GuardOrderBlocked`) pending the
planned `AppWrapPreMutation` gate. P5 is the wizard's profile workspace
over the same state (`ProfileWorkspaceState`). P6 writes the
corresponding-source/license bundle under `Other/Source` in PAL packages.
The real PAL path compiles the root launcher with the pinned, hash-verified
PAL 2.2.9 / NSIS 2.46.5-Unicode backend (`PalCompiler` re-verifies the
makensis hash before running it; failure is fatal, never a shim fallback).
Backend preparation is **Verified** against the pinned hashes; PAL
compilation, runtime behavior and WPF layout on Windows are **Unverified**.

### Project model, editors, templates (Suite Phases 1, 2, 5) — Configuration validated

`appwrap.project.json` (schema v3; forward-only migration chain v1→v2→v3;
unknown JSON properties preserved verbatim) records name, targeted PAF
version (3.9), creation time, the deterministic bounded source snapshot
hash, build-mode history, user choices, and — since v3 — the project's
icon and sidebar image paths. Stored paths are project-relative in forward
slashes when the file lives inside the project root and absolute when it
does not (`ProjectStore.ToStoredPath`/`ResolveStoredPath`). Inference from
a bare folder reads an existing `App/AppInfo/appinfo.ini` for name/spec and
picks up the conventional `App/AppInfo/appicon.ico` as the icon; the
sidebar is never inferred (it has no conventional in-package location —
see the sidebar contract below). `PafValidator` returns ordered field-level
diagnostics for the layout, the three INI documents, custom code, and the
project image properties (missing file, escaping relative path,
unsupported picture type, 32 MB bound).

`ProjectConfigWorkspace` is the testable state behind the wizard's
configuration panel: the project's INI files on disk, parsed losslessly,
are the single source of truth; guided field edits and raw text edits
mutate the same documents; validation runs `PafValidator` with the
in-memory documents overlaid, so unsaved edits face exactly the saved
project's rules. A fifth panel tab (Project) edits the image properties.
Advanced mode is a per-project user choice, off by default, and gates only
direct text editing of the project's own two custom-code files — no
packaging path reads it. `ProjectScaffolder` writes the blank PAF template
(placeholder values validate with TODO warnings only) into a new/empty
folder transactionally; the wrapped-binary template is the inference path
with its origin recorded. The full contract is `docs/project-model.md`.

### Outputs and the pinned PortableApps toolchain — Configuration validated

Four output modes share staging. **Standard** compiles an Inno Setup 7
script (per-user, stable name-derived identity, `PrivilegesRequired=lowest`,
generated Pascal only for the typed completion options). **Portable**
stages the canonical PAF 3.9 tree — complete original folder under
`App/Payload`, generated `appinfo.ini`/`installer.ini`, a genuine compiled
root launcher (the basic NSIS shim, or the PAL launcher with a profile) —
and compiles it with the distinctly renamed silent **AppWrap Packaging
Builder**, AppWrap's minimal patch of the upstream Installer 3.9.24 wizard
(the generated-installer script itself is unmodified). **Both** runs
Standard and Portable from one snapshot with paired publication. **PAF
Package** leaves the same staged tree as a folder for the official
installer, differing only where a folder consumer requires it (empty
`Data/` placeholder; the upgrade-guard helper shipped beside its hook with
a package-relative address), published by `FolderArtifactPublisher` under
the same discipline. The compliance statement is `docs/PAF-COMPLIANCE.md`,
and `ComplianceChecks` pins its matrices to live generator/editor/catalog
output. Compilation of a PAF folder by the official installer is
**Unverified**.

Pinned-source findings that shape this design (upstream
`InstallerWizard.nsi`, Installer 3.9.24, hash-verified before extraction;
nothing downloaded at build or run time):

- `.onInit` treats the complete command line as the sole package directory
  and sets automatic compile; the modified builder therefore takes exactly
  one package-directory argument, runs silent, and sets a reliable process
  error level from the wizard's error flag.
- The content guard requires the root launcher named by `[Control] Start`
  (AppWrap's patch validates that file instead of any root `*.exe`).
- The generated installer script defines its sidebar bitmap at line 173
  (`MUI_WELCOMEFINISHPAGE_BITMAP "PortableApps.comInstaller.bmp"`, resolved
  beside the script in `Other/Source` at compile time); custom NSH is not
  included until lines 417 onward, so no late define or INI key can set it.
- **Sidebar contract:** the wizard deletes any package-side
  `Other/Source/PortableApps.comInstaller.bmp` (lines 496–511) and copies
  its own installation's `App/installer` files in before compiling, and the
  complete set of `installer.ini` keys it reads contains no graphics key.
  The sidebar is therefore installer-installation state, never package
  content. AppWrap's compiled flow substitutes the chosen image (164×314
  24-bit BMP) into its private per-build backend clone at
  `App/installer/PortableApps.comInstaller.bmp`; without a chosen image the
  upstream default is retained. A PAF Package carries no sidebar, and the
  wizard does not offer the sidebar step for that mode.
- The upgrade guard is coupled to its generated installer settings:
  `[CheckRunning] CloseEXE=NONE` is emitted only together with the
  first-party guard hook, which performs the path-scoped graceful close.
  Payloads carrying `.nsh`/`.nsi` under `Other/Source` are rejected for
  every portable output.

### Runtime test harness (Suite Phase 4) — Configuration validated

An explicit, separate action — never part of a build, and the check suite
asserts no build path references it. The pure Core half snapshots watched
trees and classifies the diff (package `Data/` writes expected; profile
writes are violations; temp writes noted); the Build half copies the
package into a throwaway tree, redirects
USERPROFILE/APPDATA/LOCALAPPDATA/TEMP into a sandbox, runs the launcher
once with a graceful-then-force timeout, writes the versioned
`test-report.json`, and always cleans up. Detection is file-system snapshot
diffing only: the registry is not observed and no clean-machine guarantee
is made (`docs/TEST-HARNESS.md`). Real-app runs on Windows are
**Unverified**.

### Command-line interface — Configuration validated

`AppWrap.Cli` exposes every wizard/suite capability: `build` (all four
modes, `--dry-run`, `--overwrite` as the replace-confirmation equivalent),
`project validate|info|create`, `config list|get|set` over the three INI
documents plus the `project` document (section `Images`, keys `IconPath` /
`SidebarImagePath`), `profile validate`, `test`, and `gui`. Precedence for
build defaults is flag > project > built-in for name, filename style, icon
and sidebar; completed builds are recorded in the project exactly as the
wizard records them. Argument parsing and routing perform no console I/O
and are check-covered; the JSON envelope, config round-trips, profile
validation and dry-run builds were smoke-run on a non-Windows host. Real
packaging runs on Windows are **Unverified**. Full reference:
`docs/CLI.md`.

### Backend provenance — Verified

Both backends are hash-pinned and prepared locally by committed scripts
(`tools/Prepare-PalBackend.ps1`, `tools/Prepare-PortableBackend.ps1`);
provenance files record the pins. The stale `modifiedWizardSha256` pin was
refreshed from a real PowerShell run of the committed preparation script
(the script deterministically produces the pinned value from the verified
official Installer 3.9.24 archive), and v0.2.0-preview shipped with the
backend passing its provenance check. `tools/Publish-AppWrap.ps1` copies
all three backends (Inno Setup, PortableApps, PAL) beside the executables —
`PalCompiler` requires `third_party/pal` beside the exe — and the release
manifest is built by walking the assembled folder and re-verified entry by
entry.

## Themes

Five native WPF brush palettes (AppWrap, Catppuccin Mocha, Nord, Gruvbox
Light, OLED Black) share layout, control behaviour, validation and backend
orchestration; Windows high-contrast mode uses system colors. Theme choice
persists in `Data/window.json` alongside window bounds and the preferred
filename style, starts no process, downloads nothing, and never changes
generated packages.

## Planned (ROADMAP)

- **The `AppWrapPreMutation` gate** — the reviewed earlier-safe point in
  the generated installer that would unblock the DownloadFiles,
  CopyLocalFiles, LegacyVersions and plugin/common-files families. Until
  it exists they stay typed, discoverable, configuration-checked and
  blocked.
- **Host runtime fixture qualification** — what only a Windows host can
  prove: an actual PAL compile and source bundle inside a package, the
  runtime fixture suite, guard variants, WPF layout at 680×560, official
  installer compilation of a PAF Package folder, and the test harness
  against real packaged applications.

## License and distribution obligations

AppWrap's own code is MIT. The modified packaging builder is GPL-2.0-or-later
derived work: it ships with its exact source, the upstream source, the
patch, the preparation script and clear modified attribution, distinctly
named so it never presents as the upstream executable. The generated
installer script is unmodified upstream and carries its own exception for
OSI-approved/freeware payloads (commercial use by contacting
PortableApps.com); the PAL launcher bundle carries the GPL v2 text and
plugin notices the pinned backend ships, and records what the backend does
not ship rather than inventing it. NSIS components keep their full COPYING
and per-plugin notices. None of this grants redistribution rights for a
packaged application, upstream endorsement, or trademark use; unknown
license information in a packaged app stays unknown/conservative. AppWrap
is not endorsed by PortableApps.com. Distribution is a direct-download ZIP
only — no winget, Scoop or Chocolatey packages (ROADMAP, out of scope).

## Verification gates

- Solution build with warnings as errors (Windows targeting enabled on
  non-Windows hosts).
- The full check suite: naming, wizard paths, CLI parsing/routing and
  precedence, project model/migration/snapshot/validator, configuration
  workspace, scaffolder, profiles (validator, planner, installer surface,
  workspace, bundle), PAF package staging, compliance matrices, test
  harness model and pipeline, batch publication, upgrade guard.
- On Windows, the suite additionally runs real Inno Setup and packaging
  builder compiles and static archive extraction checks.
- Published files are read back and hash-verified; release assets are
  re-downloaded and hash-checked against the local build.
- Anything only a Windows host can prove is recorded **Unverified**, never
  inferred from compilation.
