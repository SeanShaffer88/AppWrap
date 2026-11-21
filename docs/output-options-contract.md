# Output naming and paired-build contract

User explicitly requested naming conventions and simultaneous standard/portable output. This extends the approved product specification without adding a wizard page or approval gate. Controller approves this operational contract before implementation. Existing generator and single-output module contracts remain compatible.

## Product behavior

Format choice has three options: Installer, Portable app, Both. Both uses the existing seven-question path because optional icon/sidebar apply to the portable artifact. The shortcuts question applies to the standard installer; use a short helper in Both rather than another checkbox/page. Keep the final screen compact at 680×560 DIP.

On the existing final question show a labeled naming-style dropdown and actual filename preview(s). Presets:

| Style | Standard preview pattern | Portable preview pattern |
|---|---|---|
| Name-Version-Setup | `{Name}-{Version}-Setup.exe` | `{Name}-{Version}-Portable.paf.exe` |
| Name_Setup | `{Name}_Setup.exe` | `{Name}Portable.paf.exe` |
| Name-Setup-Version | `{Name}-Setup-{Version}.exe` | `{Name}-Portable-{Version}.paf.exe` |

Use the same validated display name/version inputs as the build; version preview is the entered valid numeric version, not silently padded package metadata. Validate resulting Windows filenames/lengths and suffixes. Derive names through one pure Core function, never duplicate interpolation rules in GUI/Build. Unknown persisted style falls back safely. Persist preferred style in existing app settings without payload-specific filesystem state or new telemetry.

Single format retains Save-file dialog with the generated name prefilled; users can customize the final filename there. Store that chosen filename as explicit override. Changing naming preset/app name/version/mode invalidates the selected filename override and existing overwrite consent; retain the parent folder where possible, update previews, and require a fresh selection or explicit collision confirmation before replacing any newly targeted existing file. Never silently map prior consent to a different destination.

Both final step uses one folder picker plus two compact filename previews and naming-style dropdown. Do not put two large file-picker cards on screen. Both always outputs the two distinct derived filenames to the same existing chosen folder. User-selected single-output custom filename behavior remains available in the single-format path; Both does not introduce a custom-name editor. One Build action starts the paired operation. If either target exists, one concise native dialog names both intended targets and identifies which will be replaced; cancel leaves all files untouched. No extra confirmation for nonexistent targets.

## Minimal module/interface changes

- **Core:** add `PackageNamingStyle` and pure `PackageNaming.GetFileName(name, version, singleMode, style)` or equivalent canonical API. Existing `OutputMode.Standard/PortableApps` remain the two backend variants. Represent UI choice Both separately (e.g. `BuildSelection` or GUI selection enum); do not overload single `AppWrapInput.OutputPath` with a directory or pass Both down as a single-generator mode.
- **Build:** add explicit `BuildBothAsync(standardInput, portableInput, token)` returning a `BatchBuildResult` with two ordinary `BuildResult` records, or equivalent typed batch request/result. Validate exactly Standard then PortableApps, identical source/name/version/main executable/shortcut values, distinct canonical final paths and same parent. Optional images belong to portable input. Existing `BuildAsync` remains available and preserves single-output behavior. Extract only a small internal prepare/commit seam needed to share the implementation; no broad public API break.
- **Generators/Imaging:** consume ordinary single-mode inputs as before. Naming is final output routing and must not require backend-specific interpolation changes. Existing compiler internal filenames may remain staging-only.
- **Gui:** three-format choice, final-step compact naming/folder/preview UI, collision dialog, paired progress/result display. Main wizard remains 5/7 steps. Do not expose backend names, scripts, advanced controls or two Build buttons.

## Shared snapshot and preparation

One accepted immutable input snapshot at Build start; read the original source exactly once into the existing bounded, reparse-rejecting snapshot implementation. Both prepared outputs must derive from those captured bytes. Never call existing single-output BuildAsync twice against the live source, and never recopy original source for the second compile. Compile sequentially from shared snapshot for simple ownership/resource limits; if portable staging consumes/moves payload, compile standard fully first and then move the captured payload into portable staging. A later copy may copy only this owned immutable snapshot, not the original source. Selected optional image bytes should likewise be captured once for the operation.

Prepare both fresh artifacts and validate nonempty files/hash/expected format before publishing either. Cancellation or compile failure at any preparation stage leaves both old final paths untouched. Keep cancellation responsive during inventory/copy/process stages. Progress text may name Installer/Portable package, using one indeterminate bar and no log window. No misleading determinate percentage in product UI.

## Two-output commit and recovery

This is a recoverable two-file transaction for handled errors, **not** atomic publication across power failure/process crash. Both targets must share a parent and be distinct under Windows case-insensitive canonical comparison. Apply existing source/state/tool/reparse containment checks to both paths and parent; forbid collision with executable/toolchain/owned staging and temporary filenames. Reject a directory at either target.

Prepare two unique sibling temporary files in destination directory and finish writing/flushing/closing them before commit. Preserve every prior target in a separately owned unique backup (including an explicit missing-target state). Revalidate target existence/identity against collision consent immediately before replacing: if a previously absent target appears or existing target changed, abort/reconfirm rather than overwrite unapproved new content. Do not offer a race-free guarantee against external malicious modification; keep the supported concurrency assumption explicit.

Once commit begins, defer cancellation until the bounded replace/rollback completes; do not stop halfway because a token was set. Commit both targets sequentially. On a handled failure after replacing the first, restore its old backup or remove only the exact newly committed artifact if the original was absent. Preserve the second original. Restore only operation-owned files; do not delete a concurrently replaced unrelated file. Check identities/hashes as needed before rollback mutation. If full rollback cannot complete, report partial result precisely and retain backups/recovery paths for the user. Never report success or discard the only old copy after a failed rollback.

After both replacements succeed, success result contains both final paths/hashes/sizes. Then remove owned backups and temp/staging; cleanup failures become explicit warnings, not false build failure or silent orphan cleanup claims. No broad directory deletion. Process/power loss may leave backups or one replaced output; document this limit and the recognizable recovery naming scheme. Do not call this pair atomic without qualification.

## Verification gates

- Pure naming tests for three styles × two modes, entered valid version, spaces/Unicode names, reserved/invalid/long names, stable extension and unknown style fallback. Distinct case-insensitive output names.
- Regression: existing standard-only and portable-only preparation/commit still produce correct artifacts and preserve old output on failure/cancellation.
- Batch meaningful test: mutate live source after first capture/compile and verify both prepared packages use the captured original bytes; no second original-source scan/copy.
- Real paired benign build with both backends, two nonempty final artifacts, recorded hashes and payload inventory. Verify portable custom/skipped images still work.
- Force second preparation failure and cancellation before commit: neither old destination changes. Force second publish failure after first replacement: restore first byte-identically and retain second byte-identically. Cover both preexisting and originally absent targets. Force rollback failure: preserve backup and return honest partial-recovery evidence.
- Collision consent tests: naming/mode/version changes invalidate consent; newly appeared/changed target cannot be overwritten using stale confirmation. No `.exe` output folder ambiguity.
- GUI default/minimum size, all three modes, naming previews/update, customized single filename, Both folder cancel, existing-target dialog cancel, one Build, two success paths and friendly failure. Reuse passing style/theme evidence when layout/control behavior is unchanged; review actual final screenshot where this layout changes.
- Independent auditor reviews final output transaction diff and actual completed evidence before signoff. No extra dependencies or original source execution required.
