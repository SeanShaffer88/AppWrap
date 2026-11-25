# Portable configuration capability matrix

Scope: full **published PAL2.2 launcher configuration** and **PortableApps Format3.9/Installer3.9.24 configuration** surface, checked2026-11-21. This is a researched implementation inventory, not a claim these features already ship or that every Windows app can be made portable. Runtime portability and installer upgrade policies are separate consumers. User's latest explicit request supersedes the original no-configuration-editor restriction.

Legend: **D** declarative setting with actual pinned source/manual support, planned for typed editing+text roundtrip; **E** supported but expert/risk-gated; **X** obsolete/disabled/not demonstrated in pinned engine, visible with explanation and cannot silently compile as working; **C** explicit trusted source extension, separate approval/hash gate. Every D/E row still needs actual build/runtime evidence before a shipped implementation claims it works.

## Pinned evidence and compiler

Official download page currently lists PAL **2.2.9**, updated2024-05-26, SHA256 `7bc93e47b885d5e953fa2082a2e65169ae98128026ac4d747f774a23bd01345b`. Downloaded exact official PAF under `work/pal-research`, hash matched; static7-Zip extraction930files exited0. No downloaded launcher/installer/payload executed. Native HTTP fetched official manual pages after browser-tool restrictions; initial text-normalization command failed (exit1), corrected fetch succeeded(exit0), so only corrected text snapshots are evidence.

PAL bundle includes `App/NSIS/makensis.exe` **v2.46.5-Unicode**, x86, SHA256 `47cd625d2d90f0cfdff411ba18c39c88ab7e9b28730fc81e5e14ed66ead6fb21`. `/VERSION` observed; static Get-DllInfo valid=true/x86. Do not substitute the newer Installer NSIS compiler just because both are NSIS. PAL's own `GeneratorWizard.nsi` around245–285 selects metadata and compiles:

`<PAL>/App/NSIS/makensis.exe /O<owned-log> /DPACKAGE=<stage-package> /DNamePortable=<validated-name> /DAppID=<validated-id> /DVersion=<PAL-tool-package-version> [XML_ENABLED/RUNASADMIN_COMPILEFORCE defines] <PAL>/Other/Source/PortableApps.comLauncher.nsi`

Those exact definitions/order and package metadata must be verified by actual direct compile. Developer code passes separate argv; generated source/settings are never shell fragments. Direct official-source compilation avoids the interactive Generator wizard. `PACKAGE` is stage root; output `<PACKAGE>/<AppID>.exe`. Exact required runtime INI is `App/AppInfo/Launcher/<AppID>.ini`, **not a literal launcher.ini**. Root `<AppID>.ini` is a different optional end-user override file.

Pinned source includes its own registry.dll under App/NSIS/Plugins and UAC/xml/newtextreplace under Other/Source/Plugins. All statically x86; hashes: registry `acb79c55b3b9c460d032a6f3aaf6c642bf8c1d450e23279d091cc0c6ca510973`; UAC `c5ab2926c268257122d0342739e73573d7eeda34c861bc7a68a02cbc69bd41af`; XML `e900fcb9d598643eb0ee3e4005da925e73e70dbaa010edc4473e99ea0638b845`; newtextreplace `156b9b583399faf13c4d46b89339fb0f7f38dc847ac2d7872178d8e3998b9734`. registry readme identifies plugin3.5 with Unicode2010 changes; DLL version resources blank. Hash/provenance is stronger identity evidence here than inventing a modern plugin version.

Source `PortableApps.comLauncher.nsi` header GPL2-or-later; preserve exact license/source and plugin notices. Official page says FOSS/freeware licensed use, commercial via contacting PortableApps. Full corresponding-source/per-plugin obligations remain distribution gates; source tree presence is not proof every bundled binary's obligations are met. Official GitHub `PortableApps/Launcher` is an upstream source reference, not automatically the exact release snapshot. Use downloaded hash-pinned release as execution source and compare future updates deliberately.

## Runtime launcher: every documented family

| Family | Exact public fields | Editor meaning/status | Important semantics |
|---|---|---|---|
| Launch variants | ProgramExecutable, ProgramExecutable64, ProgramExecutableWhenParameters, ProgramExecutableWhenParameters64 | D: file pickers, optional variant rows | Relative to package **App**; precedence WhenParameters64→64→WhenParameters→base. No stock ARM64 variant |
| Arguments/cwd | CommandLineArguments, WorkingDirectory | D: typed literal/path/token argument controls + text; optional cwd | Fixed arguments plus caller arguments plus root-user AdditionalParameters. WorkingDirectory can rewrite a single relative argument; test multiple relative paths, do not promise arbitrary argv fidelity |
| OS bounds | MinOS, MaxOS | E:2000/XP/2003/Vista/2008/7/2008 R2; omit key for no bound | Public manual mentions none, but implementer verified actual OperatingSystem.nsh rejects literal none. GUI None must serialize omission. No enforced Win10/11 enum; never serialize fictional values |
| Elevation | RunAsAdmin, RunAsAdmin2000/XP/2003/Vista/2008/7/2008R2 | E: none/try/force; compile-force where actually supported | Default none; compile-force requires rebuild and conflicts with per-OS overrides. Build does not elevate creator or execute runtime UAC |
| Process coordination | SinglePortableAppInstance, SingleAppInstance, CloseEXE | D/E: clearly named instance checks | Stock CloseEXE checks for running basename; it does not gracefully close. Mutex AppID/baseName is not package-path-specific. Concurrent portable copies with shared host paths require explicit conflict review |
| Wait/children | WaitForProgram, WaitForOtherInstances, WaitForEXE1…N | D/E: wait defaults true; sequential extra exe names | Moves/registry cleanup require primary launcher alive until all relevant apps end. WaitForProgram=false only for profile with no cleanup-dependent work. Stock waiting by basename can include foreign processes; never name-kill |
| Temporary data | CleanTemp | D: contained temp default true | Stock uses host TEMP/AppIDTemp (or Data/Temp for nonwaiting); cleanup/recovery testing must cover preexisting collision and crash. Not an app-owned global-temp guarantee by itself |
| Splash | SplashTime, LaunchAppAfterSplash | D: milliseconds/boolean, default1500/false | Optional original/properly licensed artwork; honor end-user disabling |
| Shell/console | RefreshShellIcons, HideCommandLineWindow | D/E: before/after/both/none; boolean | Registry association workflows may need refresh; no invented global association toggles |
| Location behavior | DirectoryMoveOK, NoSpacesInPath, SupportsUNC | D/E: exact yes/warn/no or boolean | These declare/enforce behavior, not evidence. Folder/drive movement and UNC require matching real tests |
| Deprecated display | AppName | X/E: retained for compatibility, clearly deprecated | Prefer metadata name; do not invent behavior |
| Activate | Registry, Java, XML | D: enable referenced families automatically with visible switch state | Registry=true required for all registry sections; Java none/find/require; XML=true also requires compile XML_ENABLED/plugin |
| LiveMode | CopyApp | E: copy app for Live mode | End-user RunLocally activates Live mode; data changes may be discarded. Host TEMP writes documented |
| LiveMode obsolete | CopyData | X | Source deliberately disables switch, always copies Data; retain import as inactive warning |
| Environment | ordered arbitrary NAME=value | D: row editor with typed tokens | Child-process environment only; ordering matters. No machine/user environment mutation. Names unique case-insensitively, no critical blank/root expansion |
| RegistryKeys | storage-name=HKCU/HKLM/HKCR subtree; special -=key | D/E: preserve portable changes with local backup/restore, or discard portable changes while protecting local data | Source backs up under HKCU/Software/PortableApps.com/Keys, saves Data/settings/*.reg, restores on normal completion. Never map whole hive/Software/Classes root. HKCR normalized to HKCU/Software/Classes by PAL |
| RegistryValueWrite | key\\value=REG_TYPE:data (default REG_SZ) | D/E: value name/type/data rows | Does **not itself** preserve prior value; require covering RegistryKeys or RegistryValueBackupDelete rule. Default value trailing\\ semantics needs exact fixture |
| RegistryValueBackupDelete | sequential1…N full value paths | D/E: protect prior host value, discard portable run value | Restores host value; does not persist portable value. Validate conflicts with key rules |
| RegistryCleanupIfEmpty | sequential1…N keys | D: remove only empty app-owned parents | No root/broad parent targets |
| RegistryCleanupForce | sequential1…N keys | E: destructive exact app-owned temporary keys only | Unconditional deletion; no default rows. Require explicit rule acknowledgment/scope evidence and safe backup ownership; cannot overlap restored local keys |
| QtKeysCleanup | sequential1…N Qt cache prefixes | E: exact documented cache entries | Source-specific old Qt cache convention; no arbitrary registry path guessing |
| Registry view | no stock published key | X for explicit32/native64/both until verified adapter | No SetRegView/KEY_WOW64 setting found in pinned PAL source/wrapper. Show requested view and engine limitation; do not serialize made-up HKLM64/RegistryView keys or equate payload bitness with plugin view |
| FileWrite1…N | Type, File, Entry, Section, Key, Value, Find, Replace, Attribute, XPath, CaseSensitive, Encoding | D/E: typed INI/ConfigWrite/Replace/XML text/XML attribute | Ordered sequential sections. File must exist or stock skips; UTF16/no-BOM behavior explicit. XML requires XML compiled. Path relocation presets generate specific→broad replacements; no arbitrary source execution |
| FilesMove | Data-relative file/pattern=runtime target **directory** | D: save portable file and restore preexisting host file | Do not append filename to target. Data parent must exist. Wildcards supported but bounded/prefix-owned only |
| DirectoriesMove | Data-relative folder=runtime full target **folder**; special -=target | D/E: save portable tree or protect/discard local target | Target includes folder name. Data/settings reserved for registry; moving it is invalid. App/Payload-relative settings and host AppData both configurable |
| DirectoriesCleanupIfEmpty | sequential1…N expanded directory patterns | D: app-owned empty parent cleanup | Safe default; skip nonempty |
| DirectoriesCleanupForce | sequential1…N directories/patterns | E: destructive app-owned scratch cleanup | No default deletion, never broad AppData/Documents/TEMP/root or overlapping restore scope. Host data loss possible if misconfigured |
| Language | Base, Default, CheckIfExists, DefaultIfNotExists | D: platform-language mapping and fallback | Define PAL:LanguageCustom; no automatic installed language claim |
| LanguageStrings | arbitrary ordered platform-string=app-string mapping | D: row editor | Validate resulting paths and missing/fallback behavior |
| LanguageFile | Type, File, Entry, Section, Key, Attribute, XPath, CaseSensitive, TrimRight | D/E: read local config fallback (ConfigRead/INI/XML attribute/XML text) | XML compile flag required; real existing-file encoding semantics |
| Service1…N | Name, Path, Type, User, Display, Dependencies, Description, IfExists | X | Pinned Services.nsh explicitly disabled; replacement/restore TODO. Do not enable services/drivers or route around it with hidden custom code |
| Root end-user settings | AdditionalParameters, DisableSplashScreen, RunLocally | D/E: generate documented example / optional defaults | Distinct file from launcher definition; RunLocally may discard persistence. Never silently import runtime overrides from unrelated source |
| Custom source | Custom.nsh, Debug.nsh, Segment hooks, OverrideExecute, custom user keys | C | Trusted executable source, not ordinary INI. Hash-bound explicit opt-in; imports inert by default; debug never shipped inadvertently |

### All documented environment token families

Directory tokens: PAL:AppDir, PAL:DataDir, PAL:PortableAppsDir, PAL:PortableAppsBaseDir, PAL:LastPortableAppsBaseDir, PAL:PackagePartialDir, PAL:LastPackagePartialDir; host APPDATA, LOCALAPPDATA, USERPROFILE, ALLUSERSPROFILE, PAL-provided ALLUSERSAPPDATA, DOCUMENTS, TEMP; portable Documents/Pictures/Music/Videos; JAVA_HOME when activated. Drive tokens: PAL:Drive/LastDrive/DriveLetter/LastDriveLetter. Directory forms include normal, ForwardSlash, DoubleBackslash, java.util.prefs. Platform languages: PortableApps.comLanguageCode, LocaleCode2/3, Localeglibc, LocaleID, LocaleWinName, LocaleName, plus PAL:LanguageCustom. Source-proven FullAppDir/DriveHex variants require their own exact evidence entries, not generic guessed expansion. Custom ordered environment variables may be referenced after definition; forward references/cycles/unresolved variables fail validation. Never substitute host values at build time into portable profiles.

## Installer configuration: every published family

| Family | Fields / surface | Status and separation |
|---|---|---|
| Package metadata | Format Type/Version; Details Name, AppID, BaseAppName, Publisher, Homepage, Donate, Category, Description, Language, Trademarks, InstallType; License Shareable/OpenSource/Freeware/CommercialUse/EULAVersion; Version PackageVersion/DisplayVersion | D/E, full typed metadata editor with truthful/unknown rights state; not runtime backup rules |
| Menu/dependencies | SpecialPaths Plugins; Dependencies UsesGhostscript, UsesJava, UsesDotNetVersion, Requires64bitOS, RequiresPortableApp, RequiresAdmin; Control Icons, Start/StartN/NameN, ExtractIcon/ExtractName, BaseAppID/64/ARM64, ExitEXE/ExitParameters | D/E; declarations not installers/runtime download. BaseAppIDARM64 is metadata, not a PAL ProgramExecutableARM64 implementation |
| Associations/icons | FileTypes, FileTypeCommandLine[-extension], Protocols, ProtocolCommandLine[-protocol], SendTo, SendToCommandLine, Shell, ShellCommand; FileTypeIcons and AllOtherIcons; appicon variants | D/E. Advertise supported associations; actual host integration/restoration needs separate scoped runtime rules. File-picker/image pipeline unchanged |
| CheckRunning | CloseEXE, CloseName | D/E; stock basename warning vs current generated path-scoped guard separate. Guard must cover all configured executable variants and wait PAL root natural cleanup |
| Source | IncludeInstallerSource | D/E; source notices obligation not optional merely because false |
| MainDirectories | RemoveAppDirectory, RemoveOtherDirectory | D/E upgrade-only replacement switches. Preserve user Data by default. **RemoveDataDirectory is not in current published format**; source-history leftovers cannot become user-data wipe feature |
| Preserve/remove lists | DirectoriesToPreserve PreserveDirectory1…10; DirectoriesToRemove RemoveDirectory1…10; FilesToPreserve PreserveFile1…10; FilesToRemove RemoveFile1…10 | D/E; distinguish upgrade rule, runtime cleanup and uninstall. Prefix/overlap conflicts blocked; no default Data destruction |
| OptionalComponents | OptionalComponents, MainSectionTitle/Description, OptionalSectionTitle/Description, OptionalSectionSelectedInstallType, OptionalSectionNotSelectedInstallType, OptionalSectionPreSelectedIfNonEnglishInstall, OptionalSectionPreSelectedIfWindows32Bit, OptionalSectionPreSelectedIfLessThanWindows, OptionalSectionInstalledWhenSilent, OptionalDirectoryN, OptionalFileN; source-supported OptionalSectionInstalledWhenSilentOn32Bit | D/E; explicit payload composition and conditions, no hidden host actions. On32Bit flag verified by implementer in pinned wizard1014; label source-supported extension separately from public manual |
| Languages | all documented ENGLISH etc language booleans | D, complete supported locale list from pinned source rather than handpicked few; English required when section present |
| CopyLocalFiles | CopyLocalFiles, CopyFromRegPath, CopyFromRegKey, CopyFromRegRemoveDirectories, CopyFromDirectory, CopyToDirectory | E; source installation reuse at package install time, not creator-side source scan. Exact scope/permission; no host search during editing/build |
| DownloadFiles | AdditionalInstallSize; DownloadURL/KnockURL/Name/Filename/SHA256/To/InnoExtractTo/InnoExtractFrom; AdvancedExtractNTo/Filter; DoubleExtractFilename/DoubleExtractNTo/Filter; matching Download2 fields; Download2AdvancedExtractN*, Download2DoubleExtract*; CustomCodeUses7zip, UseLightweight7Zip | E; complete offline/online options. HTTPS+SHA256 for download/extraction, endpoint and size limits; nested extraction bounds. Build must not silently download runtime payloads or run installer inputs |
| LegacyVersions | Legacy32AppIDSuffix/AppNameSuffix/CutoffAppVersion/RemoveAppSubDirectory/CustomLegacyAppID; LegacyOSAppIDSuffix/AppNameSuffix/CutoffOSVersion/CutoffAppVersion/CustomLegacyAppID | E; migration artifacts and source/launcher replacements with fixture proof. These may mutate before CustomCodePreInstall—current guard ordering must be re-audited before enabling |
| Plugin/common files packages | plugininstaller.ini combined metadata; Details PluginName/PluginType=CommonFiles; plugin custom NSH | E/C; distinct package mode/target semantics. Not silently treated as an ordinary app; requires separate install/upgrade fixtures |
| EULA | App/AppInfo/EULA.txt; PluginEULA.txt; EULAVersion | D/E; content license evidence, encoding, actual acceptance UX |
| Custom installer source | PortableApps.comInstallerCustom.nsh / PluginCustom.nsh; PreInstall/PostInstall/OptionalCleanup | C; only explicit hash-trusted source; preview side effects/order. Current internal guard hook is first-party and separately pinned, not permission to import arbitrary hooks |
| Installer logging | pac_installer_log.ini and supported /LOG behavior | engine-controlled/read-only evidence, never forge/delete or treat as portability proof |
| Uninstall/removal | no universal portable Windows uninstaller key | Show actual package removal semantics; runtime host restoration occurs at app exit. No invented installer.ini UninstallCleanup registry rule |

## Important limits, always visible

- Implementer P0 source inspection further verified `ForEachINIPair` enumerates raw section headers case-sensitively and supports repeated `-` rows by file enumeration; ordinary scalar ReadINIStr has different behavior. Preserve repeated discard rows, and require correct canonical section spelling for enumerated rule sections rather than silently treating all INI constructs case-insensitively.

- PAL is configured portability, not an OS sandbox. An app may access undeclared state; declarations need app-specific evidence. Drivers, services, reboot/system integration, DRM, Store identity and arbitrary privileged/background apps are not automatically portable.
- Normal child exit, even nonzero, allows PAL post-cleanup while primary remains alive. Killing/crashing PAL, power loss, drive removal, backup collision or app self-restart can leave host state. Source runtime INIs/backups are recovery evidence, not unconditional crash rollback.
- Exact child exit-code forwarding is not established by stock PAL source (ExecWait without captured return); do not inherit the basic shim's tested exit-code promise automatically.
- Enabling full installer features changes guard-hook ordering: Download/Legacy/OptionalCleanup may run before current PreInstall guard. The phased specification defines a distinct internal AppWrapPreMutation gate before the main Section's first SetOutPath; preserve public hook timing. Until that adapter and all affected legacy-root guards pass, these combinations remain blocked—not advertised as implemented. A modified generated installer has different source/exception distribution consequences, documented separately.
- All options must be discoverable/editable or explicitly marked unsupported; unsupported public options may be retained inert in an imported profile but cannot be advertised as effective. Unknown keys cannot be silently ignored because PAL often does exactly that.

## Primary sources

- https://portableapps.com/development
- https://portableapps.com/apps/development/portableapps.com_launcher
- https://portableapps.com/manuals/PortableApps.comLauncher/ref/launcher.ini/index.html (all linked sections fetched)
- https://portableapps.com/manuals/PortableApps.comLauncher/ref/envsub.html
- https://portableapps.com/manuals/PortableApps.comLauncher/topics/registry.html
- https://portableapps.com/development/portableapps.com_format
- https://github.com/PortableApps/Launcher (reference; pinned release sources above govern executable integration)

Local selected references: DevTools/portableapps docs04/06/09, resource GitHub catalog (reference tier, no unverified helper imported), portableapps-packager researched-models/workflow contracts. Existing typed Python research models provide concepts/tests, not a runtime dependency for this C# app. DevTools reference search exit0; static PE helpers exit0 on selected files; no registry/payload execution or runtime verification occurred in this research.
