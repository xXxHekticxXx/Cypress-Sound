using System.Linq;
// Small helpers for building the UI in code.
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace CypressSound.Views;

static class Ui
{
    public static Brush B(string key) => (Brush)Application.Current.Resources[key];
    public static Style S(string key) => (Style)Application.Current.Resources[key];

    public static TextBlock Text(string s, double size = 14, bool bold = false, string? brush = null, double wrap = 0)
    {
        var t = new TextBlock { Text = s, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal };
        if (brush != null) t.Foreground = B(brush);
        if (wrap > 0) { t.TextWrapping = TextWrapping.Wrap; t.MaxWidth = wrap; }
        return t;
    }

    public static Button Btn(string s, Action click, bool primary = false)
    {
        var b = new Button { Content = s };
        if (primary) b.Style = S("Primary");
        b.Click += (_, _) => click();
        return b;
    }

    public static ToggleButton Chip(string s, bool on, Action click)
    {
        // The app-wide TextBlock style would keep the text light, so color it here (dark text on the light "selected" chip).
        var t = new TextBlock { Text = s, FontSize = 13 };
        var b = new ToggleButton { Content = t, IsChecked = on, Style = S("Chip") };
        void Paint() { t.Foreground = b.IsChecked == true ? B("Bg") : B("Ink"); t.FontWeight = b.IsChecked == true ? FontWeights.SemiBold : FontWeights.Normal; }
        Paint();
        b.Checked += (_, _) => Paint();
        b.Unchecked += (_, _) => Paint();
        b.Click += (_, _) => click();
        return b;
    }

    public static CheckBox Switch(string s, bool on, Action<bool> changed)
    {
        var c = new CheckBox { Content = s, IsChecked = on, Style = S("Switch"), Margin = new Thickness(0, 4, 0, 4) };
        c.Click += (_, _) => changed(c.IsChecked == true);
        return c;
    }

    public static Slider Slider(double min, double max, double value, double step, Action<double> changed)
    {
        var s = new Slider { Minimum = min, Maximum = max, Value = value, SmallChange = step, LargeChange = step * 4, TickFrequency = step, IsSnapToTickEnabled = true, Margin = new Thickness(0, 6, 0, 2) };
        s.ValueChanged += (_, e) => changed(e.NewValue);
        return s;
    }

    public static Border Card(string title, params UIElement[] body)
    {
        var sp = new StackPanel();
        sp.Children.Add(Text(title, 13, true, "Muted"));
        foreach (var e in body) sp.Children.Add(e);
        return new Border { Background = B("Panel"), CornerRadius = new CornerRadius(14), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 12, 12), Child = sp };
    }

    public static Border Banner(string text, string brush, params Button[] actions)
    {
        var dp = new DockPanel();
        var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var a in actions) { a.Margin = new Thickness(8, 0, 0, 0); sp.Children.Add(a); }
        DockPanel.SetDock(sp, Dock.Right);
        dp.Children.Add(sp);
        dp.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Foreground = B("Ink") });
        return new Border { BorderBrush = B(brush), BorderThickness = new Thickness(1), Background = new SolidColorBrush(Color.FromArgb(40, 0xE0, 0xA3, 0x3D)), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 10), Child = dp };
    }

    public static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch { }
    }

    /// <summary>Starts a program as administrator (Windows shows its yes/no prompt). Returns false if the user said no.</summary>
    /// <summary>Starts Equalizer APO's Device Selector. It is a Qt program: it has to start in its own folder and
    /// find its "platforms" plugin there, or it fails with "no Qt platform plugin could be initialized"
    /// (other Qt programs on the PC can also point it at the wrong plugin folder through QT_* variables).</summary>
    public static bool RunApoTool(string folder, string exeName)
    {
        try
        {
            // find the Qt platform plugin wherever this version of Equalizer APO keeps it
            var plat = System.IO.Path.Combine(folder, "platforms");
            if (!System.IO.File.Exists(System.IO.Path.Combine(plat, "qwindows.dll")))
            {
                var found = System.IO.Directory.EnumerateFiles(folder, "qwindows*.dll", System.IO.SearchOption.AllDirectories).FirstOrDefault();
                if (found != null) plat = System.IO.Path.GetDirectoryName(found)!;
            }
            if (!System.IO.Directory.Exists(plat))
            {
                System.Windows.MessageBox.Show("Equalizer APO's install is missing some of its files (the \"platforms\" folder), so its device list can't open.\n\nReinstall Equalizer APO from its website, then try again.", "Cypress Sound");
                return false;
            }
            var cmd = $"/c cd /d \"{folder}\" && set QT_PLUGIN_PATH={System.IO.Path.GetDirectoryName(plat)}&& set QT_QPA_PLATFORM_PLUGIN_PATH={plat}&& set QT_QPA_PLATFORM=&& start \"\" \"{System.IO.Path.Combine(folder, exeName)}\"";
            Process.Start(new ProcessStartInfo("cmd.exe", cmd) { UseShellExecute = true, Verb = "runas", WorkingDirectory = folder, WindowStyle = ProcessWindowStyle.Hidden });
            return true;
        }
        catch { return false; }
    }

    public static bool RunAsAdmin(string exe, string args = "", bool wait = true)
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true, Verb = "runas", WorkingDirectory = System.IO.Path.GetDirectoryName(exe) ?? "" });
            if (wait) p?.WaitForExit();
            return true;
        }
        catch { return false; }
    }
}
