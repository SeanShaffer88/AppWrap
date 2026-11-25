namespace AppWrap.Core.Pipeline;

/// <summary>
/// The standard PortableApps branding splash the pipeline composites for
/// a package: the installer branding bitmap, at the PortableApps.com
/// Installer's fixed sidebar size, in the location AppWrap's own
/// packaging builder consumes it (the same slot a project's sidebar
/// image fills when one is supplied — see PortableBuild). The bitmap is
/// composited from the wrapped app's icon and name by the imaging layer
/// (AppWrap.Imaging.ImageAssets.WriteSplash); this spec is the pure
/// contract both sides share, so checks can pin the shape without
/// rasterizing anything.
/// </summary>
public sealed record SplashSpec(int Width, int Height, string FileName, string BuilderRelativePath, string Format)
{
    public static SplashSpec Standard { get; } = new(
        164, 314,
        "PortableApps.comInstaller.bmp",
        @"App\installer\PortableApps.comInstaller.bmp",
        "BMP (24-bit)");

    /// <summary>The splash is only ever composited from a real icon and a non-empty app name; anything else is a spec, not an image.</summary>
    public bool CanComposite(string? appName, bool hasIcon) =>
        !string.IsNullOrWhiteSpace(appName) && hasIcon;
}
