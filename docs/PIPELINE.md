# The four-stage conversion pipeline

AppWrap converts installers and archives into PortableApps format through
a four-stage pipeline, fronted by a self-bootstrapping installer. The
audit (`docs/AUDIT.md`) is the pipeline's evidence model; this document
describes the machinery around it: what is pinned, what runs where, and
what happens when a piece is missing.

| Stage | What happens | Where it is described |
| --- | --- | --- |
| 0 | AppWrap Setup bootstraps Sandboxie-Plus | This document |
| 1 | Ingestion: extraction engines unpack the source | This document + `docs/AUDIT.md` |
| 2 | Autonomous sandboxed profiling in Sandboxie-Plus | This document + `docs/AUDIT.md` |
| 3 | Configuration & asset synthesis (draft profile, splash) | `docs/AUDIT.md` + this document |
| 4 | Compilation (PAL / PAF) + Sandboxie leak verification | `docs/TEST-HARNESS.md` + this document |

## The bootstrap decision (SEAN, 2026-11-25)

AppWrap's standing rules are zero-cloud and no silent elevation. For
this feature SEAN explicitly overrode them, scoped to exactly this:
**AppWrap Setup silently downloads and installs Sandboxie-Plus** (with
its kernel driver) when it is missing or outdated, so the sandboxed
profiler has a consistent, isolated environment on any Windows machine
without relying on Windows Sandbox or OS virtualization features. The
override is recorded here and in `docs/ROADMAP.md`; it does not extend
to anything else — AppWrap itself still never phones home, and no other
component downloads anything at runtime. (The extraction engines below
are prepared at build time by a maintainer script, like every other
AppWrap backend; nothing fetches them on a user's machine.)

## Stage 0 — installer bootstrap

During setup, a dedicated section checks for Sandboxie-Plus and acts on
what it finds. The decision logic is factored into Core
(`SandboxieBootstrapPlanner`) so the installer script and the profiler
agree, and it is check-covered:

- **Detection** uses the official installer's own facts: the Inno Setup
  uninstall key `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Sandboxie-Plus_is1`
  (its AppId is "Sandboxie-Plus"), the `SbieSvc` service key under
  `HKLM\SYSTEM\CurrentControlSet\Services`, and `Start.exe` in the
  install folder (default `C:\Program Files\Sandboxie-Plus`).
- **Ready**: service + Start.exe present and the version is at or above
  the pinned floor (or unreadable — an install AppWrap cannot prove is
  old is left alone). Nothing is downloaded.
- **Install / upgrade**: the pinned Windows x64 installer
  is downloaded from the official sandboxie-plus/Sandboxie GitHub
  release, its SHA-256 is compared against the pinned value, and it is
  run with the Inno Setup silent form
  (`/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-`), which installs the
  service and the SbieDrv kernel driver. Windows shows its own UAC
  prompt for the driver install — that prompt belongs to the operating
  system and cannot be silenced by any installer.
- **Failure is plain, never fake**: a failed download, a hash mismatch
  (the download is refused), or a failed install leaves AppWrap fully
  installed and working; the outcome is written to the install log, to
  `Data\sandboxie-bootstrap.log` beside AppWrap.exe, and onto the
  setup's final page. The GUI sandbox action and an explicit CLI
  `--sandboxie` request stop with a clear error if Sandboxie is unavailable;
  the CLI's separate host runtime route remains explicit.

### The Sandboxie lock

One lock, in code: `SandboxieLock.Pinned`
(`AppWrap.Core/Pipeline/PipelineLocks.cs`). The NSIS defines at the top
of `tools/AppWrap-Setup.nsi` mirror it for compile time; update all
three places (lock, script, this table) together.

| Fact | Value |
| --- | --- |
| Pinned release | Sandboxie-Plus **1.18.8** (Classic core 5.73.8), 2026-10-17 |
| Release page | https://github.com/sandboxie-plus/Sandboxie/releases/tag/v1.18.8 |
| x64 asset | `Sandboxie-Plus-x64-v1.18.8.exe` |
| x64 SHA-256 | `f27078ff74a07906fd51476079e1a7a8f5e20c8bb75a9d2ae86169d47179ae76` — **Unverified**: independent mirrors compute this value for the file the official release serves, but the Sandboxie project publishes no hash on a source this lock can cite, so the pin records the value with its verified flag false. The bootstrap still refuses a mismatch. |
| Supported host | Windows 11 x64 |
| Silent form | Inno Setup: `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-` |

## Stage 1 — ingestion engines

Three extraction engines are pinned as **optional backends**, prepared
the same way as the PortableApps backend: a maintainer runs
`tools/Prepare-EngineBackend.ps1`, which downloads each official
archive (or takes local copies), verifies what can be verified, extracts
it *statically* (archives are never executed), and lays the result out
under `third_party/engines/{7zip,innoextract,lessmsi}` with a
`provenance.json` per engine. The lock is `ExtractionEngineLock`
(same file as the Sandboxie lock).

| Engine | Pin | Archive | Hash status |
| --- | --- | --- | --- |
| 7-Zip (Extra, standalone console) | 26.06 (2026-11-06) | `7z2606-extra.7z` from the ip7z/7zip GitHub release | No SHA-256 published on a citable source — Unverified; the prepare script records the observed SHA-256 in provenance |
| innoextract | 1.9 (2020-08-09) | `innoextract-1.9-windows.zip` from constexpr.org/innoextract | Official **MD5** `72d0d0dd874b6236eaa44411f4470ee1` published by the project and **verified** by the prepare script; no SHA-256 published — Unverified |
| lessMSI | 2.12.9 (2026-04-02) | `lessmsi-v2.12.9.zip` from activescott/lessmsi releases | No SHA-256 published on a citable source — Unverified; observed hash recorded in provenance |

Wiring (`EnginePlanner` + `StaticAuditor.AuditExeWithEnginesAsync` /
`AuditMsiWithEnginesAsync`, behind the `IExtractionRunner` seam):

- **NSIS** payloads and MSI wrappers → 7-Zip extraction.
- **Inno Setup** → innoextract (7-Zip cannot unpack Inno; innoextract
  1.9 covers Inno Setup up to 6.1.x — newer Inno formats degrade).
- **MSI** → the existing table inventory, complemented by a lessMSI
  payload extraction whose files go through the same classification.
- Extracted payloads flow through the existing folder inventory and
  finding classification unchanged.
- A missing engine, or an engine that cannot open the source, degrades
  to the pre-engine behavior — family identification plus an explicit
  static-audit-limited finding. Never an error, never a guess.

CLI: `audit <source> --engines`. Engine-backed static extraction remains
a CLI concern; the wizard keeps its static pass read-only.

## Stage 2 — sandboxed profiling

The GUI's explicit runtime action and the CLI's
`audit <source> --runtime --sandboxie` profile inside Sandboxie. The CLI's
`audit <source> --runtime` setup-only route remains host-based; requesting
`--sandboxie` never falls back to the host:

- A dedicated box (`AppWrapProfile`) is configured from generated
  Sandboxie.ini settings (`SandboxieBoxConfig`): its file root is a
  scratch folder the audit owns, auto-recovery is off, and network file
  access is blocked. Configuration is installed through SbieIni.exe
  when present; the fallback updates only AppWrap's section in the
  global Sandboxie.ini.
- For `.exe`/`.msi`, the vendor installer runs in the box (same silent
  switches, same scratch install target), then the installed app runs in
  the box once. For `.zip`, the safe extractor unpacks to a scratch
  payload; the user selects an executable, which runs once in the box
  with the payload folder as its working directory. The extracted payload
  is kept under AppWrap's `Data\Audit` folder for the next build step.
- **Harvest**: files the box holds are mapped back to host paths
  (the box mirrors drives under `drive\<letter>\…` and the profile
  under `user\<name>\…`) and then to PAL tokens (`PalPathMapper`:
  `%APPDATA%`, `%LOCALAPPDATA%`, `%PROGRAMDATA%`, `%PAL:AppDir%`, …),
  and the box's own registry hive is loaded and normalized
  (MACHINE→HKLM, USER\<sid>→HKCU). Both halves become findings through
  the existing classification — one report shape for both backends.
- **Failure handling**: a missing Sandboxie install, failed box
  configuration, or failed registry-hive load stops the sandboxed audit.
  A present hive that cannot be loaded is never reported as an empty
  registry diff. The host route's honest limits in `docs/AUDIT.md` apply
  only when the user explicitly chooses it.

## Stage 3 — synthesis extensions

The draft-profile synthesizer (`docs/AUDIT.md`) gained the pipeline's
cleanup and repair rules:

- **Leftover vendor directories** → `[DirectoriesCleanupIfEmpty]`:
  the vendor parent of a redirected AppData folder (redirecting
  `%APPDATA%\Vendor\App` leaves the empty `%APPDATA%\Vendor` shell),
  and ProgramData vendor folders — ProgramData cannot be redirected by
  a per-user portable, so those are cleanup-only, with a note saying
  exactly that.
- **Dynamic path replacement**: a hardcoded absolute path found in a
  payload config file becomes a `[FileWrite]` Replace rule when the
  path tokenizes to a PAL form — at launch PAL rewrites the literal
  machine path in that file to the portable equivalent. Paths that do
  not tokenize stay unmapped, with the finding visible.
- **Branding splash**: when a package supplies no sidebar image, the
  compiled-portable flow composites the standard PortableApps branding
  splash from the wrapped app's icon and name (`SplashSpec`:
  164×314 24-bit BMP, `PortableApps.comInstaller.bmp`, the slot a
  supplied sidebar image otherwise fills), via
  `ImageAssets.WriteSplash`. A supplied sidebar image always wins.

## Stage 4 — compilation & leak verification

Compilation is unchanged: PAL launcher compilation and the PAF
packaging builder, both from the pinned backends (see
`docs/PAF-COMPLIANCE.md`).

Leak verification is the test harness's Sandboxie mode
(`TestHarness.RunSandboxieAsync`, CLI: `test <package> --sandboxie`):
the packaged app runs once inside a clean dedicated box
(`AppWrapLeakTest`, file root inside the harness's throwaway tree)
while the harness watches the host-side roots as usual. Every watched
root is outside the box, so **the verdict certifies zero writes escaped
the box**; the report records `observationBackend: "sandboxie"` (the
host mode records `"host"`). If Sandboxie-Plus is not installed, the
CLI refuses the mode with a plain explanation rather than silently
running the host mode instead.

## Evidence level

**Configuration validated** on a non-Windows host (`PipelineChecks`):
the locks' shape, bootstrap decisions, engine planning, engine-backed
audit through a fake runner, path mapping, box config generation,
box-config updates, zip runtime flow through a fake sandbox, registry
failure reporting, harvest classification and hive normalization, the synthesis
extensions, the splash spec, and the harness's Sandboxie verdict
through a fake sandbox.

**Unverified** (Windows-only runtime): the Setup bootstrap's actual
download/install, driving SbieIni/Start.exe, loading the box registry
hive, the real engine processes, the imaging layer's splash
rasterization, and all end-to-end behavior on a Windows machine.
