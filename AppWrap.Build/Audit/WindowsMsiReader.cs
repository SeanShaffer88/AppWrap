using System.Runtime.InteropServices;
using AppWrap.Core;
using AppWrap.Core.Audit;

namespace AppWrap.Build.Audit;

/// <summary>
/// The real <see cref="IMsiReader"/>: opens the MSI database read-only
/// through msi.dll (Windows Installer) and reads the File, Component,
/// Registry, Shortcut, Directory and ServiceInstall tables. Nothing is
/// installed and no custom action runs — the database is only queried.
/// Windows-only at runtime: on any other platform (or when the Windows
/// Installer service is unavailable) it fails with an actionable error,
/// and callers fall back to <see cref="FixtureMsiReader"/> data or report
/// the audit as limited. Runtime behavior on Windows is Unverified.
/// </summary>
public sealed class WindowsMsiReader : IMsiReader
{
    public MsiTables Read(string msiPath)
    {
        if (!OperatingSystem.IsWindows())
            throw new UserInputException("Reading an .msi file's tables needs Windows (msi.dll). On this machine, audit a recorded MSI fixture or the installed folder instead.");
        var full = Path.GetFullPath(msiPath);
        uint database = 0;
        try
        {
            // MSIDBOPEN_READONLY is the persist value (LPCTSTR)0.
            var error = MsiOpenDatabase(full, IntPtr.Zero, out database);
            if (error != 0 || database == 0)
                throw new UserInputException($"The .msi file could not be opened for reading (Windows Installer error {error}).");
            var components = Query(database, "SELECT `Component`, `Directory_` FROM `Component`",
                record => (Key: Text(record, 1) ?? "", Directory: Text(record, 2)))
                .ToDictionary(row => row.Key, row => row.Directory, StringComparer.OrdinalIgnoreCase);
            var files = Query(database, "SELECT `File`, `Component_`, `FileName` FROM `File`",
                record =>
                {
                    var component = Text(record, 2) ?? "";
                    return new MsiFileRow(Text(record, 1) ?? "", component, Text(record, 3) ?? "",
                        components.TryGetValue(component, out var directory) ? directory : null);
                });
            var registry = Query(database, "SELECT `Registry`, `Root`, `Key`, `Name`, `Value`, `Component_` FROM `Registry`",
                record => new MsiRegistryRow(Text(record, 1) ?? "", Integer(record, 2), Text(record, 3) ?? "", Text(record, 4), Text(record, 5), Text(record, 6) ?? ""));
            var shortcuts = Query(database, "SELECT `Shortcut`, `Directory_`, `Name`, `Component_`, `Target` FROM `Shortcut`",
                record => new MsiShortcutRow(Text(record, 1) ?? "", Text(record, 2) ?? "", Text(record, 3) ?? "", Text(record, 4) ?? "", Text(record, 5)));
            var directories = Query(database, "SELECT `Directory`, `Directory_Parent`, `DefaultDir` FROM `Directory`",
                record => new MsiDirectoryRow(Text(record, 1) ?? "", Text(record, 2), Text(record, 3) ?? ""));
            var services = Query(database, "SELECT `ServiceInstall`, `Name`, `DisplayName`, `Component_` FROM `ServiceInstall`",
                record => new MsiServiceRow(Text(record, 1) ?? "", Text(record, 2) ?? "", Text(record, 3), Text(record, 4) ?? ""));
            return new MsiTables(files, registry, shortcuts, directories, services);
        }
        finally
        {
            if (database != 0) _ = MsiCloseHandle(database);
        }
    }

    /// <summary>Runs one SELECT and maps each record; a missing table reads as empty (not every MSI has Shortcuts or Services).</summary>
    private static List<T> Query<T>(uint database, string sql, Func<uint, T> map)
    {
        var rows = new List<T>();
        if (MsiDatabaseOpenView(database, sql, out var view) != 0 || view == 0) return rows;
        try
        {
            if (MsiViewExecute(view, 0) != 0) return rows;
            while (MsiViewFetch(view, out var record) == 0 && record != 0)
            {
                try { rows.Add(map(record)); }
                finally { _ = MsiCloseHandle(record); }
            }
        }
        finally { _ = MsiCloseHandle(view); }
        return rows;
    }

    private static string? Text(uint record, uint field)
    {
        var length = 0;
        // First call with an empty buffer reports the size (and 234 = more data).
        var status = MsiRecordGetString(record, field, null, ref length);
        if (status == 5) return null; // ERROR_INVALID_HANDLE shouldn't happen; treat other failures below
        if (length == 0)
        {
            var buffer = new char[1];
            return MsiRecordGetString(record, field, buffer, ref length) == 0 ? "" : null;
        }
        var chars = new char[length + 1];
        var count = chars.Length;
        return MsiRecordGetString(record, field, chars, ref count) == 0 ? new string(chars, 0, count) : null;
    }

    private static int Integer(uint record, uint field)
    {
        var value = MsiRecordGetInteger(record, field);
        return value == int.MinValue ? -1 : value; // MSI_NULL_INTEGER
    }

    [DllImport("msi.dll", CharSet = CharSet.Unicode, EntryPoint = "MsiOpenDatabaseW")]
    private static extern uint MsiOpenDatabase(string databasePath, IntPtr persist, out uint database);

    [DllImport("msi.dll", CharSet = CharSet.Unicode, EntryPoint = "MsiDatabaseOpenViewW")]
    private static extern uint MsiDatabaseOpenView(uint database, string query, out uint view);

    [DllImport("msi.dll")]
    private static extern uint MsiViewExecute(uint view, uint record);

    [DllImport("msi.dll")]
    private static extern uint MsiViewFetch(uint view, out uint record);

    [DllImport("msi.dll", CharSet = CharSet.Unicode, EntryPoint = "MsiRecordGetStringW")]
    private static extern uint MsiRecordGetString(uint record, uint field, char[]? value, ref int length);

    [DllImport("msi.dll")]
    private static extern int MsiRecordGetInteger(uint record, uint field);

    [DllImport("msi.dll")]
    private static extern uint MsiCloseHandle(uint handle);
}
