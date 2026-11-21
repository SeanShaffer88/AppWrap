# Build and imaging review (intermediate)

2026-11-21. Review of current Core, InnoSetupGenerator, BuildService, ProcessRunner and ImageAssets. Portable implementation and GUI integration remain in progress and are not judged as missing functionality here.

## Required findings sent to Implementer

1. **Process deadline must cover redirected stream completion.** `ProcessRunner.RunAsync` bounds only `WaitForExitAsync`. Once the root process exits, `await stdout` / `await stderr` can wait forever if a surviving child inherited the pipe handles. Cancellation/timeout branches also await both readers without a deadline. Bound the complete process-and-drain lifetime, close/cancel owned read handles when the deadline expires, and accurately report uncertain child cleanup. Add a test whose root exits while a child holds a pipe.
2. **Reject source/staging overlap before creating staging.** `BuildService` creates `stateDirectory/Builds/<id>` before copying the source. If source is an ancestor of stateDirectory (for example selecting a portable AppWrap installation folder), the copier encounters its own staging subtree and recursively copies it until the depth/budget limit. Refuse this relationship before creating build work or choose staging outside the selected source with verified ownership.
3. **Protect the running tool and compiler from output replacement.** `InputRules.Validate` only rejects output inside source. A destination naming AppWrap runtime or bundled backend files can pass and be replaced by `File.Move(... overwrite: true)`. Build orchestration knows baseDirectory/stateDirectory and must reject protected tool/runtime/staging output targets.
4. **Complete current portable icon set.** `ImageAssets.WriteIcons` currently produces 16/32/128/256 PNG companions and six DIB ICO entries. Architect's current-format contract additionally requires 75px companion and 256px PNG-compressed ICO entry. Verify actual asset contents and official-builder acceptance.
5. **Preserve transparent icon input.** `Fit` paints white for every icon frame; this makes transparent source pixels opaque. Use transparent icon rendering, retain 32-bit alpha/PNG alpha, and set the 8-bit DIB AND mask for transparent pixels. Sidebar white background is appropriate for an opaque 24-bit BMP.

## Positive observations

- Prior inline preprocessor injection and shortcut filename findings have targeted guards in Core and Literal.
- Build copies source under file-count/depth/cumulative-byte budgets, checks cancellation between reads, and never starts the selected payload.
- Standard compiler arguments use `ArgumentList`, no shell, hidden process, and disable inherited signing configuration.
- Fresh unique stage and expected output existence check prevent stale successful-artifact confusion.
- Final output is copied to a unique sibling file and moved only after hashing; ordinary compiler failure/cancellation before commit preserves old destination.
- Redirected streams are drained concurrently with bounded retained text, although a truncation marker and whole-lifecycle bound remain needed.
- Sidebar uses 164x314 crop and Bgr24 BMP; basic six DIB icon entries contain correct dimensions, palette calculation, row padding and bottom-up rows.

## Pending evidence

Actual tests/compilation/publish, cancellation and failure artifact preservation, process orphan/deadline tests, full icon parser and transparent-pixel tests, source/stage cleanup inventory, final portable hook behavior, GUI flows and final diff review. This intermediate review is not approval.
