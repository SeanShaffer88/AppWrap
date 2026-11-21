# Output naming and paired-build audit

Status: **APPROVED for local development preview**. Final source, fault tests, packed consumption, native GUI observations and strict final directory inventory pass. This supersedes the baseline-only delivery approval recorded in audit-final.md. No public release, installer lifecycle or accessibility certification is implied.

## Reviewed contracts and source

Reviewed approved `output-options-contract.md`, `PackageNaming.cs`, `NamingChecks.cs`, `BatchArtifactPublisher.cs`, `BatchBuild.cs` and the shared BuildService preparation extraction. Existing generator/portable/image contracts remain unchanged.

Positive source evidence:

- One pure Core naming function supplies three presets for the two backend formats, validates name/version/components, retains entered version and falls back safely for unknown styles. Backend OutputMode retains only two variants.
- BuildService rejects unknown backend mode before preparation; Both requires Standard then PortableApps, identical source/details/launch/shortcut inputs, same canonical parent and case-insensitively distinct destinations.
- Public immutable OutputIdentity captures before confirmation, is accepted through expectedOutputs and is verified at API entry and before commit. Existing-output overwrite defaults false. Explicit trusted-host bool fallback captures at build start.
- Original source is copied once into bounded owned payload. Both compiles standard first, then portable consumes the captured payload. Selected images inside source come from captured payload; external selected images are copied once with deduplication.
- Publisher prepares, closes and hashes both sibling temporary outputs before moving any existing destination. Existing destinations are moved to unique sibling backups; publication uses nonoverwriting moves. Cancellation is deferred through commit/rollback.
- On failure, rollback checks newly committed output hash before deletion, verifies backup hash before restore, and retains backups with explicit recovery diagnostics if rollback cannot finish. Success cleanup failure becomes a warning.

No confirmed material source defect was found in this early review. GUI must capture expected identities before its collision confirmation and avoid blocking the UI while hashing large old outputs. Final actual tests must cover enum/role boundaries, not only the naming helper's enum validation.

## Independent mutation evidence

Isolated copied publisher source, original Core/naming and minimal unchanged result/exception declarations were compiled into a scratch harness. It injects failure before the second replacement and checks that both existing destinations retain their original bytes.

- Baseline publisher: assertion passes, process/runner exit **0**, DevTools run `20261121T205955501Z-da990818c30b4b278d54fd8d03cf4a5f`.
- Invert only `committed[i] && File.Exists(...)` to `committed[i] && !File.Exists(...)` in copied rollback code: the same original-bytes assertion fails, expected process/runner exit **1**, DevTools run `20261121T205957567Z-ade3aceb73ee4a1193e32c6ae4b302f8`.
- Harness driver exits **0**, copied source restored and shared source never edited. Local result: `docs/audit-batch-mutation/result.json`. Scratch compiled files are not user-facing deliverables.

## Completed final evidence

- **169 checks pass**, actual process/runner exit **0**: DevTools run `20261121T210532017Z-1ab8367f71fa4af7a5dc9c75912aeaa1`; fixture evidence `work/AppWrap-verification/20261121T210532-4bde38`. Independently read fault assertions and actual output. Covers real paired compiles, same captured source bytes despite live mutation, external image capture and duplicate source-contained image capture despite later corruption; compiler/process/cancellation and baseline image checks; second preparation failure; cancellation before commit; enum/order/details/path/consent guards; existing/absent destination rollback; incomplete rollback retaining exact backup; foreign replacement preservation; and cancellation deferred through both commits.
- Requested **late destination mutation** is now exercised after `PortablePrepared`, not only before API entry. Expected identities are accepted first; the final precommit check then refuses changed external content and leaves both destinations intact.
- All four latest real packed artifacts pass a separate **13-check** consumer with isolated package cache, `work/AppWrap-packed-consumer/e96c8c066c67460a9b780c7f94a223b4/result.json` and `run-consumer.log`; result Passed true and recorded steps exit 0. Existing two-argument BuildAsync remains as a wrapper; explicit expected identity uses a separate overload.
- Independently viewed `outputs/AppWrap-both-filenames.png` and `outputs/AppWrap-both-icon-preview.png` at 680x560. Naming dropdown, two derived filenames, location, one Build both action and dense Appearance/Cancel footer are visible/readable without clipping or overlap. Selected icon thumbnail and Use default affordance are clear.
- Final GUI source updates filename previews in place instead of remounting the keyboard-active ComboBox. Capture/hashing and build service work run off the UI thread; collision identities are captured before the dialog and passed to the service. Mode/name/version/style changes invalidate a single-file override. Automation names are human-readable. Default window is 760x560 with 680x560 minimum.
- Published executable independently hashed: **131715247 bytes**, SHA256 **9D399153013225385D6635D45DC4930023ACACF8CE9552546B0BFF3B87253AA3**. Product manifest has **970 entries**, none missing; exact runtime license/notice files remain present. This existence/hash check is not the final strict package check.

## Final published GUI and artifact approval

- Independently viewed actual published `outputs/AppWrap-both-success.png`: Ready to share, two explicit output paths and Close as the sole remaining workflow action. `outputs/AppWrap-current.png` shows the final compact 760x560 default, three-mode selector and Appearance immediately beside Cancel; no overlap/clipping observed.
- Read `work/appwrap-paired-ui-result.json`: observed native success, actual portable **269192 bytes** SHA256 **97A3B22DFBB7D13236757CA6A645A033BBD57708ADD047BB7576B321275B0C79**, standard **2154366 bytes** SHA256 **C59D0AB946AB1A6FCDDE9CE58A521B6042B321467C8C8F339B6D978E46C6A199**. Independently rehashed both actual files and checked recorded sizes. Native UI tool provides no process exit code; `nativeUiExitCode` is correctly null and success is an observed UI/file result.
- Controller's native naming keyboard checks covered all three presets using Home/Down/Return and Alt+Down/End/Home/Return; both filename previews changed correctly, focus stayed on the mounted ComboBox and no unintended build occurred. One Build both action then produced both artifacts through the published GUI.
- Independently read `work/appwrap-both-package-after-ui.json`: **valid true**, **971 files**, **zero findings**. Controller captured static helper exit **0** after closing the tested app and preserving its generated Data outside the distributable. Published executable identity remains **9D399153013225385D6635D45DC4930023ACACF8CE9552546B0BFF3B87253AA3**.
- No confirmed material source issue remains. Source directory preview and runtime ZIP are approved for local-preview delivery.

## Final runtime archive

Independently hashed `outputs/AppWrap-0.1.0-preview-win-x64.zip`: **89476067 bytes**, SHA256 **590486C71A0100C26DC28E44699240419F55C489CC7AE3048D7C2FAB1BDEF64D**. Identity matches the producer's actual exit-0 archive result and `outputs/AppWrap-runtime-archive-verification.json`.

Read that report: **971 archive entries/files**, all **970 product-manifest file hashes/sizes verified**, safe contained paths, test state absent, no installers executed. The product manifest itself is the additional file. This reconciles the archive with the independently approved strict directory inventory; no repeated full rehash was needed. Local-preview artifact gate is complete. Source ZIP/repository handoff remains the controller's ordinary final exact-commit packaging step.

Two-file publication is recoverable for handled errors, not atomic across process crash or power loss. The baseline's unperformed installer lifecycle, clean-machine, settings isolation, signing, accessibility and public-release licensing gates remain explicit.
