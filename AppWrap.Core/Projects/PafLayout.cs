namespace AppWrap.Core.Projects;

/// <summary>
/// The canonical PortableApps.com Format (PAF) package layout as implemented by the
/// toolchain AppWrap pins (PortableApps.com Launcher 2.2.9, PortableApps.com Installer
/// 3.9.24) and by AppWrap's own generators. All paths are relative to the project root
/// and use forward slashes so diagnostics and persisted data are platform-stable.
/// These constants are the single source of truth for where PAF files live; see
/// docs/project-model.md for the evidence behind each path.
/// </summary>
public static class PafLayout
{
    /// <summary>
    /// The PAF specification version AppWrap targets. Evidence: PortableAppsGenerator
    /// emits <c>[Format] Version=3.9</c> in every generated appinfo.ini, matching the
    /// pinned Installer 3.9.24 backend.
    /// </summary>
    public const string DefaultPafSpecVersion = "3.9";

    public const string AppDirectory = "App";
    public const string DataDirectory = "Data";
    public const string OtherDirectory = "Other";

    /// <summary>Package metadata, written by PortableBuild to App/AppInfo/appinfo.ini.</summary>
    public const string AppInfoPath = "App/AppInfo/appinfo.ini";

    /// <summary>
    /// Installer settings. The pinned installer reads installer.ini beside appinfo.ini
    /// (App/AppInfo/installer.ini); PortableBuild writes the merged guard + profile
    /// document to exactly this path.
    /// </summary>
    public const string InstallerIniPath = "App/AppInfo/installer.ini";

    /// <summary>Directory holding the per-app launcher INI files.</summary>
    public const string LauncherDirectory = "App/AppInfo/Launcher";

    /// <summary>Launcher INI file name for an AppID: App/AppInfo/Launcher/&lt;AppID&gt;.ini.</summary>
    public static string LauncherIniPath(string appId) => LauncherDirectory + "/" + appId + ".ini";

    /// <summary>
    /// Optional custom launcher code. The pinned launcher's Segments.nsh includes it
    /// non-fatally as ${PACKAGE}\App\AppInfo\Launcher\Custom.nsh at compile time.
    /// </summary>
    public const string LauncherCustomPath = "App/AppInfo/Launcher/Custom.nsh";

    /// <summary>
    /// Optional custom installer code. The upstream installer mechanism consumes
    /// Other/Source/PortableApps.comInstallerCustom.nsh; AppWrap itself writes its
    /// first-party upgrade-guard hook to this path in generated packages and rejects
    /// source-supplied copies in a payload.
    /// </summary>
    public const string InstallerCustomPath = "Other/Source/PortableApps.comInstallerCustom.nsh";

    /// <summary>Converts an OS path relative to the project root to the forward-slash form used in diagnostics.</summary>
    public static string ToDisplayPath(string relativeOsPath) => relativeOsPath.Replace('\\', '/');
}
