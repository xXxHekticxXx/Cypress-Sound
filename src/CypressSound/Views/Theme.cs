// Colour themes. The app's brushes live in App.xaml; a theme just recolours them in place,
// so switching in Settings changes every open window at once.
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace CypressSound.Views;

public static class Theme
{
    public sealed record Palette(string Name, bool Dark, string Bg, string Bg2, string Panel, string Line, string Ink, string Muted, string Accent, string AccentDark, string Track, string Hover);

    public static readonly Palette[] All =
    {
        new("Midnight", true,  "#0E1116", "#0A0C10", "#161A21", "#252B35", "#EEF1F6", "#8B94A5", "#5B8CFF", "#22356B", "#2B313C", "#1D222B"),
        new("Graphite", true,  "#18181B", "#121214", "#222226", "#303036", "#F4F4F5", "#9A9AA3", "#2DD4BF", "#12514A", "#34343A", "#2A2A2F"),
        new("Light",    false, "#F5F6F8", "#EBEDF1", "#FFFFFF", "#D9DDE4", "#1A1D23", "#6B7280", "#3B6CF6", "#DCE5FE", "#CDD2DA", "#EEF1F6"),
        new("Cypress green", true, "#151916", "#101311", "#1D221E", "#2C332D", "#E9EEE9", "#8D998E", "#3D9A62", "#1F5C36", "#3A423B", "#242A25"),
    };

    public static Palette Current { get; private set; } = All[0];

    public static void Apply(string? name)
    {
        var p = All.FirstOrDefault(t => t.Name == name) ?? All[0];
        Current = p;
        var r = Application.Current.Resources;
        void Set(string key, string hex)
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            r[key] = new SolidColorBrush(c); // everything in XAML points at these with DynamicResource, so it all updates
        }
        Set("Bg", p.Bg); Set("Bg2", p.Bg2); Set("Panel", p.Panel); Set("Line", p.Line);
        Set("Ink", p.Ink); Set("Muted", p.Muted);
        Set("Green", p.Accent); Set("GreenDark", p.AccentDark); // accent colour (key names kept from the first version)
        Set("Track", p.Track); Set("Hover", p.Hover);
        foreach (Window w in Application.Current.Windows) TitleBar(w);
    }

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Dark (or light) Windows title bar to match the theme. Call once the window has a handle.</summary>
    public static void TitleBar(Window w)
    {
        try
        {
            var h = new WindowInteropHelper(w).Handle;
            if (h == IntPtr.Zero) { w.SourceInitialized += (_, _) => TitleBar(w); return; }
            int on = Current.Dark ? 1 : 0;
            DwmSetWindowAttribute(h, 20, ref on, sizeof(int));   // DWMWA_USE_IMMERSIVE_DARK_MODE
            var c = (Color)ColorConverter.ConvertFromString(Current.Bg2);
            int bgr = c.R | (c.G << 8) | (c.B << 16);
            DwmSetWindowAttribute(h, 35, ref bgr, sizeof(int));  // DWMWA_CAPTION_COLOR (Windows 11)
        }
        catch { /* older Windows: keep the normal title bar */ }
    }
}
