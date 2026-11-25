# Portable profiles — qualification record

This record states, per phase and per capability family, what the portable
profiles work (P0–P6, `docs/portable-profiles-contract.md`) actually
implements and what evidence stands behind it. It is deliberately explicit
about what is **not** proven: nothing in this record claims a Windows run
that did not happen.

## Evidence vocabulary

- **Verified** — exercised by an automated check that ran and passed in the
  environment named.
- **Configuration validated** — the configuration model, validation,
  planning, serialization and build-boundary logic are implemented and
  covered by the automated harness on a non-Windows host; no Windows
  compiler or runtime was involved.
- **Unverified** — not exercised at all in the available environment; the
  reason is stated per item.

Environments used for the evidence below: the full solution builds clean
(warnings as errors) under `EnableWindowsTargeting` on a Linux host with
.NET SDK 10.0.100, and the net10.0 harness that file-includes the Core
PortableProfiles sources, the portable Generators sources and the real
`*Checks.cs` files passes in full. The full Windows `AppWrap.Tests`
executable (which performs real installer compiles) was **not** run for
this work.

## Per-phase record

| Phase | Deliverable | Status | Evidence |
|---|---|---|---|
| P0 | Lossless INI AST, typed profile schema v1, pinned `EngineLock`, capability catalog, strict validator diagnostics | Implemented | Configuration validated |
| P1 | Real PAL path: launcher generator, exact compiler argv, hash-verified `PalCompiler`, backend preparation script and provenance | Implemented | Configuration validated; backend preparation Verified (see below); PAL compilation Unverified |
| P2 | Typed launcher rule views (moves, registry, environment, FileWrite, language) | Implemented | Configuration validated |
| P3 | Immutable operation planner with cross-rule diagnostics and planned host-backup locations | Implemented | Configuration validated |
| P4 | Installer surface: typed installer/metadata views, guard-safe merge, guard-order-blocked families | Implemented with deliberate blocks (below) | Configuration validated |
| P5 | WPF portable-profile workspace in the wizard (categories, rule grid, row editor, live INI text, diagnostics, New/Load/Save) | Implemented with approximations (below) | Configuration validated for the Core workspace state; WPF layout/interaction Unverified |
| P6 | Generated source/license bundle | Implemented (this phase) | Configuration validated |
| P6 | Host runtime fixture suite | Not run | Unverified |
| P6 | Guard variants + PAL cleanup waits at runtime | Not run | Unverified |
| P6 | Independent final review | Not performed | Unverified |

Backend preparation evidence (P1): the official PAL 2.2.9 archive was
downloaded from portableapps.com and its SHA-256 matched the pinned value
in `EngineLock.Pinned`; it was statically extracted with 7-Zip (the archive
was never executed) and the bundled `makensis.exe` SHA-256 matched the pin.
That verifies the pinned backend's identity and layout. It does not verify
compilation: `makensis.exe` is a Windows binary and was never run here.

## Per-family record

### Launcher families (launcher INI)

Launch/variants, arguments/working directory, OS bounds, elevation,
process coordination and waits, temporary data, splash, shell/console,
location behavior, Activate (Registry/Java/XML), LiveMode, environment,
RegistryKeys, RegistryValueWrite, RegistryValueBackupDelete,
RegistryCleanupIfEmpty, QtKeysCleanup, FileWrite, FilesMove,
DirectoriesMove, DirectoriesCleanupIfEmpty, language and language-file
families: typed views, validation and planning are implemented —
**Configuration validated**. Runtime behavior of every one of these
families (the compiled PAL launcher actually moving/restoring host state)
is **Unverified**.

Deliberately not buildable, by design and visible in the surface:
RegistryCleanupForce and DirectoriesCleanupForce require the profile's
explicit force-cleanup acknowledgment; an explicit registry view
(32/64/Both) has no stock PAL key and remains Unsupported in the catalog;
Service entries are disabled in the pinned PAL source and remain blocked;
deprecated/obsolete keys (e.g. LiveMode `CopyData`) are diagnosed, not
silently honored; unknown keys are Unrecognized and block a build until
removed.

### Installer families (installer.ini / appinfo.ini)

Preserve/remove lists (10-item upstream limit, overlap and `Data`
protection), [MainDirectories], optional components, installer languages
(English required, unknown keys warn), and metadata validation (AppID
conflict, package-version shape): implemented — **Configuration
validated**. The generated installer.ini is a deterministic merge in which
the trusted UpgradeGuard's settings always win (`GuardSettingOverride`).

Blocked with `GuardOrderBlocked`, typed and discoverable but not
buildable: **DownloadFiles, CopyLocalFiles, LegacyVersions and plugin
mode**. These families can act before the existing PreInstall guard, and
the reviewed earlier-safe ordering (the internal `AppWrapPreMutation`
gate in the generated installer, contract §"P4 early internal mutation
gate") does not exist yet. Download URLs and SHA-256 hashes are still
configuration-checked while blocked. Runtime install/upgrade behavior of
all installer families is **Unverified**.

### Generated source/license bundle (P6)

Implemented in `AppWrap.Build/PalSourceBundle`: a Pal package carries,
under `Other/Source`, the corresponding launcher source copied from the
prepared pinned backend (launcher script, top-level `.nsh` includes,
`Segments/`, `Languages/`, `Plugins/`), the package's generated launcher
INI / merged installer.ini / appinfo.ini, a build recipe (pins, hashes,
source URLs, backend preparation command, exact compiler argument array,
per-file SHA-256 inventory), the GPL v2 text copied from the backend's
`Other/Source/License.txt`, the NSIS `COPYING` and the NewTextReplace
readme where the backend ships them. The bundle writer fails the build
loudly, before writing anything, when a required backend source item is
missing; the one exception is the GPL text file — if a backend lacked it,
the notices state where the GPL text lives upstream rather than
fabricating license text (the pinned 2.2.9 backend does ship it). The
established `Other/AppWrap-PAL-NOTICES.txt` path is retained and now
points at the bundle. Evidence: **Configuration validated** (fake-backend
checks cover layout, hashes, determinism, the loud-failure contract and
the GPL fallback; no real package was produced, because that requires the
PAL compile below).

Plugin inventory note: the pinned backend ships no separate license or
readme file for the UAC or XML plugins; the bundle inventories their
binaries with hashes and says so. Per the contract, a missing source or
notice for a required component blocks a claim of ready **public
redistribution** of a generated package; it does not block local proof of
compilation. No redistribution-readiness claim is made here.

## What remains Unverified, and why

1. **An actual PAL compile.** `makensis.exe` is a Windows binary; it was
   never executed in this environment (no Windows host, no Wine). The
   compiler adapter, argument array and hash pin are configuration
   validated only. Consequently no real PAL package — and therefore no
   real bundle inside one — has been produced.
2. **The host runtime fixture suite** (contract fixture matrix: redirected
   APPDATA, file/directory moves, registry save/restore with absent and
   preexisting host subtrees, discard-only keys, guarded value writes,
   empty vs force cleanup, INI/XML writes and relocation, environment
   ordering, language fallback, variant selection, multi-instance and
   delayed children, abrupt PAL loss and recovery markers, read-only /
   denied / stale-backup conflicts, Both-mode failure/cancel/rollback).
   It needs a Windows machine running real portable apps against owned
   fixtures; none was available.
3. **Guard variants and PAL cleanup waits at runtime** — the UpgradeGuard
   closing configured executable variants and waiting for the PAL root to
   exit naturally after post-cleanup can only be proven on Windows with
   live processes.
4. **WPF layout and interaction at 680×560** — the workspace compiles
   under EnableWindowsTargeting and follows the contract's layout rules
   and existing themes (including high contrast), but minimum-size
   rendering, paging behavior, keyboard flow and theme rendering were
   never observed on Windows.
5. **Independent final review** — no reviewer independent of the
   implementers has passed over the P0–P6 work.

## Deliberate limitations on record

- P4 blocked families (above) stay blocked until the `AppWrapPreMutation`
  internal gate is implemented and qualified; they are not silently
  dropped from profiles — they surface as blocked rows in the workspace
  and as error diagnostics at build.
- P4: a profile's metadata document is validated but does **not** replace
  the generated `appinfo.ini`; `PortableAppsGenerator` output stays
  canonical (reconciliation is a workspace concern, per the P4 design).
- P5 approximations: there is no separate row-edit dialog for verbose
  XML/download/legacy entries (they edit inline or as text; download and
  legacy stay blocked regardless), and the rule grid's page size is fixed
  at 3 rather than adapting between 2 at minimum height and 3–4 when
  space permits.
- Imported profiles never grant trust: a `trusted` flag in a file is
  inert; only the explicit in-product action (`SetExtensionTrusted`)
  trusts an extension, and saved profiles never serialize trust.
- Packaging-only packages (no profile) are unchanged by P0–P6 and keep
  the basic launch shim and the unmodified-installer adapter.

## Raising the evidence

Each Unverified item names its missing environment: a Windows host with
the pinned backend prepared by `tools/Prepare-PalBackend.ps1` (for the
PAL compile and a real bundle), owned runtime fixtures (for the fixture
suite and guard/cleanup waits), a Windows desktop session at 680×560 (for
the WPF checks), and an independent reviewer (for the final review).
Until those run, the honest summary of the portable-profiles feature is:
**Configuration validated; compilation, installation and runtime
portability Unverified.**
