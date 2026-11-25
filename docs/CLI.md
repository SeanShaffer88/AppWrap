# AppWrap command-line reference

`AppWrap.Cli.exe` is the command-line interface to AppWrap. It ships beside
`AppWrap.exe` in the distribution folder and exposes every capability of
the wizard and the Development Suite as commands, so scripts and local AI
operators can drive AppWrap without the GUI. The CLI is a thin shell over
the same Core/Build operations the wizard calls: validation, packaging,
project handling and the test harness behave identically, and failures
surface as the same typed errors.

Keep the CLI in the distribution folder: like the GUI, it resolves its
packaging backends from the `third_party` tree beside the executable and
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
| `--icon <file.ico>` | Launcher icon for portable and PAF packages (ignored for installer, as in the wizard). |
| `--sidebar <image>` | Sidebar image for portable packages. |
| `--start-menu` / `--no-start-menu` | Offer a Start Menu shortcut in the installer. Default on. |
| `--desktop` / `--no-desktop` | Offer a Desktop shortcut in the installer. Default off. |
| `--run-after` / `--no-run-after` | Offer to launch the app after install. Default on. |
| `--startup` | Offer Start with Windows after install. Default off. |
| `--website <url>` | Developer website offered after install (absolute http/https URL). |
| `--profile <file>` | Portable profile for portable modes: a `.json` profile as saved by the wizard, or a launcher `.ini` (treated as the profile's launcher document, PAL kind). Imported under the wizard's trust rules — imports never grant trust. A profile with validation errors stops the build with exit 3 before anything is written. |
| `--project <file-or-folder>` | An existing `appwrap.project.json` (or its folder). Supplies the default name and filename style (explicit flags override both) and records each completed build in the project, as the wizard does. |
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
{ "root", "inferred": false, "name", "pafSpecVersion", "schemaVersion": 2,
  "createdUtc", "sourceSnapshotHash", "sourceSnapshotTruncated": false,
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
project snapshot). Documents: `appinfo`, `launcher`, `installer`.

```
appwrap config list <folder> [document]
appwrap config get <folder> <document> <section> <key>
appwrap config set <folder> <document> <section> <key> <value>
```

- `list` without a document lists the three documents with their paths and
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

Exit 0 on verdict Pass, 4 on verdict Fail — the report is written either
way. JSON `result`:

```json
{ "verdict": "Pass", "reportPath", "appName", "mode": "portable",
  "appExitCode": 0, "timedOut": false, "durationSeconds": 1.2,
  "detectedWrites": 3,
  "violations": [ { "root": "Profile", "path": "AppData/Roaming/App/settings.ini",
                    "change": "Created", "category": "ProfileLeakage" } ],
  "cleanupWarning": null }
```

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
