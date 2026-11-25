using System.Runtime.InteropServices;
using System.Text;
using AppWrap.Core;
using AppWrap.Core.TestHarness;

namespace AppWrap.Build.Audit;

/// <summary>
/// The production <see cref="IRegistrySnapshotProvider"/>: enumerates the
/// requested registry roots through advapi32 (RegOpenKeyEx / RegEnumKeyEx /
/// RegEnumValue / RegQueryValueEx), read-only, no impersonation and no
/// writes. Unreadable keys are skipped (an HKLM root the user cannot read
/// simply contributes what could be read). Bounds: 12 levels deep and
/// 500,000 values per capture; beyond that the capture fails with an
/// actionable error rather than returning a partial snapshot silently.
/// Windows-only at runtime; on other platforms it throws. Runtime behavior
/// on Windows is Unverified.
/// </summary>
public sealed class WindowsRegistrySnapshotProvider : IRegistrySnapshotProvider
{
    private const int MaxDepth = 12;
    private const int MaxValues = 500_000;

    private static readonly Dictionary<string, nint> Hives = new(StringComparer.OrdinalIgnoreCase)
    {
        ["HKCR"] = unchecked((nint)0x80000000), ["HKEY_CLASSES_ROOT"] = unchecked((nint)0x80000000),
        ["HKCU"] = unchecked((nint)0x80000001), ["HKEY_CURRENT_USER"] = unchecked((nint)0x80000001),
        ["HKLM"] = unchecked((nint)0x80000002), ["HKEY_LOCAL_MACHINE"] = unchecked((nint)0x80000002),
        ["HKU"] = unchecked((nint)0x80000003), ["HKEY_USERS"] = unchecked((nint)0x80000003),
        ["HKCC"] = unchecked((nint)0x80000005), ["HKEY_CURRENT_CONFIG"] = unchecked((nint)0x80000005),
    };

    public RegistrySnapshot Capture(IReadOnlyList<string> roots, CancellationToken token)
    {
        if (!OperatingSystem.IsWindows())
            throw new UserInputException("Registry observation needs Windows. The file half of the harness still works on this machine.");
        var keys = new Dictionary<string, IReadOnlyDictionary<string, RegistryValueSnapshot>>(StringComparer.OrdinalIgnoreCase);
        var count = new Counter();
        foreach (var root in roots)
        {
            token.ThrowIfCancellationRequested();
            var normalized = RegistrySnapshot.NormalizeKeyPath(root);
            var cut = normalized.IndexOf('\\');
            var hiveName = cut < 0 ? normalized : normalized[..cut];
            if (!Hives.TryGetValue(hiveName, out var hive))
                throw new UserInputException("Unknown registry root: " + root + ". Use a root such as HKCU\\Software.");
            var subPath = cut < 0 ? "" : normalized[(cut + 1)..];
            if (RegOpenKeyEx(hive, subPath, 0, KeyRead, out var handle) != 0) continue; // unreadable/absent root contributes nothing
            try { Walk(handle, normalized, 0, keys, count, token); }
            finally { _ = RegCloseKey(handle); }
        }
        return new RegistrySnapshot(keys);
    }

    private sealed class Counter { public int Values; }

    private static void Walk(nint key, string path, int depth, Dictionary<string, IReadOnlyDictionary<string, RegistryValueSnapshot>> keys, Counter count, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var values = new Dictionary<string, RegistryValueSnapshot>(StringComparer.OrdinalIgnoreCase);
        for (uint index = 0; ; index++)
        {
            var name = new StringBuilder(16384);
            var nameLength = (uint)name.Capacity;
            var type = 0u;
            var dataLength = 0;
            var status = RegEnumValue(key, index, name, ref nameLength, IntPtr.Zero, ref type, null, ref dataLength);
            if (status == 259) break; // ERROR_NO_MORE_ITEMS
            if (status != 0) break;
            var data = new byte[Math.Max(dataLength, 0)];
            var readLength = data.Length;
            var valueName = name.ToString(0, (int)nameLength);
            var text = "";
            if (data.Length > 0 && RegQueryValueEx(key, valueName, IntPtr.Zero, ref type, data, ref readLength) == 0)
                text = FormatData(type, data, readLength);
            if (++count.Values > MaxValues) throw new UserInputException("The registry roots are too large to snapshot (over 500,000 values). Narrow the roots.");
            values[valueName] = new RegistryValueSnapshot(valueName, KindName(type), text);
        }
        keys[path] = values;
        if (depth >= MaxDepth) return;
        for (uint index = 0; ; index++)
        {
            var name = new StringBuilder(256);
            var nameLength = (uint)name.Capacity;
            var status = RegEnumKeyEx(key, index, name, ref nameLength, IntPtr.Zero, null, IntPtr.Zero, IntPtr.Zero);
            if (status != 0) break;
            var childName = name.ToString(0, (int)nameLength);
            if (RegOpenKeyEx(key, childName, 0, KeyRead, out var child) != 0) continue;
            try { Walk(child, path + "\\" + childName, depth + 1, keys, count, token); }
            finally { _ = RegCloseKey(child); }
        }
    }

    private static string KindName(uint type) => type switch
    {
        1 => "REG_SZ", 2 => "REG_EXPAND_SZ", 3 => "REG_BINARY", 4 => "REG_DWORD",
        6 => "REG_LINK", 7 => "REG_MULTI_SZ", 11 => "REG_QWORD", _ => "REG_NONE",
    };

    private static string FormatData(uint type, byte[] data, int length)
    {
        try
        {
            return type switch
            {
                1 or 2 => Encoding.Unicode.GetString(data, 0, length).TrimEnd('\0'),
                7 => string.Join("\n", Encoding.Unicode.GetString(data, 0, length).Split('\0', StringSplitOptions.RemoveEmptyEntries)),
                4 when length >= 4 => BitConverter.ToUInt32(data, 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
                11 when length >= 8 => BitConverter.ToUInt64(data, 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
                _ => Convert.ToHexString(data, 0, length),
            };
        }
        catch (ArgumentException) { return Convert.ToHexString(data, 0, Math.Min(length, data.Length)); }
    }

    private const int KeyRead = 0x20019; // KEY_READ

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegOpenKeyExW")]
    private static extern int RegOpenKeyEx(nint hive, string subKey, int options, int samDesired, out nint result);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegEnumKeyExW")]
    private static extern int RegEnumKeyEx(nint key, uint index, StringBuilder name, ref uint nameLength, IntPtr reserved, StringBuilder? className, IntPtr classLength, IntPtr lastWrite);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegEnumValueW")]
    private static extern int RegEnumValue(nint key, uint index, StringBuilder name, ref uint nameLength, IntPtr reserved, ref uint type, byte[]? data, ref int dataLength);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegQueryValueExW")]
    private static extern int RegQueryValueEx(nint key, string valueName, IntPtr reserved, ref uint type, byte[] data, ref int dataLength);

    [DllImport("advapi32.dll")]
    private static extern int RegCloseKey(nint key);
}
