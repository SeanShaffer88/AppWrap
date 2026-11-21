# AppWrap.Core

`PackageNaming.GetFileName(name, version, mode, style)` is the canonical pure naming API. `PackageNamingStyle` offers `NameVersionSetup` (default), `NameOnly` and `NameSetupVersion`; `Labels` supplies readable UI choices and `ValidateName` applies the shared Windows filename rules. An unknown saved style falls back to the current default. The entered valid version is retained in the output filename, while `InputRules.PackageVersion` separately produces four-part metadata.

Windows .NET 10 input contracts and filesystem validation, shared by the wizard and packaging services. Original AppWrap code uses the repository's MIT license; there are no third-party package dependencies.

Public API: immutable `AppWrapInput`, `OutputMode`, `UserInputException`, and `InputRules` (`Validate`, `Executables`, `PackageVersion`, `SafeId`, `SafePath`, `IsWithin`, `Text`). Validation and executable discovery read the selected local filesystem; they do not run applications, write settings, start processes, or use the network. Importing the assembly has no background side effects. Exceptions describe rejected paths and inputs.

```csharp
using AppWrap.Core;
var input = new AppWrapInput(source, "My App", "1.2", "MyApp.exe", true, false, output);
InputRules.Validate(input);
var fourPartVersion = InputRules.PackageVersion(input.AppVersion); // 1.2.0.0
```

The final optional positional argument `StandardOptions` accepts `StandardInstallerOptions(OfferLaunchAfterInstall = true, OfferStartWithWindows = false, OfferDeveloperWebsite = false, DeveloperWebsite = null)`. Existing calls retain these defaults. `StandardInstallerOptions.Normalize(null)` returns those defaults; when the website offer is enabled it returns a normalized absolute HTTP/HTTPS URL or throws `UserInputException`. `ValidateWebsite` rejects credentials, raw quotes/backticks, control characters, whitespace, backslashes, relative/custom schemes and values over 2048 characters. These two methods perform no filesystem or network I/O. Portable inputs ignore this standard-only option, including when paired with a standard input carrying different options.

```csharp
var standard = input with { StandardOptions = new(false, true, true, "https://example.org/app") };
InputRules.Validate(standard);
```

Use `tools/Test-PackedModules.ps1` from the repository for an independent consumer restored from the actual local NuGet artifacts. The application regression harness covers additional malicious paths and input cases. These are local APIs for trusted host applications; validation cannot eliminate all filesystem change races.
