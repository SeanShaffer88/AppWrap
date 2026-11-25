# PAF compliance: AppWrap output and the official PortableApps.com toolchain

This document states exactly how AppWrap's portable outputs relate to the official
PortableApps.com toolchain: what AppWrap generates, what the official tools read,
which configuration surface is honored, and what is intentionally omitted or
blocked. It is grounded in the repository's own evidence — the generators, the
typed rule surfaces (`InstallerRules`, `CapabilityCatalog`), the capability matrix
(`docs/portable-capability-matrix.md`), and the pinned backend provenance — not in
the public manuals alone.

**AppWrap is not endorsed by PortableApps.com.** AppWrap is an independent
project; it is not affiliated with, sponsored by, or certified by
PortableApps.com, and generated packages do not claim PortableApps.com
certification or redistribution rights for the packaged app.

## Targeted specification

- **PortableApps.com Format (PAF) 3.9.** Every generated `appinfo.ini` declares
  `[Format] Type=PortableApps.comFormat` and `Version=3.9` (`PafLayout.DefaultPafSpecVersion`).
- **Pinned toolchain:** PortableApps.com Launcher (PAL) **2.2.9** and
  PortableApps.com Installer **3.9.24**, hash-pinned and prepared locally
  (`third_party/pal/provenance.json`, `third_party/portableapps/provenance.json`);
  nothing is downloaded at build or run time.
- Projects record the targeted version (`appwrap.project.json`, Phase 1) and
  `PafValidator` checks project and package folders against it.

## The two portable outputs, and who compiles what

AppWrap offers two ways to produce a PAF package from the same staged tree. The
staging is shared code (`PortableBuild.StagePackageAsync`); checks assert the
generated `appinfo.ini` and `installer.ini` are byte-identical between them.

| | **AppWrap Installer** (Portable mode, and the portable half of Both) | **PAF Package** |
|---|---|---|
| Output | A single compiled `<name>.paf.exe` | A folder named after the AppID (e.g. `MyAppPortable/`) |
| Compiled by | AppWrap itself, at build time, using the distinctly renamed silent **AppWrap Packaging Builder** (a modified upstream Installer wizard) driving the **unmodified** official installer template | The **official PortableApps.com Installer**, later, by whoever receives the folder |
| Launcher | Compiled by AppWrap during staging (see below) | The same compiled launcher, already in the folder |
| Relationship | **Replaces** the interactive official installer run | **Compiled by** the official installer, unchanged |

Why both: the compiled output is the safe default because it keeps AppWrap's
guarantees — non-interactive and deterministic compilation, the guard-coupled
installer settings described below, transactional publication with rollback, and
a build that never executes source-supplied actions. The PAF Package exists for
interoperability: a folder the official toolchain consumes as-is, for users who
want the official installer (or the PortableApps.com Platform's own packaging
flow) to perform the final compile.

AppWrap does **not** replace the PortableApps.com Platform: it neither
distributes, installs, updates nor runs portable apps for end users. It only
produces packages (compiled, or as a folder for the official installer) that
the Platform and the official installer can consume. The Development Suite's
later phases do not change this relationship: the project editors and the
template scaffolder prepare the same documents described below, and the
runtime test harness only *runs* a finished package in a throwaway sandbox to
report where it writes — it is not part of the toolchain and certifies
nothing.

The folder differs from the compiled package's contents only where a folder
consumer requires it:

- an empty `Data/` placeholder directory is present (the compiled package's
  installer creates `Data` on first run instead);
- the upgrade-guard helper ships beside its hook at
  `Other/Source/AppWrap.UpgradeGuard.exe` and the hook addresses it
  package-relatively (`..\..\Other\Source\AppWrap.UpgradeGuard.exe`, the same
  relative style the upstream installer template uses for its own source files
  from `App\installer`, the same depth below the package root). In the compiled
  flow the same hook embeds the helper from a build-machine staging path,
  because AppWrap's packaging builder compiles the folder on that machine in
  the same build.

One wizard choice is package content in **neither** portable output: the
sidebar image. It is not a file in the package and not an `installer.ini`
key. In the compiled flow, AppWrap substitutes the chosen image into its
private per-build clone of the packaging backend
(`App/installer/PortableApps.comInstaller.bmp`, converted to the 164×314
24-bit BMP the generated installer script names as its
`MUI_WELCOMEFINISHPAGE_BITMAP`) before its renamed builder runs, and the
builder's stock copy step propagates that backend file into the compile.
The official installer offers no package-side equivalent: the upstream
wizard (`InstallerWizard.nsi`, Installer 3.9.24) deletes any
`Other/Source/PortableApps.comInstaller.bmp` found in the package folder
and copies its own installation's `App/installer` files in before
compiling, the full set of `installer.ini` keys it reads contains no
graphics key, and the header bitmaps it does place in the package are
generated from the app icon, not supplied as files. A PAF Package therefore
never carries a sidebar image — whoever compiles the folder gets the
sidebar of their own installer installation — and AppWrap does not place
one in the folder, because the official wizard would silently delete it.
The sidebar remains a project property (schema v3) and applies to the
compiled portable output only.

The standard **Installer** mode (Inno Setup) is a separate, non-PAF format and
is out of scope for this document.

## The package folder

Both outputs contain the canonical PAF 3.9 layout:

```
<AppID>.exe                  compiled root launcher (see below)
help.html
App/AppInfo/appinfo.ini      generated metadata (below)
App/AppInfo/installer.ini    generated installer settings (below)
App/AppInfo/Launcher/<AppID>.ini   only with a portable (PAL) profile
App/Payload/…                the complete original application folder
Data/                        PAF Package only (empty placeholder)
Other/AppWrap-LICENSE.txt, Other/AppWrap-NATIVE-NOTICES.txt
Other/Source/PortableApps.comInstallerCustom.nsh   first-party guard hook
Other/Source/AppWrap.UpgradeGuard.exe              PAF Package only (the hook's helper)
Other/Source/…               PAL source bundle, only with a PAL profile
```

The official installer reads `appinfo.ini` and `installer.ini` from
`App/AppInfo`, packages `App`, `Data` and `Other` plus the root launcher, and
applies `installer.ini` at install time. AppWrap's folder supplies exactly that
input; the fixture in `PafPackageChecks` mirrors this layout and the staged tree
must pass `PafValidator` with zero errors.

## Support matrix: three distinct surfaces

"Supported" means different things on three surfaces that must not be
conflated: what the **generator emits** into a package, what the **project
editors** (Development Suite Phase 2) expose for a project's own documents,
and what **`PafValidator`** checks. The detailed tables in the following
sections are the authority; this matrix is the summary. Statuses:
**Required** (the format or AppWrap requires it), **Optional** (honored when
present), **Supported** (modeled/typed and validated), **Not supported**
(intentionally absent or blocked).

### appinfo.ini

| Surface | Emitted by generator | Editable in project editors | Checked by PafValidator |
|---|---|---|---|
| `[Format] Type`, `Version` | Always — **Required** | Supported | Type wrong = error; Version differing from the project target = info |
| `[Details] Name`, `AppID` | Always — **Required** | Supported | Missing = error |
| `[Details] Publisher`, `Homepage`, `Category`, `Description`, `Language` | Always — **Optional** (preserved or fallback) | Supported | Missing/invalid = warning or info |
| `[Details] Donate`, `Trademarks`, `InstallType` | Not emitted | Supported | Not checked |
| `[License]` four booleans | Always — **Optional** | Supported | Non-`true`/`false` value = warning |
| `[License] EULAVersion` | Not emitted | Supported | Not checked |
| `[Version] PackageVersion` | Always — **Required** | Supported | Missing or non-numeric = error |
| `[Version] DisplayVersion` | Always — **Optional** | Supported | Missing = warning |
| `[Control] Start` | Always — **Required** | Supported | Missing = error |
| `[Control] Icons`, `BaseAppID` | Always — **Optional** | Supported | Missing `Icons` = info |
| `[Control] ExtractIcon/ExtractName`, `BaseAppID64`, `BaseAppIDARM64`, `ExitEXE/ExitParameters` | Not emitted | Supported | Not checked |
| `[Dependencies]`, `[SpecialPaths] Plugins` | Not emitted | Supported | Not checked |
| `[Control] StartN/NameN`, associations families, `EULA.txt` file | Not emitted | Not supported | Not checked |

### Launcher INI (`App/AppInfo/Launcher/<AppID>.ini`, PAL profiles only)

| Surface | Generated from profile | Editable in project editors | Checked |
|---|---|---|---|
| `[Launch]` scalars (executable + variants, arguments, working directory, OS bounds, `CloseEXE`, `SplashTime`, wait/instance/temp/splash/shell switches) | Supported | Supported | `PortableProfileValidator` + `PafValidator` (`ProgramExecutable` missing = error) |
| `[Activate] Registry/Java/XML`, `[LiveMode] CopyApp`, `[Language]` scalars | Supported | Supported | Same |
| Sequential and pattern families (`FilesMove`, `DirectoriesMove`, `RegistryKeys`, `RegistryValueWrite`, `FileWriteN`, environment, cleanup families) | Supported (typed rule views + planner) | Via the profile workspace / document text, not the scalar editor catalogue | Same, plus planner cross-rule diagnostics |
| `[LiveMode] CopyData` | **Not supported** — removed in PAL 2.2.9, diagnosed not honored | Not offered | Diagnosed |

### installer.ini

| Surface | Emitted by generator | Editable in project editors | Checked |
|---|---|---|---|
| Guard settings (`[Source] IncludeInstallerSource`, `[MainDirectories] RemoveAppDirectory/RemoveOtherDirectory`, `[CheckRunning] CloseEXE`) | Always — **Required**, guard-owned | Visible; generated value always wins | `InstallerValidator` (`GuardSettingOverride`) |
| `[MainDirectories] RemoveDataDirectory` | Only as `false` — **Not supported** as a setting | Not offered | Forbidden in profiles (`RemovedDataDirectoryNotSupported`) |
| Preserve/remove lists (≤10 each), `[OptionalComponents]`, `[Languages]` | Only from a validated PAL profile — **Optional** | Scalars supported; numbered lists via document text | `InstallerValidator` (limits, overlap, `Data` protection, `ENGLISH=true`) |
| `DownloadFiles`, `CopyLocalFiles`, `LegacyVersions`, plugin/common-files mode | **Not supported** — blocked (`GuardOrderBlocked`), never emitted as effective configuration | Discoverable, shown blocked | Configuration-checked while blocked (URL/hash shape) |

## appinfo.ini

`PortableAppsGenerator.Generate` emits the following. "Preserved" means: when the
source app already carries an `appinfo.ini`, that key's value is carried into
the regenerated document (after the same safety screening as other generated
text); any other pre-existing keys are **not** carried over.

| Section / key | Emitted | Required by PAF 3.9 | Notes |
|---|---|---|---|
| `[Format] Type` | Always (`PortableApps.comFormat`) | Yes | |
| `[Format] Version` | Always (`3.9`) | Yes | The targeted spec version |
| `[Details] Name` | Always (`<AppName> Portable`) | Yes | From the wizard app name |
| `[Details] AppID` | Always (name-derived, ends in `Portable`) | Yes | Also the PAF Package folder name and launcher INI name |
| `[Details] Publisher` | Always | No | Preserved; otherwise `Publisher not supplied` — never invented |
| `[Details] Homepage` | Always | No | Preserved; otherwise `https://example.invalid/` |
| `[Details] Category` | Always | No | Preserved when it is a PortableApps.com category, otherwise `Utilities` |
| `[Details] Description` | Always | No | Preserved; otherwise a generic AppWrap line |
| `[Details] Language` | Always | No | Preserved; otherwise `English` |
| `[License] Shareable/OpenSource/Freeware/CommercialUse` | Always | No | Preserved when literally `true`/`false`, otherwise `false` (unknown stays conservative) |
| `[Version] PackageVersion` | Always (normalized four-part numeric) | Yes | From the wizard version |
| `[Version] DisplayVersion` | Always | No | The wizard version as entered |
| `[Control] Icons` | Always (`1`) | No | One root launcher |
| `[Control] Start` | Always (`<AppID>.exe`) | Yes | The compiled root launcher |
| `[Control] BaseAppID` | Always (`NOT-NEEDED`) | No | No base-app relationship |

Intentionally **not emitted** by the generator: `[Details] Donate`,
`Trademarks`, `InstallType`, `BaseAppName`; `[License] EULAVersion`;
`[Control] StartN/NameN`, `ExtractIcon/ExtractName`, `BaseAppID64`,
`BaseAppIDARM64`, `ExitEXE/ExitParameters`; `[Dependencies]`; `[SpecialPaths]`;
the associations families (`FileTypes`, `Protocols`, `SendTo`, `Shell` and their
command-line/icon companions); and an `App/AppInfo/EULA.txt` file. Several of
these are editable on a *project's* documents through the Phase 2 configuration
surface and are modeled by `InstallerRules`' metadata view, but AppWrap's
generator does not synthesize them: AppWrap does not invent publisher, licensing,
association or dependency claims for an app it did not author. A package that
needs them is a hand-finished project, and the PAF Package output is the route
that carries such a project's generated documents to the official installer.

## installer.ini

The generated document is the upgrade guard's settings, plus — only with a PAL
profile — the profile's installer entries merged over them with the guard always
winning (`InstallerDocumentMerger`). A source-supplied `installer.ini` inside an
app folder is **never** imported or executed; nor are source download actions or
custom hooks (see Custom code).

| Section / key | Emitted | Honored by the installer | Notes |
|---|---|---|---|
| `[Source] IncludeInstallerSource` | Always (`false`) | Yes | Package does not embed the installer source tree |
| `[MainDirectories] RemoveAppDirectory` | Always (`false`) | Yes | Upgrade replacement switch, coupled to the guard |
| `[MainDirectories] RemoveOtherDirectory` | Always (`false`) | Yes | Same coupling |
| `[MainDirectories] RemoveDataDirectory` | Always (`false`) | **Ignored** | Not part of the current published format (capability matrix); emitted only in the safe direction, and profiles are forbidden from setting it at all (`RemovedDataDirectoryNotSupported`) |
| `[CheckRunning] CloseEXE` | Always (`NONE`) | Yes | Stock basename close-check is bypassed **only** because the first-party guard hook performs a path-scoped graceful close in the same package; `NONE` is never emitted without the hook |
| Profile families: `[DirectoriesToPreserve]`, `[DirectoriesToRemove]`, `[FilesToPreserve]`, `[FilesToRemove]` (≤10 entries each), `[OptionalComponents]` scalars and payload lists, `[Languages]`, other non-guard scalars | Only from a validated PAL profile | Yes | Typed and validated by `InstallerValidator`: list limits, preserve/remove overlap and `Data`-target rules, optional entries require `OptionalComponents=true`, `[Languages]` keeps `ENGLISH=true`. Guard-owned keys can never be overridden (`GuardSettingOverride`) |

**Blocked, not honored:** the `DownloadFiles`, `CopyLocalFiles` and
`LegacyVersions` families and plugin/common-files mode are typed and
discoverable in profiles but diagnosed `GuardOrderBlocked` and never emitted as
effective configuration, because they can act before the guard's safe point in
the install sequence; enabling them awaits the reviewed earlier-safe gate (the
internal AppWrapPreMutation seam in `docs/portable-profiles-contract.md`).
Download URLs and SHA-256 hashes are still configuration-checked while blocked.
In a PAF Package these families are equally absent from the emitted document, so
the official installer sees the same honored surface.

## Launcher

- **No profile (packaging-only):** the root launcher is AppWrap's basic shim —
  an original NSIS script compiled during staging with the packaging backend's
  NSIS — which starts `App\Payload\<MainExecutable>` preserving relative
  dependencies, working directory and caller arguments. There is no launcher
  INI; `[Control] Start` names the shim.
- **PAL profile:** the launcher INI at `App/AppInfo/Launcher/<AppID>.ini` is
  generated from the typed profile (the full pinned-PAL surface in the
  capability matrix) and the root launcher is compiled from the pinned PAL
  2.2.9 by AppWrap during staging, with its corresponding-source/license bundle
  written under `Other/Source` (P6). The official installer does not compile
  launchers; upstream, the Launcher Generator performs this step, and AppWrap
  performs the equivalent step with the same pinned engine so the folder is
  complete when the official installer consumes it.

## Conversion audit and generated profiles

A package converted from a setup file or portable zip carries a launcher
profile **drafted from audit evidence** (`docs/AUDIT.md`): the static and
runtime audit passes report where the source app stores data and which
registry entries it creates, and the synthesized draft maps those
findings onto the ordinary PAL surface described above — `[RegistryKeys]`
save/restore, covered `[RegistryValueWrite]`, `[DirectoriesMove]` /
`[FilesMove]` redirects — through the same validator and planner as a
hand-written profile, with guard-owned installer keys untouched.
Findings with no honest PAL equivalent (services, drivers, hardcoded
paths that do not tokenize to a PAL form) are reported as unmapped,
never faked into rules; a service or driver in the source is a hard
warning that the converted package will still touch the host outside
the launcher's cleanup. The draft is untrusted data until the user
reviews and saves it in the profile workspace; neither audit pass is
part of any build, and the static pass executes nothing.

The conversion pipeline (`docs/PIPELINE.md`) extends this surface
without changing it: prepared extraction engines unpack installer
payloads for the static pass; the runtime pass can profile inside a
Sandboxie box; drafts additionally gain `[DirectoriesCleanupIfEmpty]`
rules for leftover vendor folders and `[FileWrite]` Replace rules for
hardcoded paths that do tokenize; and a package built without a
supplied sidebar image gets the standard branding splash composited
from the app's icon and name in the sidebar image's slot (164×314
24-bit BMP, `PortableApps.comInstaller.bmp` — the package-side file the
official installer does not consume; see the sidebar note above).
Setup-time Sandboxie bootstrapping is AppWrap's own installer behavior
(documented in `docs/PIPELINE.md`), not part of any generated package.

## Custom code and the no-scripts guarantee

- A payload carrying `.nsh`/`.nsi` files under `Other/Source` is **rejected**
  for every portable output, PAF Package included — unchanged by this phase.
- The only installer custom code in an AppWrap package is AppWrap's own
  first-party upgrade-guard hook (`Other/Source/PortableApps.comInstallerCustom.nsh`),
  generated from a trusted template with the guard's coupled settings.
- The Phase 2 Advanced mode unlocks editing a *project's own* custom-code files
  in the project folder. It changes no packaging behavior: project custom code
  is not imported into generated packages, and the default path still refuses
  custom build scripts and still never executes the packaged app during a build.

## Publication discipline for the folder output

A PAF Package build follows the same transactional discipline as the file
outputs, adapted to a folder (`FolderArtifactPublisher`): the staged tree is
copied to an owned `.appwrap-*.tmp` folder beside the destination and verified
against its deterministic tree hash, any previous destination is moved to an
owned `.appwrap-*.backup` folder, the pending folder is moved into place, and a
handled failure restores the previous folder or reports the exact recovery
paths. The build result identifies a folder output by that tree hash
(`SourceSnapshot`) and its total bytes. As with the paired file outputs, this
is not an atomic transaction across process termination or power loss.

## Evidence level

The compliance matrix above is checked by `PafPackageChecks`: the staged PAF
tree for a fixture carries the required files at the paths above, its
`appinfo.ini`/`installer.ini`/launcher documents pass `PafValidator` with zero
errors at PAF 3.9, and the compiled flow's staging is byte-identical for the
generated documents. `ComplianceChecks` additionally pins the matrix itself:
it asserts every key this document lists as emitted is present in
`PortableAppsGenerator` output, every key listed as editable is present in
the project editors' field catalogue, and every editable launcher key is
classified by the pinned-PAL `CapabilityCatalog` — so a supported field
regressing in code fails the suite even if this document is not updated.
Compilation of the folder by the official installer and
runtime install/upgrade behavior on Windows remain **Unverified** here, as with
the compiled outputs (see `docs/portable-profiles-qualification.md`).
