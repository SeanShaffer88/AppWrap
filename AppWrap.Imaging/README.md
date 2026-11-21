# AppWrap.Imaging

Windows .NET 10 WPF/WIC image conversion with no third-party NuGet dependencies. Original code uses the repository's MIT license. Call image generation from an STA thread. It uses the installed Windows desktop graphics APIs and does not support non-Windows hosts.

Public API: `ImageAssets.Load(path)`, `DefaultIcon()`, `WriteIcons(sourcePathOrNull, destinationDirectory)`, and `WriteSidebar(sourcePath, outputPath)`. Loading reads a selected file; icon/sidebar methods write only to their explicit destination and may replace files there. The caller owns destination selection and cleanup. Decoder limits reject oversized input; invalid or unsupported images throw. `DefaultIcon` is original AppWrap artwork. Importing this assembly does not start background work, write settings, or access the network.

```csharp
using AppWrap.Imaging;
// Execute on an STA thread; use a destination owned by the caller.
ImageAssets.WriteIcons(null, iconDirectory);
ImageAssets.WriteSidebar(Path.Combine(iconDirectory, "appicon_128.png"), sidebarBmp);
```

`tools/Test-PackedModules.ps1` restores the real local NuGet package into a separate STA consumer and verifies produced icon, PNG, and 24-bit sidebar files. Application tests cover additional format details. Image decoding uses Windows codecs and is not a security sandbox.
