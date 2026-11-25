using System.IO;
using AppWrap.Build;
using AppWrap.Core;
using AppWrap.Core.PortableProfiles;

internal static partial class PalChecks
{
    /// <summary>
    /// Build-boundary checks: a missing PAL backend is a build failure (never a silent
    /// shim fallback), and a Pal profile on a standard input is rejected by BuildService.
    /// No compiler is executed here.
    /// </summary>
    internal static async Task RunAsync(string root, string evidence, AppWrapInput input, Action<string, bool> check, Action<string, Action> reject)
    {
        Run(input, check, reject);

        var profile = Pal("[Launch]\nProgramExecutable=Payload\\bin\\Demo.exe\n");
        var emptyBase = Path.Combine(evidence, "pal-empty-base"); Directory.CreateDirectory(emptyBase);
        var missingFailed = false;
        try
        {
            await PalCompiler.CompileAsync(input, profile, Path.Combine(evidence, "pal-package"), evidence, emptyBase, CancellationToken.None);
        }
        catch (BuildFailureException) { missingFailed = true; }
        check("pal missing backend is a build failure", missingFailed);

        var standardRejected = false;
        try
        {
            await new BuildService(root, Path.Combine(evidence, "pal-state")).BuildAsync(input with { Profile = profile }, CancellationToken.None);
        }
        catch (UserInputException) { standardRejected = true; }
        check("pal profile with standard mode rejected", standardRejected);
    }
}
