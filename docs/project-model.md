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

## What Phase 1 deliberately does not do

- No editors: Phase 2 builds the guided INI editors and Advanced mode on top of
  this model. The validator runs from tests and the model API today; surfacing its
  diagnostics in the wizard UI arrives with the Phase 2 editors.
- No new build outputs: Phase 3 adds the "PAF Package" option. The project model
  does not change any existing Installer / Portable / Both behaviour.
- The snapshot hash is a change-detection aid recorded in the project file; it is
  not a signature and is never used to gate a build.
