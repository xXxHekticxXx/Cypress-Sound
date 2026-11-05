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
        void Paint() { t.Foreground = b.IsChecked == true ? new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11)) : B("Ink"); t.FontWeight = b.IsChecked == true ? FontWeights.SemiBold : FontWeights.Normal; }
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
    public static bool RunAsAdmin(string exe, string args = "", bool wait = true)
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true, Verb = "runas" });
            if (wait) p?.WaitForExit();
            return true;
        }
        catch { return false; }
    }
}
