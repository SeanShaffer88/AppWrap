using System.IO;
using System.Text;
using AppWrap.Build;

internal static class BackendLogChecks
{
    internal static async Task Run(string evidence, Action<string, bool> check)
    {
        const string compiler = "Processing script file: owned.nsi\nError: actual compiler error";
        check("compiler no-BOM UTF8 log readable", BackendLog.Decode(Encoding.UTF8.GetBytes(compiler)) == compiler);
        check("compiler BOM UTF8 log readable", BackendLog.Decode(new byte[] { 0xef, 0xbb, 0xbf }.Concat(Encoding.UTF8.GetBytes(compiler)).ToArray()) == compiler);
        var wizard = "ERROR: C:\\" + new string('界', 120) + "\\app.exe";
        check("wizard no-BOM UTF16 long Unicode error preserved", BackendLog.Decode(Encoding.Unicode.GetBytes(wizard)) == wizard);
        check("compiler BOM UTF16 log readable", BackendLog.Decode(new byte[] { 0xff, 0xfe }.Concat(Encoding.Unicode.GetBytes(compiler)).ToArray()) == compiler);
        var malformed = new byte[] { 0xff, 0xfe, 0x00 };
        var display = BackendLog.Decode(malformed);
        check("malformed declared encoding retains exact bytes", display.Contains("invalid or unknown", StringComparison.Ordinal) && display.EndsWith(Convert.ToBase64String(malformed), StringComparison.Ordinal));
        var legacy = new byte[] { 0x45, 0x72, 0x72, 0x6f, 0x72, 0x3a, 0xff };
        check("unknown legacy compiler bytes retained", BackendLog.Decode(legacy).EndsWith(Convert.ToBase64String(legacy), StringComparison.Ordinal));
        var path = Path.Combine(evidence, "large-backend-log.txt"); await File.WriteAllTextAsync(path, new string('a', 400000));
        var bounded = await BackendLog.ReadAsync(path, CancellationToken.None);
        check("backend diagnostic read bounded and marked", bounded.Length < 132000 && bounded.EndsWith("[log truncated]", StringComparison.Ordinal));
    }
}
