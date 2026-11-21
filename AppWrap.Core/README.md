# AppWrap.Core

Windows .NET 10 input contracts and filesystem validation, shared by the wizard and packaging services. Original AppWrap code uses the repository's MIT license; there are no third-party package dependencies.

Public API: immutable `AppWrapInput`, `OutputMode`, `UserInputException`, and `InputRules` (`Validate`, `Executables`, `PackageVersion`, `SafeId`, `SafePath`, `IsWithin`, `Text`). Validation and executable discovery read the selected local filesystem; they do not run applications, write settings, start processes, or use the network. Importing the assembly has no background side effects. Exceptions describe rejected paths and inputs.

```csharp
using AppWrap.Core;
var input = new AppWrapInput(source, "My App", "1.2", "MyApp.exe", true, false, output);
InputRules.Validate(input);
var fourPartVersion = InputRules.PackageVersion(input.AppVersion); // 1.2.0.0
```

Use `tools/Test-PackedModules.ps1` from the repository for an independent consumer restored from the actual local NuGet artifacts. The application regression harness covers additional malicious paths and input cases. These are local APIs for trusted host applications; validation cannot eliminate all filesystem change races.
