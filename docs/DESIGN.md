# Design conformance — FluentGruv

AppWrap's wizard conforms to **FluentGruv v1.3.0**, SEAN's design system
(Gruvbox + bespoke red Fluent language):
<https://github.com/SeanShaffer88/FluentGruv>. Token values are taken from
that repo's `tokens.json` (machine-readable companion to `TOKENS.md`);
geometry, focus, disabled-state and typography rules come from `TOKENS.md`
§§2–4 and `SPEC.md`. Nothing is approximated silently: every mapping that
is not 1:1 is listed below.

## Themes

`AppWrap.Gui/ThemeCatalog.cs` carries seven palettes. **FluentGruv** (dark)
is the default theme for fresh installs and the design AppWrap is judged
against; **FluentGruv Light** is the same token set with the spec's light
values. The other five (AppWrap, Catppuccin Mocha, Nord, Gruvbox Light,
OLED Black) remain selectable in the Appearance list — user choice is kept,
and a saved choice from an older version still restores. Windows
high-contrast mode still overrides everything with system colors.

## Token mapping (FluentGruv token → AppWrap resource)

| FluentGruv token | Resource key | Used for |
|---|---|---|
| `bg.canvas` | `Canvas` | Window background (the spec's solid fallback for the Mica base) |
| `bg.surface` | `Surface` | Panels and cards |
| `bg.elevated` | `Elevated` | Floating surfaces (combo-box popup) |
| `bg.sunken` | `InputSurface` | Input wells, cards, tracks |
| `fg.primary` | `Text` | Primary text |
| `fg.secondary` | `TextSecondary` | Secondary labels (header subtitle, step topic) |
| `fg.muted` | `Muted` | Hints, captions — and disabled text at full strength |
| `fg.inverse` | `AccentText` | Text on filled accent |
| `accent.primary` | `Accent` | Filled accent: primary button, checked choices, selection |
| `accent.primary` | `StepAccent` | Focus rings (2px solid) and step indicators |
| `accent.secondary` | `AccentPressed` | Pressed state of accent-filled controls |
| `accent.subtle` | `AccentSubtle` | Selected-row background (profile rows) |
| `border.subtle` | `Border` | Hairline dividers, control frames |
| `border.strong` | `BorderStrong` | Hover-strengthened frames |
| `status.success` | `Success` | Passing test verdict |
| `status.warning` | `Warning` | Non-fatal warnings (e.g. cleanup warning) |
| `status.danger` | `Error` | Errors and failing verdicts |

## Geometry, typography, states

- **Corner radius** (`TOKENS.md` §2): 4px on standard control faces and
  cards, 2px on compact surfaces (combo-box item highlight). Focus-ring
  wrapper borders use 6px — the 4px face radius plus the 2px ring — a
  geometric consequence, not a spec token.
- **Spacing**: control-style padding sits on the 4/8px base grid
  (`TOKENS.md` §2). Window-level margins in code-built panels are dense
  but not every one is a multiple of 4; see Approximations.
- **Typography** (`TOKENS.md` §3): UI font is `Segoe UI` (the spec's first
  choice). Paths, logs, metrics and the output readout use the spec's mono
  stack `Consolas, Cascadia Mono, JetBrains Mono`. The spec defines no
  font-size scale, so existing sizes are unchanged.
- **Focus**: every templated control draws a 2px solid `accent.primary`
  ring (`StepAccent`) with an offset gap, per `TOKENS.md` §2.
- **Disabled**: disabled controls set their text to `fg.muted` at full
  strength instead of fading the whole control to 50% opacity, per
  `TOKENS.md` §4.
- **Hover**: hover strengthens the frame to `border.strong`; the spec
  tokenizes no hover color, and `border.strong` is its "focused container
  frames" value — the nearest token.

## Approximations and non-1:1 decisions

- **Materials**: the window uses solid `bg.canvas`. That is the spec's own
  fallback for `material.mica` ("solid `bg.canvas` when Mica is
  unavailable"); Mica/Acrylic backdrops are not enabled, so the design is
  identical on Windows 10 and 11.
- **Alternate palettes** predate the extended resource slots; their
  `Canvas`/`Elevated`/`TextSecondary`/`BorderStrong`/`AccentSubtle`/
  `AccentPressed`/`Success`/`Warning` values reuse each palette's own
  nearest existing color, so the alternates render as they did before.
  Only the two FluentGruv palettes carry true spec values for every slot.
- **"Gruvbox Light"** is the legacy pre-FluentGruv light palette (blue
  accent), kept under its existing name so saved preferences keep
  restoring; it is *not* the FluentGruv light palette — that is
  "FluentGruv Light".
- **Pressed states** of non-accent controls keep the existing opacity
  change; `accent.secondary` is applied where a pressed surface is
  accent-filled (checked check-box, checked mode choice).
- **Tabular figures**: WPF text in these styles has no tabular-figure
  switch; numeric readouts that need alignment use the mono stack, which
  is inherently tabular.
- **Installer**: `tools/AppWrap-Setup.nsi` embeds no color values or
  sidebar bitmap (only the app icon, which is already in-family), so
  there is nothing to align there.
- **Rendering**: WPF rendering of the conforming design on a real Windows
  host is Unverified; conformance is enforced by the source-level checks
  in `AppWrap.Tests/ThemeChecks.cs`, which pin every FluentGruv token
  value, the default-theme wiring, the App.xaml startup brushes, the
  no-hardcoded-colors rule and the radius scale.
