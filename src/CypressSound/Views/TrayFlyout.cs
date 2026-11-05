// The quick panel that pops up from the tray icon: outputs, quick toggles, and every app's volume and output.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CypressSound.Audio;
using CypressSound.Services;

namespace CypressSound.Views;

sealed class TrayFlyout : Window
{
    static AudioController A => App.Audio;
    readonly StackPanel _body = new() { Margin = new Thickness(16) };

    public TrayFlyout()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        Width = 380;
        SizeToContent = SizeToContent.Height;
        Content = new Border
        {
            Background = Ui.B("Bg"), BorderBrush = Ui.B("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14),
            Child = new ScrollViewer { MaxHeight = 640, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _body },
        };
        Deactivated += (_, _) => Hide();
        A.Changed += () => { if (IsVisible) Build(); };
    }

    public void ShowNearTray()
    {
        Build();
        Show();
        UpdateLayout();
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - 12;
        Top = area.Bottom - ActualHeight - 12;
        Activate();
    }

    void Build()
    {
        _body.Children.Clear();
        var head = new DockPanel();
        var open = Ui.Btn("Open", () => { Hide(); ((App)Application.Current).ShowMain(); });
        open.Margin = new Thickness(0);
        DockPanel.SetDock(open, Dock.Right);
        head.Children.Add(open);
        head.Children.Add(Ui.Text("Cypress Sound", 16, true));
        _body.Children.Add(head);

        _body.Children.Add(Section("OUTPUT"));
        foreach (var d in A.Outputs)
        {
            var on = d.Id == A.DefaultOutput;
            var id = d.Id;
            var row = new Button
            {
                Style = Ui.S("DeviceRow"),
                Background = on ? Ui.B("Panel") : Brushes.Transparent,
                BorderBrush = on ? Ui.B("Green") : Brushes.Transparent,
                Content = new TextBlock { Text = $"{d.Icon}   {A.NameOf(d)}", FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis },
            };
            row.Click += (_, _) => A.SwitchTo(id);
            _body.Children.Add(row);
        }

        if (A.DefaultOutput is { } def)
        {
            var vol = Ui.Slider(0, 100, Math.Round(A.Devices.DeviceVolume(def) * 100), 1, v => A.Devices.SetDeviceVolume(def, (float)(v / 100)));
            _body.Children.Add(vol);
            var eq = Settings.EqFor(def);
            var toggles = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            toggles.Children.Add(Ui.Chip("EQ", eq.Enabled, () => { eq.Enabled = !eq.Enabled; A.EqChanged(); }));
            toggles.Children.Add(Ui.Chip("🌙 Night", eq.NightMode, () => { eq.NightMode = !eq.NightMode; A.EqChanged(); }));
            toggles.Children.Add(Ui.Chip("Mono", eq.Mono, () => { eq.Mono = !eq.Mono; A.EqChanged(); }));
            _body.Children.Add(toggles);
            var preset = new ComboBox { Margin = new Thickness(0, 2, 0, 0) };
            foreach (var p in Settings.AllPresets(false)) preset.Items.Add(p.Name);
            preset.SelectedItem = eq.Edited ? null : eq.Preset;
            preset.SelectionChanged += (_, _) =>
            {
                var p = Settings.AllPresets(false).FirstOrDefault(x => x.Name == (string?)preset.SelectedItem);
                if (p == null) return;
                eq.Bands = (double[])p.Bands.Clone();
                eq.Preset = p.Name;
                eq.Edited = false;
                eq.Enabled = true;
                eq.NightMode = p.NightMode;
                eq.BassBoost = p.BassBoost;
                A.EqChanged();
            };
            _body.Children.Add(preset);
        }

        _body.Children.Add(Section("APPS  ·  volume and which output each one uses"));
        var apps = A.Devices.Apps();
        if (apps.Count == 0) _body.Children.Add(Ui.Text("No apps are playing sound right now.", 12, brush: "Muted"));
        foreach (var app in apps)
        {
            var pid = app.ProcessId;
            var box = new StackPanel { Margin = new Thickness(0, 4, 0, 8) };
            var top = new DockPanel();
            var outs = new ComboBox { Width = 150, FontSize = 12, MinHeight = 26 };
            outs.Items.Add(new ComboBoxItem { Content = "Default output", Tag = null });
            foreach (var d in A.Outputs) outs.Items.Add(new ComboBoxItem { Content = A.NameOf(d), Tag = d.Id });
            outs.SelectedItem = outs.Items.Cast<ComboBoxItem>().FirstOrDefault(i => i.Tag is string t && app.DeviceId != null && app.DeviceId.Contains(t, StringComparison.OrdinalIgnoreCase)) ?? outs.Items[0];
            outs.SelectionChanged += (_, _) =>
            {
                try { WindowsAudio.SetAppDevice(pid, (string?)((ComboBoxItem)outs.SelectedItem).Tag); }
                catch (Exception e) { App.Log("SetAppDevice: " + e.Message); }
            };
            DockPanel.SetDock(outs, Dock.Right);
            top.Children.Add(outs);
            top.Children.Add(new TextBlock { Text = app.Name, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            box.Children.Add(top);
            box.Children.Add(Ui.Slider(0, 100, Math.Round(app.Volume * 100), 1, v => A.Devices.SetAppVolume(pid, (float)(v / 100))));
            _body.Children.Add(box);
        }
    }

    static TextBlock Section(string s)
    {
        var t = Ui.Text(s, 11, true, "Muted");
        t.Margin = new Thickness(2, 14, 0, 6);
        return t;
    }
}
