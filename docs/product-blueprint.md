# AppWrap — Complete Workflow Blueprint

**Product:** A wizard-driven, idiot-proof setup creator for standard Windows installers and PortableApps.com-compatible packages.
**Core promise:** Five questions, one Build button. The user never sees a script, an `.nsi` file, or an `appinfo.ini`.


## 1. Product Vision and Non-Negotiable Principles

AppWrap exists to serve the person who has never built an installer and should never have to learn. The existing “simple” tools — InstallForge, Clickteam Install Creator Pro — succeed because they are **wizard-driven and non-script-based**, requiring no scripting knowledge. AppWrap must go further by removing even the *concept* of a project structure or advanced configuration from the default path.

Three principles govern every design decision:

**The user never names the backend.** AppWrap must never display “NSIS,” “Inno Setup,” or “appinfo.ini” in its interface. The backend is an implementation detail. This is what separates a friendly wrapper from a developer tool wearing a GUI.

**There is no Advanced Mode in version 1.** The moment an “Advanced” button appears, the product has admitted that the simple path is incomplete. Users who need advanced configuration already have Inno Script Studio, InstallForge, or NSIS itself. AppWrap’s value is its refusal to expose complexity.

**The interface is one linear wizard, not a tabbed editor.** InstallForge uses a left-pane category structure (General, Files, Build, etc.) that, while wizard-driven, still presents a project workspace. Clickteam’s wizard is more linear, stepping through Files → Product Name → Version → Window → Background Bitmap → License → Installation Directory → Shortcuts → Build. AppWrap should follow the **linear, one-question-at-a-time model**, because linearity is what makes a flow feel guided rather than navigable.


## 2. Architecture: Thin Wrapper, Deterministic Code Generator, Silent Backend

AppWrap is not a new installer engine. It is a **thin GUI layer** that collects a small number of user inputs, deterministically generates a backend configuration or script, and invokes an existing engine silently.

```
┌─────────────────────────────────────────────┐
│              AppWrap GUI (Wizard)            │
│  Linear steps, no tabs, no advanced panels  │
└─────────────────────┬───────────────────────┘
                      │  User inputs (5–7 fields)
                      ▼
┌─────────────────────────────────────────────┐
│         Deterministic Code Generator         │
│   Input model → backend config / script     │
└──────────┬──────────────────────┬───────────┘
           │                      │
           ▼                      ▼
┌──────────────────┐   ┌──────────────────────────┐
│  Standard Mode   │   │   PortableApps Mode       │
│  Inno Setup      │   │  PortableApps.com         │
│  (silent compile)│   │  Installer (wrapped)      │
└──────────────────┘   └──────────────────────────┘
```

**Backend choice for Standard Mode: Inno Setup, not NSIS.** Inno Setup already ships a Script Wizard that asks only for app name, version, files, and shortcuts, then produces a working installer without script editing. More importantly, Inno Setup supports `/SILENT` and `/VERYSILENT` for unattended compilation and installation, which means AppWrap can invoke the compiler without any backend GUI flashing on screen. NSIS is more powerful but its compiler is less naturally silent; Inno Setup is the better “invisible engine” for a tool whose entire identity is invisibility.

**Backend for PortableApps Mode: the official PortableApps.com Installer, wrapped.** The PortableApps installer is already wizard-driven and expects a properly structured portable app directory. AppWrap should not reimplement the PortableApps format; it should generate the minimal `appinfo.ini` and icon assets, place them correctly, and invoke the official installer build process. This inherits every future fix and feature of the upstream installer.


## 3. The Unified User Workflow

The Standard Mode and PortableApps Mode share the same wizard shell. The PortableApps mode inserts two additional steps for icon and sidebar customization. The user experiences **one product with two output formats**, not two separate tools.

### Standard Mode: Five Questions

| Step | Question | UI Element | Default / Fallback |
|---|---|---|---|
| 1 | Where is your application? | Folder picker | None (required) |
| 2 | What is it called, and what version is it? | Two text fields, pre-filled from folder name | Folder name; version `1.0` |
| 3 | Which file launches the app? | Dropdown of `.exe` files found in the folder | None (required if `.exe` found) |
| 4 | Where should shortcuts go? | Checkboxes: Start Menu, Desktop | Start Menu checked; Desktop unchecked |
| 5 | Where should the installer be saved? | Save-file picker | Desktop, named `{AppName}_Setup.exe` |

After step 5, a single **Build** button appears. There is no “Next” to a confirmation screen, no summary page requiring approval, and no progress log unless the build fails.

### PortableApps Mode: Seven Questions

Steps 1–4 are identical. Step 5 is replaced by the portable packaging output step, and two visual customization steps are inserted:

| Step | Question | UI Element | Backend handling |
|---|---|---|---|
| 5 | Choose an icon for your app. | File picker (`.png`, `.ico`, `.jpg`) | Convert to required `appicon.ico` + PNG set; place in `App\AppInfo` |
| 6 | Choose a sidebar image for the installer window. | File picker (any image) | Resize/crop to correct aspect ratio, convert to 24-bit BMP; pass to installer build |
| 7 | Where should the portable package be saved? | Save-file picker | Desktop, named `{AppName}Portable.paf.exe` |

The user never sees `appicon.ico`, `WizardImageFile`, or `App\AppInfo`. They pick two pictures. If they skip either step, the wizard falls back to default PortableApps branding with no error and no broken build.


## 4. PortableApps Customization: The Technical Layer AppWrap Must Hide

### 4.1 The Launcher Icon (`appicon.ico` and companions)

The PortableApps.com Menu reads the application icon from `AppNamePortable\App\AppInfo\`. The main file is **`appicon.ico`**, and it must contain six specific formats: 16px, 32px, and 48px in both 256-color (8-bit) and True Color + Alpha (32-bit). There are also PNG companions: `appicon_16.png` and `appicon_32.png` (True Color with alpha transparency), with `appicon_128.png` optional but recommended for high-DPI displays. A 256px PNG variant is also optional for Vista+ large icon views.

**AppWrap’s handling:** The user selects any reasonably sized image. The generator uses an imaging library (ImageMagick, SkiaSharp, or System.Drawing) to produce the six-format ICO and the required PNG companions. If the user already has a correctly formatted `appicon.ico`, it is copied directly. The wizard never asks the user to “make a multi-resolution icon” — it simply asks for a picture.

There is a legacy `customicon.ico` mechanism mentioned in community discussions, but the official manual confirms `appicon.ico` is the standard path the launcher and installer actually recognize. Stick with `appicon.ico` and its PNG companions.

### 4.2 The Installer Sidebar Image (`WizardImageFile`)

The left-side graphic on the installer wizard is controlled by `WizardImageFile`. In Inno Setup, the directive specifies the image for the Welcome and Finished pages, and the image area maintains an **aspect ratio of 164:314**. The image can be BMP or PNG, including transparency, and Inno Setup recommends specifying an image larger than 202×386 to avoid blurriness on high-DPI systems; the default built-in image is 240×459. In NSIS Modern UI, the traditional constraint is a BMP no larger than 164×314, and 24-bit uncompressed BMP is the safe format.

**AppWrap’s handling:** The user selects any image. The generator:

1. Crops/resizes to the target aspect ratio (164:314) at a safe resolution (e.g., 328×628 or 492×942 for higher DPI).
2. Converts to the format the chosen backend expects (24-bit BMP for NSIS-style builds; PNG or BMP for Inno Setup).
3. Places the file in the source directory and injects the correct directive into the generated configuration (`WizardImageFile` for Inno Setup, or `MUI_WELCOMEFINISHPAGE_BITMAP` for NSIS).

Because AppWrap uses a deterministic code generator, this is a simple template substitution — not a dynamic scripting problem.


## 5. Technical Implementation Blueprint

### 5.1 Technology stack

**Recommended stack: C# / .NET 8 with WPF (or WinUI 3).** This choice is grounded in three factors. First, PackageEasy (a WPF/MVVM NSIS wrapper) has already proven that a modern .NET desktop framework is viable for this class of tool. Second, .NET provides mature imaging libraries (System.Drawing, SkiaSharp, ImageSharp) for the icon and sidebar conversion work. Third, single-file publishing with self-contained .NET produces a distributable `AppWrap.exe` without requiring a runtime install on the user’s machine.

Alternative stacks and their trade-offs:

| Stack | Viable? | Trade-off |
|---|---|---|
| C# / WPF | **Best fit** | Windows-only; fastest UI iteration; excellent imaging support |
| Python / PySide6 | Viable for prototyping | Distribution requires bundling a Python runtime or freezing with PyInstaller; slower startup |
| C++ / Qt | Viable for native performance | GUI development slower; Qt deployment adds complexity; overkill for a five-question wizard |

### 5.2 Project structure

```
AppWrap/
├── AppWrap.Gui/              # WPF wizard shell, view models
├── AppWrap.Core/             # Input model, validation, orchestration
├── AppWrap.Generators/       # Backend-specific code generators
│   ├── InnoSetupGenerator.cs
│   └── PortableAppsGenerator.cs
├── AppWrap.Imaging/          # ICO multi-resolution builder, BMP converter
├── AppWrap.Build/            # Silent process invocation, error capture
└── third_party/
    ├── innosetup/            # Bundled ISCC.exe (compiler)
    └── portableapps/         # Bundled PortableApps.com Installer toolchain
```

### 5.3 The input model (the entire product surface)

The GUI collects a single flat object. Every wizard page writes into this object; the generator reads only from this object.

```csharp
public sealed class AppWrapInput
{
    public string SourceFolder { get; set; }
    public string AppName { get; set; }
    public string AppVersion { get; set; }
    public string MainExecutable { get; set; }
    public bool CreateStartMenuShortcut { get; set; }
    public bool CreateDesktopShortcut { get; set; }
    public string OutputPath { get; set; }
    public OutputMode Mode { get; set; }          // Standard | PortableApps
    public string? LauncherIconPath { get; set; }  // PortableApps only
    public string? SidebarImagePath { get; set; }  // PortableApps only
}

public enum OutputMode { Standard, PortableApps }
```

This model is small enough to fit on one screen. That is intentional.

### 5.4 The Inno Setup generator (Standard Mode)

The generator emits a minimal `.iss` script and invokes `ISCC.exe` with `/Q` (quiet) or captures output for error reporting. Inno Setup’s `/SILENT` and `/VERYSILENT` parameters govern the *installed application’s* installer behavior, not the compiler’s behavior; the compiler’s quiet flag is what AppWrap needs to avoid flashing a console window. The generated script uses the `[Setup]`, `[Files]`, `[Icons]`, and `[Run]` sections with values directly mapped from `AppWrapInput`. No custom Pascal scripting is generated in version 1.

**Failure handling:** If the compiler returns a non-zero exit code, AppWrap shows a single dialog: “AppWrap couldn’t build the installer. The app folder may be in use, or a file may be locked. Try closing any running copies of the app and build again.” The full compiler log is available behind a “Show details” expander, but it is not the primary error message.

### 5.5 The PortableApps generator (PortableApps Mode)

The generator performs four deterministic steps:

1. **Scaffold the portable app directory.** Copy the source folder into a temporary staging directory and create `App\AppInfo\` if it does not exist.
2. **Generate or patch `appinfo.ini`.** Write a minimal valid `appinfo.ini` with `[Format]`, `[Details]`, `[Control]`, and `[License]` sections derived from `AppName`, `AppVersion`, and `MainExecutable`. If the source folder already contains an `appinfo.ini`, preserve it and only fill missing required fields.
3. **Place the icon assets.** If `LauncherIconPath` is provided, generate the six-format `appicon.ico` and the `appicon_16.png` / `appicon_32.png` companions into `App\AppInfo\`. If not, leave the defaults in place.
4. **Invoke the PortableApps.com Installer build process** with the sidebar image (if provided) passed via the appropriate customization mechanism.

**A note on the sidebar in PortableApps Mode:** The official PortableApps installer is NSIS-based, so the sidebar constraint follows the NSIS Modern UI convention: a BMP up to 164×314, with 24-bit uncompressed BMP being the safe format. AppWrap’s imaging module handles the conversion. If the PortableApps build system exposes a customization hook (such as a `PortableApps.comInstallerCustom.nsh`), the generator injects the `MUI_WELCOMEFINISHPAGE_BITMAP` define there; otherwise, it places the BMP where the build script expects it. The user sees none of this.


## 6. UX and Error-Handling Guidelines

**No dead ends.** Every step that is not strictly required must have a sensible default. If the folder contains exactly one `.exe`, step 3 is pre-selected and the user can simply click Next. If no `.exe` is found, the wizard shows a friendly message: “We couldn’t find an application file in this folder. Pick the folder that contains your app’s `.exe`.” The user is never asked to type a path manually.

**No technical error messages in the primary UI.** Backend compiler output, exit codes, and stack traces go behind a “Show details” disclosure. The visible message explains what happened in plain language and what the user can try next.

**Skip means default, not error.** In PortableApps Mode, if the user skips the icon and sidebar steps, the build proceeds with default branding. There is no warning, no “are you sure?” dialog, and no incomplete state. The tool should feel forgiving, not nagging.

**The Build button is the only action on the final screen.** No “Save project” button. No “Export script” button. No “Test installer” button (the built installer is the test). A single **Build** button and a single **Cancel/Close** affordance after a successful build.

**Progress is a single indeterminate bar, not a log window.** The build takes seconds to a minute depending on the app size. A determinate bar is misleading because compression times vary; an indeterminate bar with a plain status line (“Wrapping your app…”) is honest and calm.


## 7. Development Roadmap

### Phase 1: Standard Mode, Inno Setup backend (Weeks 1–4)

Build the wizard shell, the `AppWrapInput` model, the imaging module, and the Inno Setup generator. Ship a working `.exe` that produces a standard installer from five inputs. No PortableApps support yet. This phase validates the core UX promise: “five questions, one Build button.”

### Phase 2: PortableApps Mode, icon pipeline (Weeks 5–7)

Add the PortableApps output path. Implement the `appicon.ico` multi-resolution generator and the `appicon_XX.png` companion output. Implement the `appinfo.ini` scaffolder. Integrate the official PortableApps installer build process. The sidebar image step can be deferred to Phase 3 if the icon pipeline alone proves sufficient for an early PortableApps release.

### Phase 3: PortableApps Mode, sidebar image (Week 8)

Add the sidebar image picker and the `WizardImageFile` conversion pipeline. This is a small, isolated feature once the imaging module and the PortableApps generator exist.

### Phase 4: Polish and distribution (Weeks 9–10)

Error-handling refinement, single-file self-contained publish, code signing (if distributing broadly), and a short “How AppWrap works” page that explicitly tells the user: “You don’t need to write any scripts. Pick a folder and answer a few questions.”


## 8. Risks and Recommendations

**Risk: Temptation to add an Advanced Mode.** This is the single greatest threat to the product. An Advanced Mode would satisfy power users but destroy the product’s identity. **Recommendation:** Keep advanced configuration permanently out of scope. If users need it, point them to InstallForge, Inno Script Studio, or NSIS. AppWrap’s job is to be the tool you hand to someone who has never built an installer and should never have to learn.

**Risk: Backend updates breaking the generator.** Inno Setup and the PortableApps installer evolve. **Recommendation:** Pin bundled backend versions in `third_party/`, and treat backend upgrades as deliberate, tested releases — not automatic. The generator’s output is a contract; test it against each new backend version before bundling.

**Risk: Name collision.** “AppWrap” is already used by several unrelated products (an application containerization tool, a web-to-app converter, an npm package). **Recommendation:** This is a real naming risk if the tool is distributed publicly. Before committing, perform a trademark search and check domain/package-name availability in your target distribution channels. If “AppWrap” is contested, consider **SetupPal** or **PackPal** as fallbacks with the same friendly, backend-neutral character.

**Recommendation: Treat the generated script as a compile target, not a program.** The GUI models the installation; the generator translates that model deterministically; the backend compiles the translation. Do not build a “script editor with syntax highlighting” as a hidden feature. The moment the user can see and edit the generated script, the wizard’s authority as a guided flow is undermined. Keep the generated artifacts in a temporary directory, delete them after the build unless the user explicitly asks to keep them (and even then, do not offer to open them in an editor).

**Recommendation: Instrument the funnel.** Track which step users abandon. If step 3 (“Which file launches the app?”) has a high drop-off, the `.exe` detection needs improvement. If the PortableApps icon step causes hesitation, the preview of the selected image needs to be clearer. A five-step wizard with one Build button is simple enough that any friction is measurable and fixable.