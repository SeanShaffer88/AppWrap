# Independent AppWrap preview audit

Status: **baseline revision passed its local-preview audit**. The user-requested naming conventions and simultaneous dual-output revision has now passed the superseding independent audit in `audit-output-options.md`. That document is the final verdict for delivery. This is not public-release approval or installer lifecycle qualification.

## Reviewed scope and resolved findings

Reviewed Core, standard/portable generators, genuine root launch shim, build orchestration/process containment, imaging, GUI layout/control resources/themes/window state, backend patch/recipe and shipped notices. Earlier required findings are resolved: ISPP input rejection, Windows shortcut names, output/source/staging containment, whole-process/pipe deadlines, complete icons and selected alpha, bounded metadata/log reads, explicit retained-cleanup reporting, portable unknown-file preservation, apostrophe-safe shim compilation, navigation race guards, dynamic theme labels, safe window placement and linked-state guards.

No HTTP/socket/telemetry implementation was found in the AppWrap C# modules. The selected application and produced installers are not run during compilation. The launch shim forwards raw arguments, uses the selected executable's working directory and propagates its exit code; it does not provide registry/profile redirection.

## Actual verification evidence

- Final library harness: **58 passing checks**, process/runner exit **0**, `C:/Users/Sean Shaffer/Documents/DevTools/runs/20261121T204249694Z-38b1f934ebdf4dfeb80025df357de922`. Includes actual standard/default-portable/custom-artwork compiles, static PAF extraction and payload/companion retention, apostrophe path compilation, selected image alpha, bounded logs, nonzero exits, active cancellation, descendant-held pipe deadline, source preservation and normal stage cleanup.
- All four actual packed library artifacts consumed by a separate project and isolated cache: **10 passing checks**, every recorded step exit **0**; `C:/Users/Sean Shaffer/Documents/Codex/2026-11-21/c/work/AppWrap-packed-consumer/09cbe207688e470584902e779bf4eb77/result.json` and `run-consumer.log`.
- Independent mutation: original copied generator rejects `{#...}` (exit **0**); removing only that guard makes the same assertion fail (expected exit **1**). Isolated copy restored; shared source untouched. Evidence `docs/audit-mutation/result.json`, DevTools runs `20261121T203139275Z-f6934b3272c64006990fff7167ec5e42` and `20261121T203140898Z-4b60ec450b424dc99c6e5808d83b950b`.
- Final solution build: exit **0**, zero warnings/errors, `C:/Users/Sean Shaffer/Documents/DevTools/runs/20261121T204559827Z-374c510b4c7e42e9892036e2368d0b9d`.
- Final self-contained win-x64 publish: exit **0**, `C:/Users/Sean Shaffer/Documents/DevTools/runs/20261121T204608776Z-34e599b389cb45e88d64d09716112be3`. Published AppWrap.exe is **131686575 bytes**, SHA256 **48CCA40C76F125CDF88CFB0A01164DA62B365A1EA3AC6E4F13086EC6DE5486B0**.
- Read the final manifest's **970 entries**, with no missing paths. This independent existence check does not replace the controller's full hash/static package check.
- Three runtime license/notice files under `licenses/dotnet-runtime` and `licenses/windowsdesktop-runtime` exactly hash-match installed **10.0.15** runtime pack originals. Inno compiler and modified builder hashes match their recorded provenance. Actual modified/original wizard sources, decoded text patch, license and preparation script accompany the tool.
- Implementer's independent backend recipe reproduction reports exit **0** and the exact same modified builder hash; source and recipe inspected. DevTools run `20261121T203900834Z-b1a685f228e046f4ad06d18396305e79`.

## Limits

No produced installer or payload was run. Real install/upgrade/uninstall, application-specific settings migration/isolation, clean-machine compatibility, physical mixed-DPI behavior, spoken Narrator qualification, code-signing trust and trademark clearance remain unperformed. Portable generated configuration prevents deletion of unlisted App/Other/Data files; matching shipped file paths may still be replaced and need application-specific validation.

This preview retains the explicitly documented pending review of exact transitive NSIS/plugin/extractor source-distribution obligations. Upstream URLs and a bundled binary archive are not asserted to prove complete corresponding source or a valid source offer. Public release requires that separate gate.

## Final GUI/package gate

- Independently viewed `outputs/AppWrap-final-dense.png` and `outputs/AppWrap-final-details.png`, captured from the actual published app at 680x560. Header, question, picker/name/version and footer are visible/readable; Appearance sits immediately beside Cancel; Back/Next remain separate. No clipping/overlap/ordinary-content scrollbar observed in these states.
- Controller's native custom executable-popup visual and Escape/Home/Down/Return/Alt+Down checks passed; earlier theme visuals cover the same unchanged semantic brushes. These observations do not establish Narrator or high-DPI qualification.
- Independently read `work/appwrap-final-package-check-compatible.json`: **valid true**, **971 files**, **zero findings**, actual AppWrap executable **x64**, artifact hash unchanged. Controller captured helper exit **0**. The helper-compatible external allowlist includes all artifact hashes plus the product manifest's own hash. The initial attempt using unsupported rich manifest fields returned InvalidManifest and is not counted as a pass.
- Independently viewed `outputs/AppWrap-final-success.png`: actual published GUI shows READY TO SHARE, output path and Close as its sole workflow action. Produced standard installer `work/AppWrap-ui-e2e-setup.exe` is **2154362 bytes**. Controller observed end-to-end GUI build completion and repeated strict package validation after preserving generated UI state outside the artifact.

## Superseding user-requested work

The baseline gates above passed. The later naming-selector and simultaneous standard/portable-build revision is separately approved for local preview in `audit-output-options.md`, with final 169-check results, 13-check packed consumption, targeted rollback mutation, actual published-GUI dual success and strict final artifact inventory. Its documented runtime/public-release limitations still apply.
