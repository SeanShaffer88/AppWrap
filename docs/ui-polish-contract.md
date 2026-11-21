# AppWrap UI polish contract

Controller relayed explicit user request to beautify the GUI, and abandonment of TesselGUI conformity. This is a visual revision authorized for implementation; it does not reopen backend contracts. Existing Original/AppWrap, Catppuccin Mocha, Nord, Gruvbox Light and OLED Black themes remain.

## Visual direction

Calm, polished native Windows utility. The current question and its picker/input dominate. Use Segoe UI Variable when installed with Segoe UI fallback, native title bar and resize behavior. No forced wallpaper texture, dashboard panels, web landing-page motifs or invented operational status. No remote fonts, artwork downloads, telemetry or animated decoration.

Default window remains approximately 880×620 DIP; ensure current minimum 680×560 DIP fits without clipped content. Header/main/footer have one shared horizontal inset: 32 DIP default, 24 DIP at minimum if needed. Use 8 DIP rhythm with 16/24/32 spacing and limited 10–14 DIP corner radii. UI is resizable: main question and hints wrap; picker paths have bounded presentation with full accessible value/tooltip; footer never collides or goes offscreen.

## Layout

1. Header: original vector package mark in a 40–44 DIP square, AppWrap name at 24–26 DIP semibold, short tagline at 13–14 DIP muted. On the right a compact rounded two-option format selector built from actual RadioButtons with clear checked state. Use real native semantics; it is a format choice, not navigation tabs. Collapse to a second header row at narrow width if necessary rather than overlap.
2. Step cue: persistent text `Step n of 5/7` plus restrained nonclickable progress/number indicators. Past/current/upcoming states are distinct and current has a text cue. This is progress only; no clickable side navigation, completed-step jump, or additional question.
3. Main question: 27–30 DIP semibold, wrapping; helper text at 14–15 DIP, at most a few short lines. Primary input below in a single softly differentiated rounded card, minimum 44 DIP input/button height, generous whitespace. No redundant cards around every label.
4. Picker cards: original native vector folder/file/image glyph, concise chosen filename/path and useful empty prompt, `Choose folder`/`Choose image`/`Save as` action. Selected path stays readable in every theme. Optional image steps show the actual selected image preview and a clear default/skip affordance with no warning. Preview is descriptive, never interpreted as validated output.
5. Fields: visible labels, 8 DIP between label/control, full width where helpful; app name/version can use two proportional columns if minimum width permits. Executable selector supports keyboard search/selection. Shortcut options are actual CheckBoxes with consistent spacing and visible checked/focus states.
6. Footer: action row with Cancel left and Back + Next/Build right. Appearance control lives in a quieter second row or small left secondary cluster; it must remain available without competing with the primary action. Final question still exposes Build directly, with no summary/confirmation page. Success shows actual result and Close; no extra project/script/test controls.

## Native control styles

Theme/resources worker owns reusable control resource dictionary and a preview artifact only, under controller-assigned paths. GUI implementer owns MainWindow markup/code and dynamic page content. No overlapping file ownership. Wire shared keyed styles explicitly so the independently created resource file cannot accidentally override unrelated controls.

Buttons, inputs, selectors, CheckBoxes and RadioButtons share semantic brushes and consistent radius/border/padding. Provide normal, hover, pressed, disabled, keyboard-focus and checked/selected states. Use existing `Surface`, `InputSurface`, `Text`, `Muted`, `Accent`, `AccentText`, `StepAccent`, `Border`, `Error` keys; new semantic tokens must be initialized for every theme and high contrast, without stale fallback colors.

ComboBox requires a themed Popup plus item container, not merely Foreground/Background on the closed control. Popup surface, selected/hover text, border, scrolling and disabled visuals must match the current theme and remain readable in Gruvbox Light/OLED Black. Preserve WPF `PART_Popup`, `PART_EditableTextBox` where relevant, keyboard arrow/Home/End/type search, Alt+Down/F4 open, Escape close, Tab traversal and SelectionChanged semantics. Do not synthesize a menu from Borders or lose automation names. Theme switching while popup is open should update live resources or close safely and reopen consistently.

Original vector logo is an AppWrap package motif, drawn with WPF Geometry/Path and theme brushes; do not use emoji or upstream trademarks. Decorative paths are not keyboard focus targets and should not duplicate screen-reader announcements. No custom borderless title bar or window chrome in this pass.

## Feedback and accessibility

Retain existing automation IDs/event handlers, labeled inputs, default-button behavior, error live region and Show details only on failure. During build: actual indeterminate progress and plain status, disable input navigation, leave cancellation responsive. Error uses a symbol/text cue as well as color, wraps with an actionable message and never exposes raw backend names outside details. High contrast uses Windows system brushes and visible outlines/focus. Avoid decorative transitions; any subtle hover treatment must not create perpetual timers/idle animation. Do not claim accessibility certification from these checks.

## Acceptance checks

- Show a reviewable mockup/preview of the changed design; obtain no extra approval gate for the already-requested polish.
- Real WPF build passes; launch actual packaged app and capture source, information, executable, optional-image, final/build and failure/success representative states.
- Observe all five themes, including actual open appearance/executable ComboBox popup and keyboard focus. No black-on-black, white-on-white, clipped text or legacy bright popup in dark themes.
- Check default/minimum size plus one resized larger view; paths, wrapped hints/errors, footer and theme control remain visible. Dynamic question layout does not require scrolling for ordinary inputs. Long diagnostics may scroll inside disclosure.
- Keyboard navigation and file-picker cancellation preserve existing workflow and input. Theme choice/window bounds persist. Backend generation/compilation tests need rerun only if their inputs/behavior changed; UI-only changes do not invalidate passing backend artifacts.
- Independent reviewer compares exact final UI diff and screenshots against this contract; report any unperformed DPI/Narrator/clean-machine checks accurately.

## Skill grounding

Activated `C:\Users\Sean Shaffer\.codex\skills\ui-ux-pro-max\SKILL.md`: prioritize contrast, keyboard navigation/visible focus, labeled errors, consistent semantic tokens and native vector icons. Design-system query `desktop utility guided wizard calm` returned a wellness/web landing-page recommendation, rejected as off-topic; narrowed UX query used for keyboard/form guidance. This contract's WPF design is a task-specific fallback, not a verified stack recommendation from the search catalog.
