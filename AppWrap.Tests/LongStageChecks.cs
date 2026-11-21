using System.IO;
using AppWrap.Core;
using AppWrap.Build;
using AppWrap.Generators;

internal static class LongStageChecks
{
    internal static async Task Run(string root, string evidence, AppWrapInput input, Action<string, bool> check)
    {
        var work = Path.GetDirectoryName(Path.GetDirectoryName(evidence))!;
        var owned = Path.Combine(work, "long-stage-" + Guid.NewGuid().ToString("N")[..8]);
        if (Directory.Exists(owned)) throw new IOException("Long-stage regression requires a new directory.");
        var output = Path.Combine(owned, "pairs"); Directory.CreateDirectory(output);
        var standard = input with { AppName = "AppWrapLifecycle-20261121-9f6a82c1", AppVersion = "1.0.0", OutputPath = Path.Combine(output, "long-name-Setup.exe"), Mode = OutputMode.Standard };
        var portable = standard with { OutputPath = Path.Combine(output, "long-name-Portable.paf.exe"), Mode = OutputMode.PortableApps };
        var id = PortableAppsGenerator.PackageId(portable);
        var oldRawOutput = Path.Combine(owned, "appwrap-state", "Builds", new string('a', 32), id, "Other", "Source") + "\\..\\..\\..\\" + id + "_1.0.0_English.paf.exe";
        check("long-stage regression exercises legacy relative path beyond 260", oldRawOutput.Length > 260);
        var result = await new BuildService(root, Path.Combine(owned, "appwrap-state")).BuildBothAsync(standard, portable, CancellationToken.None);
        check("short private portable stage handles long application identifier", result.Standard.Bytes > 10000 && result.Portable.Bytes > 10000 && File.Exists(result.Standard.OutputPath) && File.Exists(result.Portable.OutputPath));
        check("long-stage public application ID remains unchanged", PortableAppsGenerator.PackageId(portable) == id);
        File.WriteAllText(Path.Combine(owned, "result.json"), System.Text.Json.JsonSerializer.Serialize(new { legacyRawPathLength = oldRawOutput.Length, result }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
}
