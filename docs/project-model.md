# AppWrap project model (Phase 1)

AppWrap now treats a PortableApps project as a persistent, structured thing rather
than re-deriving everything from the chosen folder on every run. This document is
the contract for the project file, its schema migrations, the source snapshot, and
the PAF validator. Implementation: `AppWrap.Core/Projects/`
(`PafProject`, `ProjectStore`, `ProjectMigrator`, `SourceSnapshot`, `PafValidator`,
`PafLayout`). The wizard wiring lives in `AppWrap.Gui/MainWindow.Project.cs`.

## The project file

`appwrap.project.json` sits at the project root (the folder chosen in the wizard).
It is human-readable, indented JSON, written atomically (temporary file moved into
place). Current schema version: **2**.

| Field | Type | Meaning |
|---|---|---|
| `schemaVersion` | number | Document schema version; migrated forward on load. |
| `name` | string | Project name (restored into the wizard on reopen). |
| `pafSpecVersion` | string | PAF specification version the project targets. Default `3.9` — the version the pinned toolchain implements (PortableAppsGenerator emits `[Format] Version=3.9`; Installer backend is 3.9.24, Launcher is 2.2.9). |
| `createdUtc` | string (ISO 8601) | When the project was created or inferred. |
| `sourceSnapshotHash` | string \| null | Deterministic SHA-256 of the source tree (see below); null when no snapshot exists or the tree exceeded the snapshot bounds. |
| `sourceSnapshotTruncated` | bool | True when the last snapshot stopped at the bounds; the hash is null in that case rather than describing a partial walk. |
| `buildModeHistory` | array of `{mode, completedUtc}` | One entry per completed build (`Standard` / `PortableApps`), appended by the wizard after a successful build. |
| `userChoices` | object (string → string) | User configuration choices recorded by the wizard (currently `namingStyle`). |

Properties AppWrap does not model are **preserved verbatim** through a load/save
round-trip (JSON extension data), so files written by newer tooling never lose data
when an older build saves them.

### Schema history and migration policy

- **v1**: `schemaVersion`, `name`, `pafSpecVersion`, `createdUtc`.
- **v2**: adds `sourceSnapshotHash`, `sourceSnapshotTruncated`, `buildModeHistory`,
  `userChoices` — all defaulted empty/null by the v1→v2 migration; nothing a v1 file
  carried is altered.

Migrations are forward-only, one version per step, applied in memory on load and
persisted on the next save. A file with a schema version newer than the running
build is **rejected** with an actionable error (never silently downgraded), as are
malformed JSON and a missing schema version.

### Backwards compatibility

A folder with no `appwrap.project.json` works exactly as before: the wizard infers
a project (name and targeted PAF version from an existing
`App/AppInfo/appinfo.ini` when present, otherwise the folder name and the toolchain
default) and writes the file on first save — which happens before the first build.
Packaging output for folders that never open the wizard is unchanged.

## Source snapshot

`SourceSnapshot.Compute` hashes the tree deterministically: files in ordinal
relative-path order, each contributing its forward-slash relative path, byte size,
and content SHA-256. The walk is bounded by the same limits the build layer
enforces for source folders (50,000 entries, 40 nested folders, 20 GB — see
`BuildService.Snapshot`); exceeding a bound yields the truncated marker described
above. The root `appwrap.project.json` is excluded — it records the hash, so it can
never be an input to it. Nested files with the same name are ordinary payload and
are included.

## Payload safety

`BuildService.Snapshot` copies the entire source folder into the build staging
area, so a root project file would otherwise be swept into every package (under
`App/Payload` for portable packages, and into the installer payload for standard
ones). The snapshot therefore skips exactly one entry: a **file at the source root**
named `appwrap.project.json` (identified by the shared
`ProjectStore.IsProjectFileName` predicate). Behaviour for folders without a
project file is untouched, and `ProjectChecks` proves the payload file list is
identical with and without the root project file present.

## PafValidator

`PafValidator.Validate(root, project?)` checks the folder against the targeted PAF
specification and returns ordered, structured `PafDiagnostic` values. Every
diagnostic carries: severity (`Info` / `Warning` / `Error`), a stable code, the
file path relative to the project root (forward slashes), the INI section and key
when the diagnostic is about a field, a message, and a remediation hint.

Severity policy: files that **exist** are validated field-by-field and broken
fields are errors (missing `[Details] AppID`, a non-numeric
`[Version] PackageVersion`, missing `[Control] Start`, a wrong `[Format] Type`,
INI parse problems, oversized metadata). Canonical files that are **absent** are
reported at Info/Warning level, because AppWrap generates them during packaging —
absence is normal for a plain application folder; broken content is not.

Coverage: canonical layout presence (`App/`, `Data/`, `Other/`); `appinfo.ini`
(`[Format]`, `[Details]` incl. the PortableApps.com category list, `[Version]`,
`[Control]`, `[License]` value shape); `installer.ini` parse health plus the
guard-owned `[CheckRunning] CloseEXE` note; every launcher INI under
`App/AppInfo/Launcher/` (`[Launch] ProgramExecutable` required, `WaitForProgram`
shape, INI named after the AppID); custom code (a `Custom.nsh` is informational —
AppWrap never compiles source-supplied launcher code — while `.nsh`/`.nsi` files
under `Other/Source` warn, because packaging rejects them); and project-level
spec-version alignment with the pinned toolchain.

## Grounded layout — where the brief and the toolchain disagreed

The expansion brief's example paths were checked against the pinned backends and
AppWrap's own generators. The toolchain won in three places:

| Brief path | Actual path | Evidence |
|---|---|---|
| `App/AppInfo/Launcher/launcher.ini` | `App/AppInfo/Launcher/<AppID>.ini` | PortableBuild writes `<PackageId>.ini` into `App/AppInfo/Launcher/`; the compiled launcher reads the INI named after its AppID. |
| `App/AppInfo/Installer/installer.ini` | `App/AppInfo/installer.ini` | PortableBuild writes the merged installer document beside appinfo.ini; the pinned installer's own log/metadata layout (`App/AppInfo/installer.ini`) agrees. |
| `App/AppInfo/Launcher/Custom.nsh` | `App/AppInfo/Launcher/Custom.nsh` | **Brief correct.** Segments.nsh in the pinned PAL 2.2.9 source includes `${PACKAGE}\App\AppInfo\Launcher\Custom.nsh` (nonfatal) at compile time. (The PAL manual's prose mentions `Other\Source\Custom.nsh`, the pre-2.1 location; the 2.2.9 compiler include is authoritative.) |
| `App/AppInfo/Installer/PortableApps.comInstallerCustom.nsh` | `Other/Source/PortableApps.comInstallerCustom.nsh` | PortableBuild writes the first-party upgrade-guard hook to `Other/Source/PortableApps.comInstallerCustom.nsh` in generated packages; the upstream mechanism consumes it from `Other\Source` (see docs/portable-upgrade-guard-candidate.md). |

`App/AppInfo/appinfo.ini`, `App/`, `Data/`, and `Other/` matched the brief as-is.

## Phase 2 — configuration editors and Advanced mode

Phase 2 surfaces this model in the wizard. Implementation:
`AppWrap.Core/Projects/ProjectConfigWorkspace.cs` (testable editor state) and
`AppWrap.Gui/MainWindow.ProjectConfig.cs` (the panel, reached from the first
wizard step once a folder's project is loaded).

### Editors and the binding model

The panel has four tabs: **App info** (`App/AppInfo/appinfo.ini`), **Launcher**
(`App/AppInfo/Launcher/<AppID>.ini`), **Installer** (`App/AppInfo/installer.ini`)
and **Custom code**. Each INI tab offers guided fields for the keys AppWrap
itself emits and validates, plus the document's full text. The files on disk,
parsed through the lossless `IniDocument`, are the single source of truth:
field edits and text edits mutate the same parsed document, so unknown
sections, unknown keys, comments and ordering survive every edit, and saving
writes the changed documents back, refreshes the source snapshot and persists
the project file. A document that was never edited and does not exist is not
created — AppWrap still generates it during packaging.

Field coverage is grounded in the real formats, not the brief's examples:
appinfo fields follow `PortableAppsGenerator`'s emitted document plus the
metadata surface `PafValidator`/`InstallerRules` model (`[Format]`, `[Details]`,
`[License]` as a section of true/false keys, `[Version]`, `[Control]`,
`[Dependencies]`, `[SpecialPaths]`); launcher fields follow the pinned PAL
2.2.9 catalogue (`[Launch]`, `[Activate]`, `[Language]`, and `[LiveMode]`
`CopyApp` — the pinned `RunLocally` segment reads it, while `CopyData` was
removed by PAL 2.2.9 and is deliberately not offered); installer fields are the
scalar keys of `InstallerRules` (`[Source]`, `[CheckRunning]`,
`[MainDirectories]`, `[OptionalComponents]`, `[Languages] ENGLISH`). The
numbered preserve/remove and optional-payload lists have no scalar fields and
stay editable through the document text view. Fields the validator treats as
errors when missing (appinfo `Name`, `AppID`, `PackageVersion`, `Start`;
launcher `ProgramExecutable`) carry a required marker.

### Validation

A **Validate all** action — and every save — runs `PafValidator` across the
whole project. The validator's rules are unchanged; the workspace passes its
in-memory documents as an overlay, so unsaved edits are validated by exactly
the rules the saved project will face, and each diagnostic is shown next to
the field it belongs to (matched by file, section and key) as well as in the
panel's diagnostics list. Diagnostics never block navigation; packaging keeps
its own existing gates.

### Advanced mode

Advanced mode is a per-project switch, **off by default** (including for
inferred projects), persisted in the project file's `userChoices` as
`advancedMode`, and shown with a warning about what it enables. It unlocks
direct text editing of the two custom-code files inside the project —
`App/AppInfo/Launcher/Custom.nsh` and
`Other/Source/PortableApps.comInstallerCustom.nsh`. With it off, those files
are read-only in the panel and a programmatic write is refused as a typed
result (`CustomCodeWriteStatus.RefusedAdvancedModeOff`), leaving the file
untouched.

Advanced mode changes **no packaging behaviour**. Editing custom code is a
project-file edit only: packaging still rejects source-supplied build scripts
in a payload (the validator's `SourceInstallerHook` warning says so for the
installer hook), still never compiles source-supplied launcher code, and the
generated installer still uses only AppWrap's own first-party hook.

## Phase 5 — New Project from Template

The wizard's first page offers **New project from template…**, with two
templates:

- **Blank PAF app** — `ProjectScaffolder.CreateBlank` scaffolds a minimal,
  valid PortableApps.com Format skeleton into a folder that must be new or
  empty (a non-empty destination is refused, never merged into). The tree is
  staged in a sibling `.appwrap-scaffold-*.tmp` folder and moved into place
  only when complete, so a failed scaffold leaves nothing behind. It writes
  the canonical layout (`App/AppInfo/` with `appinfo.ini` and
  `installer.ini`, `App/AppInfo/Launcher/<AppID>.ini`, `Data/`, `Other/`)
  plus an `appwrap.project.json` recording the template origin (user choice
  `template` = `BlankPafApp`), the targeted PAF spec version (3.9 by
  default; another version can be chosen at scaffold time and is recorded,
  with the validator flagging the mismatch against the toolchain), and the
  Advanced mode choice. No build is run.
- **Wrapped binary** — the pre-existing wrap-a-folder flow, listed in the
  picker for consistency. It has no separate implementation: choosing it
  routes to the usual source-folder selection, and the project inferred from
  that folder records `template` = `WrappedBinary`. The recording happens
  only at inference — reopening a project never rewrites the template it was
  created from, and folders chosen outside the template flow record nothing.

### Placeholders and TODO diagnostics

A blank scaffold must be valid immediately, so required fields carry valid
placeholder values: the AppID is derived from the app name by the generator's
convention (letters and digits, ending in `Portable`), `PackageVersion` is
`1.0.0.0`, `[Control] Start` is `<AppID>.exe`, the category is `Utilities`.
Values that still need the author — publisher, homepage, description,
display version, and the launcher's `ProgramExecutable` (`TODO.exe`) —
contain a `TODO` marker. Phase 5 adds one additive validator rule,
`TodoPlaceholder`: any INI entry value containing `TODO` is reported as a
**warning** naming the file, section and key, with a hint to replace it
before publishing. The rule changes no existing severity, and a fresh blank
scaffold validates with zero errors and only these TODO warnings (plus any
info the chosen options imply). Scaffolded projects open in the Phase 2
configuration editors directly — the wizard loads the created project and
the panel is one click from the first page.

### Custom-code starters and Advanced mode

No `Custom.nsh` or installer custom code is scaffolded unless the new
project's Advanced mode is on (chosen in the template panel, recorded in the
project file, off by default). With it on, both starters are written as
comment-only, inert files whose headers document how each is consumed —
honestly, including the asymmetry: the launcher's `Custom.nsh` *is* included
by the PortableApps.com Launcher when a launcher for the project is compiled
from a portable profile, while a source-supplied
`Other/Source/PortableApps.comInstallerCustom.nsh` is *not* consumed by
AppWrap packaging (payloads containing one are rejected, and generated
packages carry AppWrap's own guard hook at that path), so the installer
starter is for use with the official installer and the validator's
`SourceInstallerHook` warning applies to it as to any source hook.

## What Phase 1 deliberately does not do

- Editors and Advanced mode were Phase 2 work; they are described above. In
  Phase 1 the validator ran from tests and the model API only.
- No new build outputs: Phase 3 adds the "PAF Package" option. The project model
  does not change any existing Installer / Portable / Both behaviour.
- The snapshot hash is a change-detection aid recorded in the project file; it is
  not a signature and is never used to gate a build.
