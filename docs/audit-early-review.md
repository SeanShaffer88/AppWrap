# Early Core/generator review

2026-11-21; read-only implementation review while other modules are in progress. Not final sign-off.

## Required: ISPP expression injection survives brace escaping

`AppWrap.Generators/InnoSetupGenerator.cs`, `Literal`: doubling a literal opening brace protects Inno runtime constants, but does not protect Inno Setup Preprocessor inline expressions. A supplied app name or source path containing `{#...}` becomes `{{#...}`; ISPP still evaluates the expression beginning at the second opening brace. This violates the supplied contract that source input cannot execute build-time code.

Confirmed with the installed official compiler using a harmless arithmetic expression:

```powershell
@'
[Setup]
AppName={{#1+1}
AppVersion=1.0
'@ | & 'C:\Program Files\Inno Setup 7\ISCC.exe' --preprocess --no-ide-signtools --no-signing -
```

Observed process exit 0; stdout `AppName={2`. No installer, source application or arbitrary function was executed. Fix by rejecting preprocessor-inline syntax across all interpolated values, or by configuring a verified non-overlapping fixed inline delimiter before user data. Add a real preprocessing regression.

## Required: distinguish display names from Windows shortcut names

`InputRules.Text` only rejects empty/control/overlong names; the same app name is used as the shortcut filename. Characters such as slash, colon and wildcard can produce invalid shortcut paths or unintended nested folders. Validate the filename component or derive a safe shortcut label separately while retaining the display name. Verify the chosen behavior through generator and compiler tests.

## Current positive evidence

- Main executable rejects rooted paths and resolves strictly under the source directory; selected file existence and `.exe` extension are checked.
- Source/output paths and traversed source entries reject reparse-point ancestors; recursive inventory limits depth and entry count.
- Output descendant/equality and nonexisting output directories are refused.
- Version syntax is constrained to one to four unsigned 16-bit numeric components before build.
- Generated standard sections are declarative; this module contains no payload execution or source-script include.

Remaining process, cancellation, staging, final-output, imaging and GUI gates await stable implementation/evidence.
