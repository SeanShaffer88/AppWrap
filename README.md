# AppWrap

A native Windows wizard that wraps an application folder into a standard installer or a PortableApps.com-format package. Choose a folder, answer six, seven or eight questions, and build. No accounts, downloads or telemetry in the application; project editing is configuration bookkeeping only, and custom code stays behind an off-by-default Advanced mode (see Limits).

## Run

Extract the complete Windows x64 ZIP into a writable folder and open `AppWrap.exe`. Keep `third_party` and the accompanying runtime files beside it. The distribution includes its .NET runtime; installing .NET is unnecessary. The executable is unsigned.

Choose **Installer**, **Portable app**, **Both**, or **PAF Package** on the first page. Installer, Portable app and Both are AppWrap's own compiled outputs (the generated portable installer is the AppWrap Installer); **PAF Package** instead produces the uncompiled package folder for the official PortableApps.com Installer to consume — see `docs/PAF-COMPLIANCE.md` for the exact relationship. The name comes from the folder, the default version is `1.0`, and a sole executable is selected automatically. Optional shortcut selections default Start Menu on and Desktop off; the installing user can change them. Portable packages use their portable menu entry instead of creating Windows shortcuts. Optional pictures can be skipped. Standard installers can offer app launch (default on), Start with Windows (default off), and a developer website (default off) on completion. The website URL is validated locally and starts unchecked in setup; startup changes only when the user finishes. Website text is cleared when selecting another application. Installer uses six steps, Portable app and PAF Package seven, and Both eight. Both uses one shared source, one destination folder, and one Build button to produce both files from one source snapshot. A PAF Package build asks for a destination folder and creates the package folder (named after the app's AppID) inside it.

The final step offers `Name-Version-Setup` (default), `Name_Setup`, and `Name-Setup-Version` filename styles with actual filename previews. A single-format save dialog also permits a custom filename. Changing name, version, mode or style clears that filename override while retaining its folder. Before replacing existing output, AppWrap shows one confirmation naming the targets. Content identities are captured before confirmation and checked again before publication, so stale consent cannot replace changed files.

Both outputs are prepared before either destination is changed. A handled second-file failure rolls the first back, or reports precise recovery paths if rollback is blocked. Cancellation is deferred during that short commit/recovery operation. This is not an atomic two-file transaction across process termination or power loss: recognizable `.appwrap-*.backup` and `.appwrap-*.tmp` files may remain beside the destination. Review those recovery files before deleting them; details identify the exact backup after a handled failure. A PAF Package folder is published with the same discipline: the staged folder is copied to an owned `.appwrap-*.tmp` folder beside the destination and verified against its tree hash, any previous package folder is moved to an `.appwrap-*.backup` folder, and a handled failure restores it.

Portable mode is for an application that already supports portable use. AppWrap does not redirect registry/profile writes, change dependencies or certify host cleanup. The complete original folder is retained under `App/Payload`, with a small native launcher that preserves its relative dependencies, working directory and caller arguments. The packaged application is never run during a build. Unknown license information is retained as unknown/conservative metadata; packaging does not grant redistribution rights.

At installation, portable packages gracefully close the selected destination's main executable and AppWrap launcher before replacing payload files. Exact image paths determine ownership: another folder's same-name app is left running. Applications that refuse to close, have an unsaved-work prompt, or cannot be identified stop the upgrade after a bounded wait. Nothing is force-killed or restarted. Other child programs/services require application-specific handling. The official generated installer uses an original trusted AppWrap preinstall hook; arbitrary source hooks remain rejected. See `native/UpgradeGuard/README.md`.

## Appearance and state

Choose AppWrap, Catppuccin Mocha, Nord, Gruvbox Light or OLED Black in the Appearance list beside Cancel. These are native brush palettes; Windows high-contrast mode uses system colors. Window size, theme and preferred filename style are stored in `Data/window.json` beside AppWrap. Restoring bounds conservatively clamps to the primary monitor's work area, so secondary-monitor positions may move to the primary monitor after restart.

Build staging lives in `Data/Builds/<unique-id>` and is removed after each normal build/cancel/failure. If cleanup fails, the UI identifies retained owned paths in details. A crash or power loss can leave staged copies; with AppWrap closed, `Data/Builds` may be deleted. Removing the extracted AppWrap folder removes its own settings and bundled tools. Windows may independently record ordinary application execution history. No hidden monitoring or background service is installed.

## Limits

Local regular paths only; linked/reparse paths are rejected. Sources are bounded to 50,000 entries, 40 nested folders and 20 GB. Pictures are bounded to 32 MB, 8192 pixels per side and 32 megapixels. Printable app names and numeric versions with one to four components are accepted. Compiler grammar-special path characters may be rejected with an actionable error. Portable source custom build scripts are unsupported and rejected; source installer actions/download settings are never executed. Optional chosen images produce seven ICO frames, PNG companions and a 164×314 24-bit sidebar bitmap.

Standard installers use a stable app-name-derived per-user installation identity, offer application launch on completion and ask the installer engine to close files in use. Runtime install/upgrade/uninstall, clean-machine compatibility and arbitrary application portability require testing; compilation and static archive checks do not establish them. Portable upgrades preserve unlisted files in `App`/`Other`/`Data`; files shipped with the same path can still be replaced, so application-specific settings behavior remains the app's responsibility.

Choosing a folder now records an `appwrap.project.json` project file at its root (schema, targeted PAF version, source snapshot hash, build history, saved choices; see `docs/project-model.md`). The file is AppWrap bookkeeping only: it is excluded from every package payload, and folders without one behave exactly as before. The wizard's project configuration panel edits the project's appinfo, launcher and installer configuration with the format checks shown inline. Advanced mode is off by default, is recorded per project in that file, and only unlocks direct text editing of the project's own `Custom.nsh` and `PortableApps.comInstallerCustom.nsh`; it changes no packaging behaviour — source build scripts in a payload are still rejected and custom code from a source folder is still never executed or compiled into a package.

## Build and verify

Windows x64, PowerShell 7, .NET SDK 10.0.404 or a compatible .NET 10 SDK and the pinned backend folders are required. No third-party NuGet runtime packages are used.

The checked native upgrade helper is embedded in AppWrap.Build. To rebuild it from source first, run `native/UpgradeGuard/Build.cmd` from that directory using the documented installed MSVC19.44/SDK10.0.26100.0 environment, then rebuild the .NET solution. Its compiler is a development dependency; generated installers carry the x86 static-runtime helper and require no compiler or .NET installation. Source, recipe and exact build provenance accompany the runtime distribution.

```powershell
dotnet build AppWrap.sln -c Release
& AppWrap.Tests/bin/Release/net10.0-windows/AppWrap.Tests.exe $PWD.Path 'C:/Program Files/FileBot/lib/7z.exe'
pwsh -NoProfile -File tools/Test-PackedModules.ps1
pwsh -NoProfile -File tools/Publish-AppWrap.ps1
```

The optional second test argument selects an installed 7-Zip executable for static package extraction. Tests create bounded owned fixtures outside the repository and never run generated installers or payloads. `tools/Test-PackedModules.ps1` verifies all four library packages through an independent consumer and an isolated package cache. See `docs/architecture.md` and each module's README for contracts.

Backend modifications are restricted to the distinctly named silent **AppWrap Packaging Builder**, based on the upstream wizard. Its exact source, original source, patch and reproducible preparation script accompany the backend. The official generated-installer script remains unchanged. Consult `THIRD-PARTY-NOTICES.md` before distributing third-party components or generated packages commercially. AppWrap is not endorsed by PortableApps.com.
