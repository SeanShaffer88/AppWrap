# AppWrap command-line reference

AppWrap is a dual-mode executable: the wizard and the command-line
interface are one self-contained `AppWrap.exe`, and the two entry points
are interchangeable.

- **`AppWrap.Cli.exe`** is a tiny native console shim that forwards its
  arguments to `AppWrap.exe` (located beside it) and exits with the same
  exit code. It exists so scripts keep a console-subsystem entry point
  with the familiar name; if `AppWrap.exe` is missing beside it, the shim
  exits 4 with an explanatory message.
- **`AppWrap.exe` itself** accepts the same command line directly:
  `AppWrap.exe build …`, `AppWrap.exe --help`, and so on. With no
  arguments — or an unrecognized first argument — it opens the wizard
  exactly as before; only an unmistakable CLI invocation (a command name
  or a global flag as the first argument) runs the CLI in-process.
- The router's `gui` command, when it is already running inside
  `AppWrap.exe`, reports that the GUI is this executable and exits 0
  instead of launching a sibling.

The CLI exposes every capability of the wizard and the Development Suite
as commands, so scripts and local AI operators can drive AppWrap without
the GUI. It is a thin shell over the same Core/Build operations the wizard
calls: validation, packaging, project handling and the test harness behave
identically, and failures surface as the same typed errors.

Keep the executables in the distribution folder: the CLI resolves its
packaging backends from the `third_party` tree beside `AppWrap.exe` and
stages builds in the `Data` folder beside it. Run from a development tree
without backends, commands that do not need a backend (validation, config,
project, dry-run) still work, and builds that need one fail with the
existing "packaging component is missing" error (exit 4).

## Global options

Accepted anywhere on the command line, for every command:

| Option | Meaning |
| --- | --- |
| `--json` | Print exactly one JSON object (the envelope below) on stdout. Nothing else is written to stdout. |
| `--dry-run` | `build` only: validate and print the plan; write nothing. |
| `--verbose` | Progress lines on stderr. Stdout stays machine-clean, so `--json --verbose` is safe to parse. |
| `--help` | Help for the command (or the top-level command list). Exit 0. |
| `--version` | Print `AppWrap <version>`. Exit 0. |

Options use `--name value` or `--name=value`. Unknown options, missing
values, duplicated options and conflicting flag pairs are usage errors
(exit 2) that name the valid choices.

## Exit codes

| Code | Meaning |
| --- | --- |
| 0 | Success. For `project validate` / `profile validate`: no error-severity diagnostics. For `test`: verdict Pass. |
| 2 | Usage error: unknown command/option, missing or extra arguments, unknown config document/section/key, conflicting flags. Help for the command is printed with the message. |
| 3 | Validation failed: input or validator diagnostics with error severity. Nothing was written. |
| 4 | Build or test failure: a backend/build failure, a failing test verdict (the report is still written), a cancellation, or `gui` when `AppWrap.exe` is absent. |

Errors never print a stack trace in JSON mode.

## The JSON envelope

Every command under `--json` prints one object:

```json
{
  "ok": true,
  "command": "project validate",
  "exitCode": 0,
  "result": { },
  "diagnostics": [
    { "severity": "warning", "code": "TodoPlaceholder",
      "file": "App/AppInfo/appinfo.ini", "section": "Details", "key": "Publisher",
      "message": "…", "hint": "…" }
  ],
  "error": null
}
```

`result` is command-specific (documented per command below) or null.
`diagnostics` is the structured form of what human mode prints as lines;
`section`, `key` and `hint` are null when a diagnostic is not about one
field. `error` carries the failure message when `ok` is false.

## Commands

### `build` — package a folder

The wizard's Build action, all four output modes.

```
appwrap build --source <folder> [options]
```

| Option | Meaning |
| --- | --- |
| `--source <folder>` | Application folder to package. Required. |
| `--mode installer\|portable\|both\|paf` | Output mode. Default `installer` (the wizard default). |
| `--name <name>` | App name. Default: the `--project` project's name, else the folder name. |
| `--version <version>` | App version. Default `1.0`. |
| `--launch <relative exe>` | Launch executable inside the source folder. Default: the folder's sole `.exe`; if there are several, the command fails and lists them. |
| `--output <path>` | Destination. For `installer`/`portable`: the full package file path. For `both`: a folder; both suggested filenames are written into it. For `paf`: a folder; the package folder (named after the app's AppID) is created inside it. Default: the Desktop folder with the suggested filename/folder. |
| `--filename-style <style>` | `Name-Version-Setup` (default), `Name_Setup`, or `Name-Setup-Version` (the enum names `NameVersionSetup`, `NameOnly`, `NameSetupVersion` are also accepted). |
| `--icon <image>` | Launcher icon for portable and PAF packages (ignored for installer, as in the wizard). Default: the `--project` project's `iconPath`, resolved against the project root. |
| `--sidebar <image>` | Sidebar image for portable packages (never for PAF Package — the official installer has no package-side sidebar; see `docs/PAF-COMPLIANCE.md`). Default: the `--project` project's `sidebarImagePath`. |
| `--start-menu` / `--no-start-menu` | Offer a Start Menu shortcut in the installer. Default on. |
| `--desktop` / `--no-desktop` | Offer a Desktop shortcut in the installer. Default off. |
| `--run-after` / `--no-run-after` | Offer to launch the app after install. Default on. |
| `--startup` | Offer Start with Windows after install. Default off. |
| `--website <url>` | Developer website offered after install (absolute http/https URL). |
| `--profile <file>` | Portable profile for portable modes: a `.json` profile as saved by the wizard, or a launcher `.ini` (treated as the profile's launcher document, PAL kind). Imported under the wizard's trust rules — imports never grant trust. A profile with validation errors stops the build with exit 3 before anything is written. |
| `--project <file-or-folder>` | An existing `appwrap.project.json` (or its folder). Supplies the default name, filename style, icon and sidebar image (explicit flags override all of them) and records each completed build — including the effective image picks — in the project, as the wizard does. |
| `--overwrite` | Replace existing output files / PAF package folder. Without it, an existing destination is an error (exit 3) — the CLI's equivalent of the wizard's replace confirmation. |

Human output: one `Built …` line per output with path, size and SHA-256.

JSON `result`:

```json
{ "mode": "portable", "name": "ListSorter", "version": "1.0",
  "launch": "ListSorter.exe", "source": "C:\\Apps\\ListSorter", "dryRun": false,
  "outputs": [ { "mode": "portable", "path": "C:\\Users\\…\\Desktop\\ListSorter-1.0-Portable.paf.exe",
                 "sha256": "…", "bytes": 123456 } ] }
```

With `--dry-run`, `outputs` carries the planned destinations (no `sha256`/
`bytes`) and nothing is written — including no project record.

Example:

```powershell
appwrap build --source C:\Apps\ListSorter --mode both --output D:\Packages --json
appwrap build --source C:\Apps\ListSorter --mode paf --project C:\Apps\ListSorter --dry-run
```

### `project validate <folder-or-project-file>`

Runs `PafValidator` over the folder (a path to `appwrap.project.json`
itself is also accepted). Prints every diagnostic with severity, code,
file, field, message and hint. Exit 3 when any diagnostic is an error;
warnings and info still exit 0.

JSON `result`: `{ "root", "projectFile": bool, "name", "pafSpecVersion" }`,
with all diagnostics in the envelope's `diagnostics` array.

### `project info <folder-or-project-file>`

Summarizes the stored project, or the project inferred for a bare folder
(inference does not write the file). JSON `result`:

```json
{ "root", "inferred": false, "name", "pafSpecVersion", "schemaVersion": 3,
  "createdUtc", "sourceSnapshotHash", "sourceSnapshotTruncated": false,
  "icon": { "path": "App/AppInfo/appicon.ico", "resolved": "C:\\Apps\\…\\App\\AppInfo\\appicon.ico", "exists": true },
  "sidebar": { "path": null, "resolved": null, "exists": false },
  "buildHistory": [ { "mode": "PortableApps", "completedUtc" } ],
  "userChoices": { } }
```

### `project create <folder>`

The wizard's New project from template action.

| Option | Meaning |
| --- | --- |
| `--template blank\|wrapped` | Default `blank`. `blank` scaffolds a minimal valid PAF project (TODO placeholders surface as validator warnings, never errors). `wrapped` records the wrap-a-folder template for an existing app folder: it loads or infers the project, stamps the template origin, and saves `appwrap.project.json`. |
| `--name <name>` | App name. Default: the folder name. |
| `--app-id <id>` | Must equal the AppID AppWrap derives from the name (letters/digits + `Portable`); anything else is a usage error naming the derived ID. |
| `--advanced` | Blank template only: start with Advanced mode on and write the inert custom-code starters, as the wizard does. |

JSON `result` (blank): `{ "root", "template": "blank", "name", "appId", "advancedMode" }`,
plus the fresh project's diagnostics in the envelope. For `wrapped`:
`{ "root", "template": "wrapped", "name", "inferred" }`.

### `config list|get|set` — project configuration

Operates on a project folder's three INI documents through the Phase 2
configuration workspace (the lossless INI model; unknown sections, keys,
comments and ordering survive every edit; saves are atomic and refresh the
project snapshot). Documents: `appinfo`, `launcher`, `installer`, and
`project` — the project's own properties in `appwrap.project.json`, which
has a single section, `Images`, with two path fields: `IconPath` and
`SidebarImagePath`. For the project document, `list` reports the stored
path (project-relative when the file lives in the project), the resolved
absolute path and whether the file exists; `get` prints the stored path;
`set` takes an image path (relative paths resolve against the project
root) and saves the project file through the same workspace save.

```
appwrap config list <folder> [document]
appwrap config get <folder> <document> <section> <key>
appwrap config set <folder> <document> <section> <key> <value>
```

- `list` without a document lists the four documents with their paths and
  field counts. With a document, it lists every guided field with its
  current value (`(not set)` when absent).
- `get` prints the value, or `(not set)` for a known field with no value.
  JSON `result`: `{ "document", "section", "key", "present": bool, "value" }`.
- `set` writes one field and saves. Boolean fields accept only
  `true`/`false` (anything else is exit 3). An unknown document, section or
  key — one that is neither in the guided field catalog nor present in the
  document — is a usage error (exit 2) whose message lists the valid
  sections and keys. JSON `result`: `{ "document", "section", "key", "value", "path" }`.

Loading a bare folder infers its project in memory (as the wizard does);
`set` then also persists `appwrap.project.json` through the workspace save,
exactly like the wizard's editors.

### `profile validate <file>`

Loads a portable profile — a `.json` profile as saved by the wizard, or a
launcher `.ini` treated as the profile's launcher document — and runs the
portable-profile validator (capability catalog, sequential gaps, uncovered
registry writes, unsafe cleanup, broad targets, tokens). Exit 3 on any
error-severity diagnostic. JSON `result`: `{ "file", "format": "json"|"ini", "executionKind" }`.

### `test <package-folder>` — runtime test harness

The wizard's Test package action: an explicit, separate action — builds
never run a packaged app. The package is copied to a throwaway sandbox,
launched once with a redirected profile, and file-snapshot diffs are
classified into `test-report.json`; writes to the profile are portability
violations.

| Option | Meaning |
| --- | --- |
| `--mode portable\|paf` | Which package kind the folder is. Default `portable`. |
| `--launch <file>` | Launch file at the package root. Default: the package's appinfo `[Control] Start`, else its sole root `.exe`. |
| `--timeout <seconds>` | 1–3600. Default 60. On timeout the app is asked to close, then terminated. |
| `--report <path>` | Default: `Data/TestReports/test-report-<yyyyMMdd-HHmmss>.json` beside the CLI (the GUI's convention). |
| `--registry` | Also observe the registry (Windows only): the HKCU/HKLM `Software` roots are snapshotted before and after the run; registry violations fail the verdict. The wizard's Test panel always observes the registry. The JSON result gains `registryObserved`, `registryChanges` and `registryViolations`. |
| `--sandboxie` | Run the app inside a dedicated Sandboxie box instead (pipeline stage 4 leak verification, `docs/PIPELINE.md`): every watched root is outside the box, so a Pass certifies zero writes escaped. Requires Sandboxie-Plus; without it the command exits 4 with a plain explanation — it never silently falls back to the host mode. The result and the report record `observationBackend`. |

Exit 0 on verdict Pass, 4 on verdict Fail — the report is written either
way. JSON `result`:

```json
{ "verdict": "Pass", "reportPath", "appName", "mode": "portable",
  "appExitCode": 0, "timedOut": false, "durationSeconds": 1.2,
  "detectedWrites": 3, "observationBackend": "host",
  "violations": [ { "root": "Profile", "path": "AppData/Roaming/App/settings.ini",
                    "change": "Created", "category": "ProfileLeakage" } ],
  "cleanupWarning": null }
```

### `audit <source>` — portable-conversion audit

Audits a setup `.exe`/`.msi` or portable `.zip` for conversion to a
PortableApps.com package: where the app stores data and which registry
entries it creates (`docs/AUDIT.md`). Static by default — nothing is
executed: a zip is inventoried (and extracted under
`Data/Audit/<name>-<id>` beside the CLI, kept so `build --source` can
package the payload), an `.msi`'s tables are read, an `.exe` is
identified by family with its payload stated as an audit limit.

| Option | Meaning |
| --- | --- |
| `--runtime` | The explicit second pass (Windows only, `.exe`/`.msi` only): silently install into a scratch folder, launch the installed app once, and merge the observed registry writes into the findings. Never part of a build. |
| `--engines` | Use the pipeline's prepared extraction engines for the static pass (7-Zip / innoextract / lessMSI under `third_party/engines` beside the CLI; `docs/PIPELINE.md` stage 1): installer payloads are unpacked and inventoried instead of stopping at family identification. Without prepared engines the lines say so and the pass degrades to family identification + the explicit audit limit. |
| `--sandboxie` | With `--runtime`: profile inside a dedicated Sandboxie box (pipeline stage 2) — the install and launch happen in the box and the box's files + registry hive are harvested, so the host is untouched. Without Sandboxie-Plus the runtime pass falls back to the host route and the result says so. The JSON result gains `runtimeBackend` (`sandboxie` / `host`). |
| `--draft <profile.json>` | Synthesize a DRAFT portable profile from the findings and save it in the wizard's profile `.json` shape. The draft is untrusted until reviewed and saved in the profile workspace; if the draft itself has validation errors the command exits 3 (the file is still written). |
| `--report <path>` | Write the full audit report JSON (`audit-report` shape: source, kind, payload folder, main executable, typed findings with evidence and confidence). |
| `--launch <file>` | For `--runtime`: the installed app's launch file relative to the install folder. Default: the inventoried main `.exe`. |
| `--silent-args <args>` | For `--runtime` with an `.exe`: the installer's silent arguments as one string, for families AppWrap does not know. An unknown family without this is refused, never run interactively. |
| `--msi-fixture <file>` | Read MSI tables from a recorded fixture JSON instead of `msi.dll` (non-Windows hosts and checks). |

JSON `result`: `{ "source", "sourceKind", "payloadFolder", "mainExecutable",
"findings": [ { "kind", "subject", "detail", "evidence", "confidence",
"hardWarning" } ], "hardWarnings", "draftProfile", "draftNotes",
"draftUnmapped", "reportPath" }` (draft/report fields only when requested).

A reviewed draft builds like any saved profile:
`appwrap build --source <payloadFolder> --mode paf --audit-draft <profile.json>`
(`--audit-draft` is `--profile` under its audit name; the two cannot be
combined).

### `gui`

Launches `AppWrap.exe` beside the CLI and exits 0. When no `AppWrap.exe`
sits beside the CLI, it fails with exit 4 and a message naming the expected
location.

## Notes for AI operators

- Prefer `--json` and branch on `exitCode` + `ok`; parse `diagnostics` for
  field-level fixes instead of scraping human text.
- Exit 3 always means "fix the input" (the diagnostics or error message
  say exactly which field); exit 4 means the operation itself failed.
- `build --dry-run --json` is the safe preflight: it validates everything
  the real build validates and reports the exact destinations.
- The CLI never executes a packaged app except through the explicit
  `test` command, and never executes source-folder installers or hooks —
  the same guarantees as the GUI.
