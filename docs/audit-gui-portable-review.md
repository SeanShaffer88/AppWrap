# GUI and portable boundary review (intermediate)

2026-11-21. Stable source read while integration proceeds; final tests/artifact notices are pending and are not judged absent here.

## Required findings sent to Implementer

- Gate asynchronous folder discovery/navigation against repeated Next/Enter or folder/mode changes. Current `NextClick` awaits inventory without a navigation guard then increments the mutable step; concurrent invocations can skip questions. Cache inventory for render instead of scanning up to 50,000 entries synchronously again on step 3.
- `Label()` captures a concrete Muted brush. Switching theme leaves current labels in the old palette, potentially unreadable on a light background. Use a dynamic resource reference.
- The centered Appearance selector can overlap Back/Next near the 680px minimum window width. Use nonoverlapping responsive footer columns and verify actual minimum-size UI.
- Window restoration checks the virtual-screen bounding rectangle; real multi-monitor arrangements can contain gaps in that rectangle. Select an actual monitor work area and ensure the titlebar/window is visible.
- `Data/window.json` saving must reject reparse ancestors/file and use an atomic write; current direct write can leave the declared portable-state boundary via a link.
- Honor the architecture's Windows high-contrast fallback. Current ThemeCatalog only applies fixed palettes. Verify actual hover/focus/disabled control states with native templates; custom foreground colors may clash with stock light hover backgrounds.
- Read existing appinfo under a byte bound before allocating its full text. Current generator character limit is reached only after unrestricted `ReadAllTextAsync`.
- Read backend-generated logs under a bound and mark truncation.
- `docs/portable-wizard.patch` currently records only binary files differ because source encodings differ; emit a reviewable text patch from decoded matching encodings while retaining exact upstream/modified sources. Assert required recipe substitutions before compiling the modified builder.

## Positive observations

- Primary GUI is backend-neutral; raw errors are behind Show details. Build is the final next action, success offers Close, and closing during a build requests cancellation.
- Portable mode explicitly says the input must already support portable use and that packaging does not relocate settings/dependencies.
- Source custom `.nsh`/`.nsi` under Other/Source is rejected before builder invocation. Existing installer.ini is overwritten with allowlisted safe configuration; metadata serialization rejects control characters, dollar variables/macros, quotes and backticks in retained fields.
- Inspected modified builder config emission and includes: user-retained metadata is emitted through known fields; fixed compiler templates use relative package paths. No additional confirmed NSIS-injection path through currently retained metadata was found.
- Modified wizard has a dated change header, separate AppWrap Packaging Builder product identity and executable name. Origin and full license/source completeness remain artifact-review gates.
- Themes are native semantic brush dictionaries and introduce no framework/download/network dependency.

No final sign-off until targeted corrections and final source/evidence are reviewed.
