using System.Text;

namespace AppWrap.Build;

internal static class BackendLog
{
    internal static async Task<string> ReadAsync(string path, CancellationToken token)
    {
        if (!File.Exists(path)) return "";
        await using var stream = File.OpenRead(path);
        var buffer = new byte[262144]; int count = 0;
        while (count < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(count), token).ConfigureAwait(false);
            if (read == 0) break;
            count += read;
        }
        var text = Decode(buffer.AsSpan(0, count));
        bool truncated = stream.Length > count || text.Length > 131072;
        return text[..Math.Min(text.Length, 131072)] + (truncated ? "\n[log truncated]" : "");
    }

    internal static string Decode(ReadOnlySpan<byte> bytes)
    {
        try
        {
        if (bytes.StartsWith(new byte[] { 0xff, 0xfe })) return new UnicodeEncoding(false, false, true).GetString(bytes[2..]);
        if (bytes.StartsWith(new byte[] { 0xfe, 0xff })) return new UnicodeEncoding(true, false, true).GetString(bytes[2..]);
        if (bytes.StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) return new UTF8Encoding(false, true).GetString(bytes[3..]);
        if (bytes.StartsWith(Encoding.Unicode.GetBytes("ERROR: "))) return new UnicodeEncoding(false, false, true).GetString(bytes);
        int oddNulls = 0;
        for (int i = 1; i < bytes.Length; i += 2) if (bytes[i] == 0) oddNulls++;
        // The upstream wizard writes no-BOM UTF16LE errors; makensis /O writes its
        // default compiler charset. English no-BOM UTF16 has alternating NUL bytes.
        if (bytes.Length >= 4 && oddNulls >= bytes.Length / 4) return new UnicodeEncoding(false, false, true).GetString(bytes);
        return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return "[invalid or unknown compiler charset; byte-preserving Latin-1 display]\n" + Encoding.Latin1.GetString(bytes) + "\n[raw log base64]\n" + Convert.ToBase64String(bytes);
        }
    }
}
