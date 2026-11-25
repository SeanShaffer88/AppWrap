using AppWrap.Core;

internal static class NamingChecks
{
    public static void Run(Action<string, bool> check, Action<string, Action> reject)
    {
        check("naming version-first standard", PackageNaming.GetFileName("Meadow", "1.4.0", OutputMode.Standard) == "Meadow-1.4.0-Setup.exe");
        check("naming version-first portable", PackageNaming.GetFileName("Meadow", "1.4.0", OutputMode.PortableApps) == "Meadow-1.4.0-Portable.paf.exe");
        check("naming legacy standard", PackageNaming.GetFileName("Meadow", "1.4.0", OutputMode.Standard, PackageNamingStyle.NameOnly) == "Meadow_Setup.exe");
        check("naming legacy portable", PackageNaming.GetFileName("Meadow", "1.4.0", OutputMode.PortableApps, PackageNamingStyle.NameOnly) == "MeadowPortable.paf.exe");
        check("naming format-first standard", PackageNaming.GetFileName("Meadow", "1.4.0", OutputMode.Standard, PackageNamingStyle.NameSetupVersion) == "Meadow-Setup-1.4.0.exe");
        check("naming format-first portable", PackageNaming.GetFileName("Meadow", "1.4.0", OutputMode.PortableApps, PackageNamingStyle.NameSetupVersion) == "Meadow-Portable-1.4.0.paf.exe");
        check("naming preserves entered version", PackageNaming.GetFileName("Meadow", "01.2", OutputMode.Standard) == "Meadow-01.2-Setup.exe");
        check("naming spaces and Unicode", PackageNaming.GetFileName("Café Notes 日本語", "2.0", OutputMode.PortableApps) == "Café Notes 日本語-2.0-Portable.paf.exe");
        check("naming unknown style default", PackageNaming.GetFileName("Meadow", "1.0", OutputMode.Standard, (PackageNamingStyle)999) == "Meadow-1.0-Setup.exe");
        check("naming unknown negative style default", PackageNaming.GetFileName("Meadow", "1.0", OutputMode.PortableApps, (PackageNamingStyle)(-1)) == "Meadow-1.0-Portable.paf.exe");
        check("naming labels follow enum order", PackageNaming.Labels.SequenceEqual(new[] { "Name-Version-Setup", "Name_Setup", "Name-Setup-Version" }));
        foreach (var style in Enum.GetValues<PackageNamingStyle>())
        {
            var standard = PackageNaming.GetFileName("Meadow", "65535.1.2.3", OutputMode.Standard, style);
            var portable = PackageNaming.GetFileName("Meadow", "65535.1.2.3", OutputMode.PortableApps, style);
            check("naming distinct Windows targets " + style, !StringComparer.OrdinalIgnoreCase.Equals(standard, portable));
            check("naming stable extensions " + style, standard.EndsWith(".exe", StringComparison.Ordinal) && !standard.EndsWith(".paf.exe", StringComparison.Ordinal) && portable.EndsWith(".paf.exe", StringComparison.Ordinal));
            check("naming bounded maximum app name " + style, PackageNaming.GetFileName(new string('a', 120), "65535.65535.65535.65535", OutputMode.PortableApps, style).Length <= 255);
        }
        foreach (var name in new[] { "", " ", ".", "..", "App.", "App ", "CON", "con.txt", "PRN", "NUL.tar.gz", "AUX", "COM1", "LPT9.txt", "COM¹", "LPT².txt", "COM³", "bad/name", "bad\\name", "bad:name", "bad?name", "bad*name", "bad|name", "bad\"name", "bad<name", "bad>name", "bad\r\nname", "{#1+1}", new string('a', 121) })
            reject("naming rejects invalid name " + name.Replace("\r\n", "newline"), () => PackageNaming.GetFileName(name, "1.0", OutputMode.Standard));
        foreach (var version in new[] { "", "1.x", "1.2.3.4.5", "65536", "1..0", "-1", "1\r\n0" })
            reject("naming rejects invalid version " + version.Replace("\r\n", "newline"), () => PackageNaming.GetFileName("Meadow", version, OutputMode.Standard));
        reject("naming rejects unknown output mode", () => PackageNaming.GetFileName("Meadow", "1.0", (OutputMode)999));
        // Leading zeros are valid numeric input, but may not exceed a Windows component.
        reject("naming rejects oversized generated component", () => PackageNaming.GetFileName("Meadow", new string('0', 256), OutputMode.Standard));

        // Regression pins for the mode-switch crash: PackageNaming is a
        // build-time validator, so it must keep refusing the states the
        // wizard renders through on its way to a build — a PAF Package has
        // a folder destination, never a generated file name, and a blank
        // or half-typed name/version has no file name yet. The wizard's
        // render/reconcile path (MainWindow.TrySuggestedDestination)
        // depends on these throws staying UserInputException so it can
        // treat them as "no suggestion yet" instead of crashing.
        reject("naming rejects a file name for paf package mode", () => PackageNaming.GetFileName("Meadow", "1.0", OutputMode.PafPackage));
        reject("naming rejects a file name for paf package mode any style", () => PackageNaming.GetFileName("Meadow", "1.0", OutputMode.PafPackage, PackageNamingStyle.NameOnly));

        // The PAF folder name is the folder-side counterpart: derivable
        // for any valid name (including names whose file name needs the
        // version), and refused — again as UserInputException — for the
        // partial names a render can legitimately hold.
        check("folder name ends in Portable", PackageNaming.GetFolderName("Meadow").EndsWith("Portable", StringComparison.Ordinal));
        check("folder name strips spaces and punctuation", !PackageNaming.GetFolderName("Café Notes!").Any(c => c is ' ' or '-' or '!'));
        foreach (var name in new[] { "", " ", "App.", "App ", "CON", "bad/name" })
            reject("folder name rejects invalid name " + name, () => PackageNaming.GetFolderName(name));
    }
}
