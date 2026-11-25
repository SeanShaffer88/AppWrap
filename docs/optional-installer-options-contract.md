# Relevant creator options and standard completion choices

Status: user requested all relevant options be available and clearly optional, and clarified startup/website finish choices apply to standard installers only. Controller approved the revised Standard6/Both8/Portable7 layout after the revised mockup link was shown, plus the narrow silent-upgrade ownership migration below. Scope excludes unrelated publisher/licensing/engine controls. This extends the original simple-wizard requirement; no raw directives, Advanced Mode, project editor or backend names are exposed. No new user approval pause is required.

Mockup: `C:\Users\Sean Shaffer\Documents\Codex\2026-11-21\c\outputs\AppWrap-optional-controls-mockup.html` (self-contained, no remote assets/scripts). It shows Both-mode creator choices and three optional completion choices. It is a visual artifact, not installer execution evidence; actual native setup appearance remains native.

## Audit: meaningful knobs and fixed safeguards

| Behavior | Current source evidence | Decision for this change |
|---|---|---|
| Format | Installer/Portable/Both choice | Keep all three |
| Source/name/version/executable | Required wizard inputs | Keep required; never label required input optional |
| Start Menu/Desktop | Creator booleans; generated Icons omitted when false | Offer both as optional installer Tasks with creator-selected defaults; Start Menu on/Desktop off |
| Open after setup | Inno generator always emits checked postinstall Run | Expose optional creator toggle, default on; false omits choice |
| Start with Windows | Absent | Optional creator toggle to offer completion choice, default off; completion checkbox initially off on fresh install |
| Developer website | Absent for standard; portable metadata may preserve source homepage | Optional standard creator URL + offer toggle, default off; valid URL required only when offered |
| Icon/sidebar | Portable optional pictures with defaults | Keep explicitly optional; do not add standard appearance controls to this bounded change |
| Output/naming/custom filename | Existing presets and single Save dialog/Both folder previews | Keep all options; preserve collision-consent invalidation |
| Appearance/window placement | Five themes plus persisted window state | Keep compact Appearance beside Exit; no footer growth |
| Install scope/path | Per-user no elevation, stable app identity/path | Keep safe default; not an arbitrary install-script editor |
| Close/upgrade/data/output rollback | Safety contracts and tested behavior | Keep mandatory safeguards, not user-disable switches |
| Compression/signing/runtime architecture/backend versions | Fixed implementation/pinned toolchain | Do not expose raw backend controls; signing not implemented and cannot be advertised |
| Publisher/licenses/arbitrary homepage/actions/dependencies | Incomplete source metadata/rights evidence | Record future scope separately; do not add unrelated fields or invent rights |

The creator's shortcut controls are now **default selections offered to the installing user**, not absolute suppression. Label their group `Shortcut defaults (optional)` and helper explains the installing user can change them. Standard task names are stable (`startmenuicon`, `desktopicon`) and default flags mirror creator booleans. Existing single-format API callers retain the same selected defaults; the intentional behavior extension is that an initially off shortcut remains available to the installing user. Portable mode explains these choices are standard-installer-only and does not generate Windows integration.

## Creator UI and state contract

Keep step4 Shortcuts separate and compact. Add a new linear step5 Finish options in Standard/Both. Final counts: **Standard6, Both8, Portable7**. Both order: source, app details, launch file, shortcuts, finish options, icon, sidebar, save/build. Standard omits icon/sidebar; Portable omits finish options. Progress counts and back navigation must match the selected flow and retain inputs.

Finish-step visible labels explicitly say Optional. Controls: offer app launch (on), offer Start with Windows (off), offer developer website (off), website field labeled Optional. Website is enabled when offer checkbox is checked; blank otherwise is valid. Use the concise two-line helper shown in revised mockup. Both passes options only to its standard member; Portable has no editable standard integration settings.

The revised mockup has exact680×560 frames, separate shortcut/finish screens and a184px finish card within the approximately200px available page region. Use three compact checkbox rows, one concise website label and36px URL input. Actual WPF layout must prove fit and keyboard visibility; concise controls take priority over decorative padding. No scrolling, clipped controls or recombined tall card. Footer Cancel/Appearance/Back/Next remains one dense row and all five themes remain available.

Remember optional checkbox preferences only if using existing app-local settings schema with backward-compatible defaults. Do not globally persist another app's website as a next-project default. Website is in-memory input, cleared when choosing a new source/application; no implicit project file/save action. Existing theme/naming/window state remains compatible. Missing/invalid stored new preference values restore safe defaults. All build inputs become immutable together; Both still takes a single original-source snapshot.

## Typed public API and validation

Append one optional `StandardInstallerOptions?` value to `AppWrapInput`, preserving existing positional arguments/default behavior. Canonical options contain `OfferLaunchAfterInstall=true`, `OfferStartWithWindows=false`, `OfferDeveloperWebsite=false`, optional `DeveloperWebsite=null`. `null` standard options normalize to those defaults. Do not overload shortcut booleans, OutputPath, mode or image paths. BuildBoth accepts populated standardInput.StandardOptions and null/ignored portableInput.StandardOptions; it must not require the portable member to carry equal standard options. Core owns validation and normalized URL, GUI calls it and displays near-field errors; no duplicate loose URL check in generators.

Accept only absolute HTTP/HTTPS URLs with nonempty host, length bounded (e.g.2048), no username/password, CR/LF/NUL/control characters, raw quote/backtick or whitespace that changes parsing. Reject javascript/file/data/mailto/shell/custom schemes and relative/protocol-relative input. Parse using `Uri.TryCreate`, normalize through validated absolute Uri and escape as an Inno literal separately. Do not fetch, ping, resolve or open the URL while validating/building. Valid HTTP loopback URL is permitted for benign local browser-launch verification. An offered website with blank/invalid URL blocks the relevant creator step/build with a clear message; URL blank and offer off succeeds.

## Generated standard installer: shortcuts and completion

`[Tasks]` offers both shortcut choices with stable names and creator-selected checked/unchecked defaults. `[Icons]` entries use those Tasks, with existing correct selected executable parent as WorkingDir. Keep privilege scope lowest and current-user paths. Verify interactive install and `/TASKS` behavior instead of assuming task defaults from script text alone.

App launch remains native `[Run]` postinstall/nowait/skipifsilent, selected by default when creator offers it. Website uses a native `[Run]` entry with validated URL, `postinstall shellexec skipifsilent unchecked` (plus `nowait` if compatible with actual compiler), description `Visit the developer's website (optional)`. No browser opening in creator, during compilation, before completion, or in silent installs/upgrades. End user must explicitly select it on that completed install. Do not persist a previous website checkbox selection across runs.

Startup is a real **completion-page** checkbox, not an earlier Tasks-page substitute. Use the smallest generated Pascal code needed with supported `TNewCheckBox`/`CreateShellLink` APIs. Place it adjacent to native RunList with measured available space; do not insert arbitrary RunList items that the engine may treat as unbacked Run entries. Native RunList holds offered app/website actions; custom startup checkbox may follow them. Three-checkbox layout must be checked at actual DPI and minimum native installer size, with no overlap. No custom native helper/runtime dependency for shortcut creation.

On fresh install startup checkbox is unchecked. If an exact app-owned startup link already exists on upgrade, reflect its current enabled state. Only the actual Finish action applies the choice, before native app/browser Run actions: checked creates/updates owned link; unchecked removes only owned link. Checkbox clicks/page-show make no change. Foreign conflict with unchecked startup preserves it quietly and must not block unrelated Finish. Offer disabled or silent upgrade makes **no startup preference change**, preserving existing choice. Silent install creates no startup entry. Cancellation before Finish makes no startup change. A selected-startup shortcut failure shows a plain actionable message and permits retry/uncheck; never report startup success falsely. Failed Finish retry must not duplicate native app/browser actions.

## Parallel implementation ownership

Optional-options Implementer owns `AppWrap.Core/AppWrapInput.cs`, new focused Core options/URL validation file, `AppWrap.Generators/InnoSetupGenerator.cs` plus a focused startup-code generator if necessary, `AppWrap.Gui/MainWindow.xaml.cs` and its existing batch/state partial files as actually present, focused tests and optional-options documentation evidence. Coordinate BuildBoth validation edits in `AppWrap.Build/BuildService.cs` with controller; avoid overlap with portable owner. Portable/native Implementer retains `PortableBuild.cs`, launch/upgrade guard native source, PAF custom-hook generation, pinned builder and portable lifecycle artifacts. Do not touch each other's files or revert concurrent work. Both can run compilation/unit tests in independent build-output locations or coordinate shared builds; original Implementer keeps exclusive host UI/process-operation lease until explicit handoff.

Startup path is an exact collision-resistant app-owned `{userstartup}\AppWrap-<stable-id>.lnk`, target `{app}\<selected-exe>`, working directory selected executable parent, no arguments. Never use commonstartup/HKLM Run or change global startup settings. Before create/replace/delete, inspect existing shell-link metadata and compare canonical target, exact empty arguments and canonical working directory. Malformed/unreadable/reparse/foreign link means preserve it and report conflict. Unique filename alone does not prove ownership.

For a changed MainExecutable during upgrade, retain prior owned integration metadata through the same stable Inno uninstall/previous-data state (`GetPreviousData`/`RegisterPreviousData`), not a new global store. Before updating those values, capture prior installed root, target-relative executable and working-directory-relative path. Validate previous metadata resolves strictly within the prior owned installed root with no traversal, link ambiguity or arbitrary absolute executable target. An existing startup link is owned only if it matches either the current expected target/cwd/noargs or that validated previous expected target/cwd/noargs. This lets a new selected executable migrate an existing owned startup link while preserving an exact-name foreign replacement. Carry the current/last-owned tuple in the installed uninstall state for later ownership-checked cleanup. Tests must change MainExecutable path and preserve a foreign replacement. Do not constrain users to an immutable executable path.

Narrow silent-upgrade exception approved by controller: after files install, refresh only an **already existing, validated previously-owned** startup link's target/cwd to the new selected executable while preserving its enabled state. This is ownership migration, not a new startup preference. An absent link remains absent; foreign/malformed link remains untouched. No app/browser launches in silent mode. Apply this same safe existing-link migration when a later creator build stops offering the checkbox, so it does not strand an enabled link on a removed executable. Interactive Finish still controls explicit enable/disable choice; capture old metadata before registration replaces it, and ensure uninstall cleanup sees the final owned tuple. Test changed executable in both silent and interactive upgrades.

Track cleanup through exact `[UninstallDelete] Type: files` entry with an ownership `Check` function available during uninstall, or equivalent narrowly scoped generated uninstall code. The function must compare the link's current target and preserve any foreign replacement. No wildcards, directory deletes or startup-folder cleanup. Upgrade registration records correct latest cleanup; offer disabled later still leaves existing tracked owned startup link eligible for correct uninstall cleanup, without changing its enabled state during the upgrade. Existing settings/data preservation contracts remain intact. Record intentional foreign-link preservation as a diagnostic, not a license to delete it.

## Verification gates

- Core input defaults/backward compatibility, valid HTTPS/HTTP/loopback, blank-off, invalid-on, credentials/control/raw quote/relative/custom schemes, length and literal injection tests. No network during validation.
- Generate and actually compile standard combinations: launch offered/omitted, website offered/omitted, startup offered/omitted, both shortcut defaults. Portable output must not gain Run/startup/browser behavior; Both standard has options and portable remains unaffected.
- Revised exact680×560 mockup shown before edits; actual Standard6/Both8/Portable7 routing/back-retention and screenshots at default/minimum size and five themes where controls change. Optional labels visible; footer alignment unchanged; no clipped option or URL error.
- Interactive standard completion exposes up to three choices with correct defaults; unselected browser/startup cause no action. Website checked uses a harmless local URL observed by local test server/browser evidence, never an external communication site. Silent setup/upgrade opens neither app nor website and preserves startup state.
- Owned fixture tests: startup checked creates expected per-user link target/cwd/noargs; upgrade reflects existing owned link, unchecked removes it; upgrade changing MainExecutable migrates prior-owned target correctly; uninstall removes owned link while preserving user settings.
- Foreign/malformed link at exact expected filename is preserved by install, upgrade and uninstall. Baseline and hash/target evidence must show it remains untouched. Tests use only uniquely owned fixture links, never a real user's startup entry.
- Regression current naming/Both transaction, generated package lifecycle and independent Auditor review of exact final generated code and actual evidence. Broader DPI/Narrator/Windows-version checks remain honestly scoped.

## Sources and activated skill rules

- `C:\Users\Sean Shaffer\.codex\skills\ui-ux-pro-max\SKILL.md`: visible labels, optional/error clarity, contrast/focus and coherent semantic tokens; `docs/ui-polish-contract.md` remains visual baseline.
- `C:\Users\Sean Shaffer\.agents\skills\api-and-interface-design\SKILL.md`: additive public input, validation at boundaries, explicit predictable errors.
- https://jrsoftware.org/inno-setup/documentation/topic_runsection.htm — native postinstall/unchecked/shellexec/skipifsilent behavior.
- https://jrsoftware.org/inno-setup/documentation/topic_taskssection.htm — stable optional Tasks and unchecked defaults.
- https://jrsoftware.org/inno-setup/documentation/topic_isxfunc_createshelllink.htm — supported shortcut API and recommendation for exact uninstall cleanup.
- https://jrsoftware.org/inno-setup/documentation/topic_uninstalldeletesection.htm — exact file cleanup and explicit warning against broad deletes.
- https://jrsoftware.org/inno-setup/documentation/topic_consts.htm — userstartup scope.
- https://jrsoftware.org/inno-setup/documentation/topic_isxfunc_getpreviousdata.htm and topic_scriptevents.htm — supported previous-data lookup/registration and finish event hooks; note NextButtonClick also runs in silent setup, so explicitly guard integration actions.
