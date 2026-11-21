using System.Windows;

namespace AppWrap.Gui;

internal static class WindowPlacement
{
    // WPF reports this primary work area in device-independent units. Conservatively
    // restoring on that real monitor avoids virtual-desktop gaps and mixed-DPI unit
    // conversions. A saved secondary-monitor position is intentionally moved here.
    public static Rect Clamp(Rect bounds, double minWidth, double minHeight)
    {
        var work = SystemParameters.WorkArea;
        var width = Finite(bounds.Width) ? bounds.Width : 880;
        var height = Finite(bounds.Height) ? bounds.Height : 620;
        width = Math.Clamp(width, Math.Min(minWidth, work.Width), work.Width);
        height = Math.Clamp(height, Math.Min(minHeight, work.Height), work.Height);
        var left = Finite(bounds.Left) ? bounds.Left : work.Left;
        var top = Finite(bounds.Top) ? bounds.Top : work.Top;
        return new Rect(Math.Clamp(left, work.Left, work.Right - width),
            Math.Clamp(top, work.Top, work.Bottom - height), width, height);
    }

    public static Rect Clamp(Rect bounds) => Clamp(bounds, 680, 560);
    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
