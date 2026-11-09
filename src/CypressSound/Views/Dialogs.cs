// Small windows built in code: name prompt, auto-profile editor, settings, and the on-screen switch message.
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using CypressSound.Eq;
using CypressSound.Services;

namespace CypressSound.Views;

abstract class DarkDialog : Window
{
    protected readonly StackPanel Body = new() { Margin = new Thickness(20) };
    protected DarkDialog(string title, double width)
    {
        Title = title;
        Width = width;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Ui.B("Bg");
        ShowInTaskbar = false;
        Theme.TitleBar(this);
        Content = Body;
    }

    protected StackPanel Buttons(params Button[] buttons)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        foreach (var b in buttons) sp.Children.Add(b);
        Body.Children.Add(sp);
        return sp;
    }

    protected static TextBlock Label(string s) { var t = Ui.Text(s, 12, true, "Muted"); t.Margin = new Thickness(0, 10, 0, 4); return t; }
}

sealed class TextPrompt : DarkDialog
{
    readonly TextBox _box;
    TextPrompt(string title, string prompt, string value) : base(title, 420)
    {
        Body.Children.Add(Ui.Text(prompt));
        _box = new TextBox { Text = value, Margin = new Thickness(0, 8, 0, 0) };
        Body.Children.Add(_box);
        var ok = Ui.Btn("OK", () => DialogResult = true, primary: true);
        ok.IsDefault = true;
        var cancel = Ui.Btn("Cancel", () => DialogResult = false);
        cancel.IsCancel = true;
        Buttons(cancel, ok);
        Loaded += (_, _) => { _box.Focus(); _box.SelectAll(); };
    }

    public static string? Ask(Window owner, string title, string prompt, string value)
    {
        var w = new TextPrompt(title, prompt, value) { Owner = owner };
        return w.ShowDialog() == true ? w._box.Text : null;
    }
}

sealed class ProfileWindow : DarkDialog
{
    public AutoProfile Result { get; }
    public bool Deleted { get; private set; }

    public ProfileWindow(AutoProfile? existing) : base(existing == null ? "New auto profile" : "Edit auto profile", 480)
    {
        Result = existing ?? new AutoProfile { Name = "New profile" };
        var a = App.Audio;
        Body.Children.Add(Ui.Text("While one of these programs is running, Cypress Sound switches the output and EQ for you, and puts them back when it closes.", 13, wrap: 430));

        Body.Children.Add(Label("NAME"));
        var name = new TextBox { Text = Result.Name };
        Body.Children.Add(name);

        Body.Children.Add(Label("PROGRAMS (comma separated, e.g. D2R, vlc)"));
        var progs = new TextBox { Text = string.Join(", ", Result.Programs) };
        Body.Children.Add(progs);
        var running = new ComboBox { Margin = new Thickness(0, 6, 0, 0), ToolTip = "Add a program that's running right now" };
        running.Items.Add("Add a running program…");
        foreach (var n in Process.GetProcesses().Where(p => { try { return p.MainWindowHandle != IntPtr.Zero; } catch { return false; } })
                     .Select(p => p.ProcessName).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n))
            running.Items.Add(n);
        running.SelectedIndex = 0;
        running.SelectionChanged += (_, _) =>
        {
            if (running.SelectedIndex <= 0) return;
            var n = (string)running.SelectedItem;
            progs.Text = string.IsNullOrWhiteSpace(progs.Text) ? n : progs.Text.TrimEnd(' ', ',') + ", " + n;
            running.SelectedIndex = 0;
        };
        Body.Children.Add(running);

        Body.Children.Add(Label("SWITCH OUTPUT TO"));
        var outs = new ComboBox();
        outs.Items.Add(new ComboBoxItem { Content = "Keep the current output", Tag = null });
        foreach (var d in a.Outputs) outs.Items.Add(new ComboBoxItem { Content = $"{d.Icon}  {a.NameOf(d)}", Tag = d.Id });
        outs.SelectedItem = outs.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string?)i.Tag == Result.OutputId) ?? outs.Items[0];
        Body.Children.Add(outs);

        Body.Children.Add(Label("EQ ON SPEAKERS / TV"));
        var presets = new ComboBox();
        presets.Items.Add("Keep my EQ");
        foreach (var p in Settings.ProfilePresets()) presets.Items.Add(p.Name);
        presets.SelectedItem = Result.Preset ?? "Keep my EQ";
        if (presets.SelectedIndex < 0) presets.SelectedIndex = 0;
        Body.Children.Add(presets);

        Body.Children.Add(Label("EQ ON HEADPHONES 🎧"));
        var hp = new ComboBox();
        hp.Items.Add("Same as speakers / TV");
        foreach (var p in Settings.ProfilePresets()) hp.Items.Add(p.Name);
        hp.SelectedItem = Result.HeadphonePreset ?? "Same as speakers / TV";
        if (hp.SelectedIndex < 0) hp.SelectedIndex = 0;
        Body.Children.Add(hp);

        var night = new CheckBox { Content = "Night mode on while it runs", IsChecked = Result.NightMode, Margin = new Thickness(0, 12, 0, 0) };
        Body.Children.Add(night);

        var save = Ui.Btn("Save", () =>
        {
            Result.Name = string.IsNullOrWhiteSpace(name.Text) ? "Profile" : name.Text.Trim();
            Result.Programs = progs.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            Result.OutputId = (string?)((ComboBoxItem)outs.SelectedItem).Tag;
            Result.Preset = presets.SelectedIndex <= 0 ? null : (string)presets.SelectedItem;
            Result.HeadphonePreset = hp.SelectedIndex <= 0 ? null : (string)hp.SelectedItem;
            Result.NightMode = night.IsChecked == true;
            DialogResult = true;
        }, primary: true);
        save.IsDefault = true;
        var cancel = Ui.Btn("Cancel", () => DialogResult = false);
        cancel.IsCancel = true;
        var bar = Buttons(cancel, save);
        if (existing != null)
        {
            var del = Ui.Btn("Delete", () => { Deleted = true; DialogResult = true; });
            del.Foreground = Ui.B("Red");
            bar.Children.Insert(0, del);
        }
    }
}

sealed class SettingsWindow : DarkDialog
{
    public SettingsWindow() : base("Settings", 460)
    {
        var s = Settings.Current;
        var a = App.Audio;
        Body.Children.Add(Ui.Text("Settings", 20, true));
        Body.Children.Add(Ui.Switch("Start with Windows (in the tray)", s.StartWithWindows, on => { s.StartWithWindows = on; AutoStart.Apply(on); a.EqChanged(); }));
        Body.Children.Add(Ui.Switch("Closing the window keeps it running in the tray", s.CloseToTray, on => { s.CloseToTray = on; a.EqChanged(); }));
        Body.Children.Add(Ui.Switch("Hotkeys: Ctrl+Alt+1-9 outputs, Ctrl+Alt+0 next, Ctrl+Alt+E EQ", s.GlobalHotkeys, on => { s.GlobalHotkeys = on; App.Hotkeys?.Enable(on); a.EqChanged(); }));
        Body.Children.Add(Ui.Switch("Check for updates", s.CheckUpdates, on => { s.CheckUpdates = on; a.EqChanged(); }));

        Body.Children.Add(Label("LOOK"));
        var theme = new ComboBox();
        foreach (var t in Theme.All) theme.Items.Add(t.Name);
        theme.SelectedItem = Theme.Current.Name;
        var restart = Ui.Btn("Restart to finish applying", () => { Settings.Save(); ((App)Application.Current).Restart(); });
        restart.HorizontalAlignment = HorizontalAlignment.Left;
        restart.Margin = new Thickness(0, 8, 0, 0);
        restart.Visibility = Visibility.Collapsed;
        theme.SelectionChanged += (_, _) =>
        {
            if (theme.SelectedItem is not string n) return;
            s.Theme = n;
            Theme.Apply(n);
            a.EqChanged();
            restart.Visibility = Visibility.Visible;
        };
        Body.Children.Add(theme);
        Body.Children.Add(restart);

        Body.Children.Add(Label("DEVICES"));
        var unhide = Ui.Btn($"Show hidden devices ({s.Hidden.Count})", () => { s.Hidden.Clear(); a.Refresh(); Close(); });
        unhide.IsEnabled = s.Hidden.Count > 0;
        unhide.HorizontalAlignment = HorizontalAlignment.Left;
        Body.Children.Add(unhide);
        var sel = Ui.Btn("Equalizer APO device list…", () =>
        {
            var ip = ApoConfig.InstallPath();
            if (ip != null) Ui.RunApoTool(ip, "DeviceSelector.exe");
        });
        sel.HorizontalAlignment = HorizontalAlignment.Left;
        sel.IsEnabled = ApoConfig.Installed;
        Body.Children.Add(sel);
        Body.Children.Add(Ui.Btn("Windows sound settings", () => Ui.Open("ms-settings:sound")));

        Body.Children.Add(Label("ABOUT"));
        var status = Ui.Text($"Cypress Sound {Updater.Current.ToString(3)} by Cypress Works.\nSound engine: Equalizer APO {(ApoConfig.Installed ? "installed" : "not installed")}. Headphone data: AutoEq ({HeadphoneLibrary.All.Count:N0} models).", 12, brush: "Muted", wrap: 420);
        Body.Children.Add(status);
        var check = Ui.Btn("Check for updates now", () => { });
        check.Click += async (_, _) =>
        {
            check.IsEnabled = false;
            check.Content = "Checking…";
            try
            {
                App.PendingUpdate = await Updater.CheckAsync();
                check.Content = App.PendingUpdate == null ? "You have the latest version" : $"Version {App.PendingUpdate.Version.ToString(3)} is ready: see the main window";
                a.EqChanged();
            }
            catch { check.Content = "Couldn't reach GitHub. Try again later."; }
            check.IsEnabled = true;
        };
        check.HorizontalAlignment = HorizontalAlignment.Left;
        Body.Children.Add(check);
        var close = Ui.Btn("Done", Close, primary: true);
        close.IsCancel = true;
        Buttons(close);
    }
}

/// <summary>The little message in the bottom middle of the screen after a hotkey ("🎧 Headphones").</summary>
static class Osd
{
    static Window? _w;
    static TextBlock? _t;
    static DispatcherTimer? _timer;

    public static void Show(string text)
    {
        if (_w == null)
        {
            _t = new TextBlock { FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Margin = new Thickness(24, 12, 24, 12) };
            _w = new Window
            {
                WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, Topmost = true,
                ShowInTaskbar = false, ShowActivated = false, SizeToContent = SizeToContent.WidthAndHeight, Focusable = false,
                Content = new Border { Background = new SolidColorBrush(Color.FromArgb(230, 0x1D, 0x22, 0x1E)), BorderBrush = Ui.B("Green"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Child = _t },
            };
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.6) };
            _timer.Tick += (_, _) =>
            {
                _timer!.Stop();
                var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(250));
                fade.Completed += (_, _) => _w!.Hide();
                _w!.BeginAnimation(UIElement.OpacityProperty, fade);
            };
        }
        _t!.Text = text;
        _w.BeginAnimation(UIElement.OpacityProperty, null);
        _w.Opacity = 1;
        _w.Show();
        _w.UpdateLayout();
        var area = SystemParameters.WorkArea;
        _w.Left = area.Left + (area.Width - _w.ActualWidth) / 2;
        _w.Top = area.Bottom - _w.ActualHeight - 80;
        _timer!.Stop();
        _timer.Start();
    }
}
