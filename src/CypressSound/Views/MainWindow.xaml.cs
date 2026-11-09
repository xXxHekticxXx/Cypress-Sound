using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using CypressSound.Audio;
using CypressSound.Eq;
using CypressSound.Services;

namespace CypressSound.Views;

public partial class MainWindow : Window
{
    static AudioController A => App.Audio;
    string? _selected;
    string? _builtFor = "\0"; // nothing built yet
    bool _loading;
    readonly DispatcherTimer _meter = new() { Interval = TimeSpan.FromMilliseconds(60) };
    Border? _meterBar;
    TextBlock? _meterText;
    readonly List<Slider> _bands = new();
    readonly List<TextBlock> _bandValues = new();

    public MainWindow()
    {
        InitializeComponent();
        Theme.TitleBar(this);
        Theme.Changed += () => { _builtFor = "\0"; OnChanged(); };
        SideCol.Width = new GridLength(Math.Clamp(Settings.Current.SidebarWidth, 220, 640));
        SideSplit.DragCompleted += (_, _) => { Settings.Current.SidebarWidth = SideCol.ActualWidth; A.EqChanged(); };
        VersionText.Text = "v" + Updater.Current.ToString(3);
        SettingsBtn.Click += (_, _) => new SettingsWindow { Owner = this }.ShowDialog();
        AddProfileBtn.Click += (_, _) => EditProfile(null);
        EqPower.Click += (_, _) => { if (Eq != null) { Eq.Enabled = EqPower.IsChecked == true; A.EqChanged(); } };
        A.Changed += OnChanged;
        _meter.Tick += (_, _) => UpdateMeter();
        IsVisibleChanged += (_, _) => { if (IsVisible) _meter.Start(); else _meter.Stop(); };
        Curve.SizeChanged += (_, _) => DrawCurve();
        _selected = Settings.Current.LastSelected;
        if (AudioController.Demo)
        {
            WindowState = WindowState.Maximized;
            var e = Settings.EqFor(A.Outputs[1].Id);
            e.Bands = new double[] { 2, 2, 1, 0, 0, 1, 3, 3, 1, 0 };
            e.Preset = "Sound bar: clear dialogue";
            e.Edited = false;
            e.SoundBar = true;
            e.SbSub = 4;
            e.SbVoice = 2;
            e.SbSurround = 0.6;
            e.Boost = true;
            e.BoostDb = 6;
            Settings.Current.SoundBarOpen = true;
            _selected = A.Outputs[1].Id;
            A.SwitchTo(A.Outputs[1].Id);
            var live = Environment.GetCommandLineArgs().SkipWhile(a => a != "--switch-theme").Skip(1).FirstOrDefault();
            if (live != null)
            {
                // screenshot check: change theme while the window is open, like picking it in Settings
                var tl = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                tl.Tick += (_, _) => { tl.Stop(); Theme.Apply(live); };
                tl.Start();
            }
            if (Environment.GetCommandLineArgs().Contains("--open-dropdown"))
            {
                // screenshot check: open the auto-profile editor with its output list dropped down
                var t0 = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                t0.Tick += (_, _) =>
                {
                    t0.Stop();
                    var w = new ProfileWindow(Settings.Current.Profiles.Last()) { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterScreen };
                    w.Show();
                    var t1 = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                    t1.Tick += (_, _) =>
                    {
                        t1.Stop();
                        var combos = new List<ComboBox>();
                        void Walk(object o) { if (o is ComboBox c) combos.Add(c); if (o is DependencyObject dobj) foreach (var ch in LogicalTreeHelper.GetChildren(dobj)) Walk(ch); }
                        Walk(w);
                        if (combos.Count > 1) combos[1].IsDropDownOpen = true;
                    };
                    t1.Start();
                };
                t0.Start();
            }
            var shot = Environment.GetCommandLineArgs().SkipWhile(a => a != "--shot").Skip(1).FirstOrDefault();
            if (shot != null)
            {
                var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                t.Tick += (_, _) => { t.Stop(); SavePanel(shot); };
                t.Start();
            }
        }
        OnChanged();
    }

    AudioDevice? Device => A.Outputs.Concat(A.Inputs).FirstOrDefault(d => d.Id == _selected);
    DeviceEq? Eq => _selected == null ? null : Settings.EqFor(_selected);
    bool IsMic => Device?.Flow == DataFlow.Capture;

    protected override void OnClosing(CancelEventArgs e)
    {
        if (Settings.Current.CloseToTray)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
        ((App)Application.Current).Quit();
    }

    void OnChanged()
    {
        if (Device == null) _selected = A.DefaultOutput ?? A.Outputs.FirstOrDefault()?.Id;
        BuildDeviceLists();
        BuildProfiles();
        BuildBanners();
        // rebuild the sliders when a profile switched on or off and moved the EQ underneath them
        if (_builtFor != _selected || A.ProfileStamp != _eqStamp) { _eqStamp = A.ProfileStamp; BuildEq(); }
        else { EqPower.IsChecked = Eq?.Enabled; DrawCurve(); }
    }

    void Select(AudioDevice d)
    {
        _selected = d.Id;
        Settings.Current.LastSelected = d.Id;
        if (d.Flow == DataFlow.Render ? d.Id != A.DefaultOutput : d.Id != A.DefaultInput) A.SwitchTo(d.Id);
        else OnChanged();
    }

    // ---------- left column ----------

    void BuildDeviceLists()
    {
        Fill(OutputList, A.Outputs, A.DefaultOutput);
        Fill(InputList, A.Inputs, A.DefaultInput);
    }

    void Fill(StackPanel list, IReadOnlyList<AudioDevice> devices, string? def)
    {
        list.Children.Clear();
        if (devices.Count == 0) list.Children.Add(Ui.Text("None found", 13, brush: "Muted"));
        foreach (var d in devices)
        {
            var isDef = d.Id == def;
            var sel = d.Id == _selected;
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(Ui.Text(d.Icon, 18));
            var names = new StackPanel();
            names.Children.Add(new TextBlock { Text = A.NameOf(d), FontWeight = isDef ? FontWeights.SemiBold : FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis });
            var eq = Settings.EqFor(d.Id);
            var sub = isDef ? "In use" : "";
            var eqText = !eq.Enabled ? "EQ off" : eq.Edited ? "Custom EQ" : eq.Preset;
            sub = sub.Length > 0 ? $"{sub}  ·  {eqText}" : eqText;
            names.Children.Add(new TextBlock { Text = sub, FontSize = 11, Foreground = Ui.B(isDef ? "Green" : "Muted"), TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetColumn(names, 1);
            g.Children.Add(names);
            if (d.Flow == DataFlow.Render && Settings.Current.Hotkey.TryGetValue(d.Id, out var n) && Settings.Current.GlobalHotkeys)
            {
                var hk = new Border { Background = Ui.B("Bg"), CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 2, 6, 2), VerticalAlignment = VerticalAlignment.Center, Child = Ui.Text($"Ctrl+Alt+{n}", 10, brush: "Muted"), ToolTip = "Switch to this output from anywhere, even in a game" };
                Grid.SetColumn(hk, 2);
                g.Children.Add(hk);
            }
            var row = new Button
            {
                Style = Ui.S("DeviceRow"),
                Content = g,
                Background = sel ? Ui.B("Panel") : Brushes.Transparent,
                BorderBrush = isDef ? Ui.B("Green") : sel ? Ui.B("Line") : Brushes.Transparent,
                ToolTip = $"{d.Name}\n{d.Adapter}\nClick: use this device and edit its EQ.  Right-click: rename, hide, hotkey.",
            };
            var dev = d;
            row.Click += (_, _) => Select(dev);
            row.ContextMenu = DeviceMenu(dev);
            list.Children.Add(row);
        }
    }

    ContextMenu DeviceMenu(AudioDevice d)
    {
        var m = new ContextMenu();
        void Add(string text, Action a) { var i = new MenuItem { Header = text }; i.Click += (_, _) => a(); m.Items.Add(i); }
        Add("Rename…", () =>
        {
            var name = TextPrompt.Ask(this, "Rename device", "Name shown in Cypress Sound:", A.NameOf(d));
            if (name == null) return;
            if (string.IsNullOrWhiteSpace(name) || name == d.Name) Settings.Current.Nickname.Remove(d.Id);
            else Settings.Current.Nickname[d.Id] = name.Trim();
            A.Refresh();
        });
        if (d.Flow == DataFlow.Render)
        {
            var hk = new MenuItem { Header = "Hotkey" };
            for (int n = 1; n <= 9; n++)
            {
                var num = n;
                var i = new MenuItem { Header = $"Ctrl+Alt+{n}", IsCheckable = true, IsChecked = Settings.Current.Hotkey.TryGetValue(d.Id, out var cur) && cur == n };
                i.Click += (_, _) =>
                {
                    foreach (var k in Settings.Current.Hotkey.Where(kv => kv.Value == num).Select(kv => kv.Key).ToList()) Settings.Current.Hotkey[k] = 0;
                    Settings.Current.Hotkey[d.Id] = num;
                    A.Refresh();
                };
                hk.Items.Add(i);
            }
            m.Items.Add(hk);
        }
        Add("Turn Equalizer APO on for devices…", OpenDeviceSelector);
        Add("Hide this device", () => { Settings.Current.Hidden.Add(d.Id); A.Refresh(); });
        return m;
    }

    void BuildProfiles()
    {
        ProfileList.Children.Clear();
        foreach (var p in Settings.Current.Profiles)
        {
            var active = A.ActiveProfile?.Name == p.Name;
            var dp = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var edit = new Button { Content = "Edit", Padding = new Thickness(10, 4, 10, 4), MinHeight = 28, Margin = new Thickness(6, 0, 0, 0) };
            var prof = p;
            edit.Click += (_, _) => EditProfile(prof);
            DockPanel.SetDock(edit, Dock.Right);
            dp.Children.Add(edit);
            // the switch turns the profile on right now (the EQ moves to its tuning) and it also comes on by itself
            // whenever one of its programs starts; off puts your own EQ back
            var sw = Ui.Switch("", active || p.Enabled && A.Testing == p, on =>
            {
                prof.Enabled = on;
                if (on) A.TestProfile(prof);
                else if (A.Testing == prof || A.ActiveProfile == prof) A.TestProfile(null);
                else A.EqChanged();
            });
            sw.Margin = new Thickness(0);
            DockPanel.SetDock(sw, Dock.Left);
            dp.Children.Add(sw);
            var names = new StackPanel { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            names.Children.Add(new TextBlock { Text = p.Name, TextTrimming = TextTrimming.CharacterEllipsis });
            names.Children.Add(new TextBlock
            {
                Text = ProfileStatus(p, active),
                FontSize = 11, Foreground = Ui.B(active ? "Green" : ProfileDeaf(p) ? "Warn" : "Muted"), TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = string.Join(", ", p.Programs),
            });
            dp.Children.Add(names);
            ProfileList.Children.Add(dp);
        }
    }

    /// <summary>True when the output this profile would change can't be EQ'd yet, so turning it on would do nothing.</summary>
    bool ProfileDeaf(AutoProfile p)
    {
        var target = A.Outputs.FirstOrDefault(o => o.Id == (p.OutputId ?? A.DefaultOutput));
        return p.Enabled && ApoConfig.Installed && target != null && !ApoConfig.EnabledFor(target);
    }

    string ProfileStatus(AutoProfile p, bool active)
    {
        if (active) return A.Testing == p ? "On now · " + (A.EffectiveEq(A.Outputs.FirstOrDefault(o => o.Id == (p.OutputId ?? A.DefaultOutput)) ?? A.Outputs[0]).Preset) : "Running now";
        if (ProfileDeaf(p))
        {
            var target = A.Outputs.First(o => o.Id == (p.OutputId ?? A.DefaultOutput));
            return $"⚠ Can't be heard: EQ isn't on for {A.NameOf(target)} (see the yellow banner)";
        }
        if (p.Enabled) return "Ready · comes on by itself when " + (p.Programs.FirstOrDefault() ?? "the program") + " opens";
        return string.Join(", ", p.Programs) + (p.HeadphonePreset != null ? " → speakers + 🎧 tunings" : p.Preset != null ? $" → {p.Preset}" : "");
    }

    void EditProfile(AutoProfile? p)
    {
        var w = new ProfileWindow(p) { Owner = this };
        if (w.ShowDialog() != true) return;
        if (w.Deleted) Settings.Current.Profiles.Remove(p!);
        else if (p == null) Settings.Current.Profiles.Add(w.Result);
        A.EqChanged();
    }

    // ---------- banners ----------

    void BuildBanners()
    {
        Banners.Children.Clear();
        if (App.PendingUpdate is { } u)
        {
            var b = Ui.Btn($"Update to {u.Version.ToString(3)}", () => { }, primary: true);
            b.Click += async (_, _) =>
            {
                b.IsEnabled = false;
                b.Content = "Downloading…";
                try
                {
                    await Updater.InstallAsync(u, new Progress<double>(f => b.Content = $"Downloading {f:P0}"));
                    ((App)Application.Current).Quit();
                }
                catch (Exception ex) { b.IsEnabled = true; b.Content = "Try again"; MessageBox.Show(this, "The update didn't download: " + ex.Message, "Cypress Sound"); }
            };
            Banners.Children.Add(Ui.Banner($"A new version of Cypress Sound is ready ({u.Version.ToString(3)}).", "Green", b));
        }
        if (A.LastError != null) Banners.Children.Add(Ui.Banner(A.LastError, "Red"));
        if (!ApoConfig.Installed)
        {
            Banners.Children.Add(Ui.Banner("The EQ needs Equalizer APO, the free sound engine Cypress Sound uses. The Cypress Sound installer adds it; run it again, or get it here.", "Warn",
                Ui.Btn("Get Equalizer APO", () => Ui.Open("https://sourceforge.net/projects/equalizerapo/"), primary: true)));
            return;
        }
        if (!ApoConfig.Ready())
        {
            Banners.Children.Add(Ui.Banner("One more step: let Cypress Sound control Equalizer APO. Windows will ask for permission once.", "Warn",
                Ui.Btn("Finish setup", () =>
                {
                    if (Ui.RunAsAdmin(Environment.ProcessPath!, "--setup-apo-config")) A.EqChanged();
                }, primary: true)));
        }
        if (A.ActiveProfile is { } ap && Device is { } dv && dv.Flow == DataFlow.Render && (ap.OutputId ?? A.DefaultOutput) == dv.Id)
        {
            var used = A.EffectiveEq(dv).Preset;
            var stop = A.Testing != null ? new[] { Ui.Btn("Turn off", () => { ap.Enabled = false; A.TestProfile(null); }, primary: true) } : Array.Empty<Button>();
            Banners.Children.Add(Ui.Banner($"🎮 {ap.Name} is on: the EQ below is set to \"{used}\". Your own EQ comes back when you turn it off." +
                (ApoConfig.EnabledFor(dv) ? "" : " You won't hear it until the EQ is switched on for this device (below)."), "Green", stop));
        }
        if (Device is { } d && !ApoConfig.EnabledFor(d))
        {
            Banners.Children.Add(Ui.Banner($"The EQ isn't switched on for {A.NameOf(d)} yet. Tick it in the Equalizer APO device list, then restart your computer (Windows only loads it at startup).", "Warn",
                Ui.Btn("Turn on EQ for this device", OpenDeviceSelector, primary: true)));
        }
    }

    void OpenDeviceSelector()
    {
        var ip = ApoConfig.InstallPath();
        if (ip == null) return;
        Ui.RunApoTool(ip, "DeviceSelector.exe");
    }

    // ---------- EQ ----------

    int _eqStamp = -1;

    void BuildEq()
    {
        _builtFor = _selected;
        _loading = true;
        var d = Device;
        var eq = Eq;
        ModeTabs.Children.Clear();
        PresetChips.Children.Clear();
        Cards.Children.Clear();
        SoundBarHost.Children.Clear();
        EqBody.Content = null;
        if (d == null || eq == null)
        {
            DeviceTitle.Text = "No sound devices found";
            DeviceSub.Text = "";
            _loading = false;
            return;
        }
        DeviceTitle.Text = A.NameOf(d);
        DeviceSub.Text = (d.Adapter.Length > 0 ? d.Adapter + "  ·  " : "") + (IsMic ? "Microphone EQ (for calls and streaming)" : "This EQ loads by itself whenever this output is in use");
        EqPower.IsChecked = eq.Enabled;

        var mode = Settings.Current.Mode;
        if (IsMic && mode == "headphone") mode = "graphic";
        foreach (var (key, label) in new[] { ("graphic", "10-band"), ("parametric", "Parametric"), ("headphone", "Headphone fix") })
        {
            if (IsMic && key == "headphone") continue;
            var k = key;
            ModeTabs.Children.Add(Ui.Chip(label, mode == key, () => { Settings.Current.Mode = k; BuildEq(); }));
        }

        if (mode == "graphic") BuildPresets(eq);
        EqBody.Content = mode switch
        {
            "parametric" => BuildParametric(eq),
            "headphone" => BuildHeadphone(eq),
            _ => BuildGraphic(eq),
        };
        BuildCards(eq);
        BuildSoundBar(eq);
        _loading = false;
        DrawCurve();
    }

    void BuildPresets(DeviceEq eq)
    {
        foreach (var p in Settings.AllPresets(IsMic))
        {
            var preset = p;
            var chip = Ui.Chip(p.Name, !eq.Edited && eq.Preset == p.Name, () => LoadPreset(preset));
            if (!p.Builtin)
            {
                var m = new ContextMenu();
                var del = new MenuItem { Header = "Delete preset" };
                del.Click += (_, _) => { Settings.Current.UserPresets.Remove(preset); BuildEq(); A.EqChanged(); };
                m.Items.Add(del);
                chip.ContextMenu = m;
            }
            PresetChips.Children.Add(chip);
        }
        PresetChips.Children.Add(Ui.Btn("+ Save as…", () =>
        {
            var name = TextPrompt.Ask(this, "Save preset", "Name for this EQ:", eq.Edited ? "My EQ" : eq.Preset + " (mine)");
            if (string.IsNullOrWhiteSpace(name)) return;
            Settings.Current.UserPresets.RemoveAll(x => x.Name == name && x.ForMic == IsMic);
            Settings.Current.UserPresets.Add(new Preset { Name = name.Trim(), Bands = (double[])eq.Bands.Clone(), BassBoost = eq.BassBoost, NightMode = eq.NightMode, ForMic = IsMic });
            eq.Preset = name.Trim();
            eq.Edited = false;
            BuildEq();
            A.EqChanged();
        }));
    }

    void LoadPreset(Preset p)
    {
        var eq = Eq!;
        eq.Bands = (double[])p.Bands.Clone();
        eq.Preset = p.Name;
        eq.Edited = false;
        eq.Enabled = true;
        if (!IsMic) { eq.NightMode = p.NightMode; eq.BassBoost = p.BassBoost; }
        BuildEq();
        A.EqChanged();
    }

    UIElement BuildGraphic(DeviceEq eq)
    {
        _bands.Clear();
        _bandValues.Clear();
        var g = new UniformGrid { Rows = 1, Columns = 10, Height = 250 };
        for (int i = 0; i < 10; i++)
        {
            var idx = i;
            var col = new DockPanel();
            var val = Ui.Text(Fmt(eq.Bands[i]), 12, true);
            val.HorizontalAlignment = HorizontalAlignment.Center;
            DockPanel.SetDock(val, Dock.Top);
            var lab = Ui.Text(DeviceEq.BandLabel[i], 12, brush: "Muted");
            lab.HorizontalAlignment = HorizontalAlignment.Center;
            DockPanel.SetDock(lab, Dock.Bottom);
            var s = new Slider { Style = Ui.S("BandSlider"), Value = eq.Bands[i], HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 6), ToolTip = $"{DeviceEq.BandLabel[i]} Hz. Double-click to reset." };
            s.ValueChanged += (_, e) =>
            {
                if (_loading) return;
                eq.Bands[idx] = e.NewValue;
                eq.Edited = true;
                val.Text = Fmt(e.NewValue);
                foreach (var c in PresetChips.Children.OfType<ToggleButton>()) c.IsChecked = false;
                A.EqChanged();
            };
            s.MouseDoubleClick += (_, _) => s.Value = 0;
            col.Children.Add(val);
            col.Children.Add(lab);
            col.Children.Add(s);
            g.Children.Add(col);
            _bands.Add(s);
            _bandValues.Add(val);
        }
        return g;
    }

    static string Fmt(double db) => (db > 0 ? "+" : "") + db.ToString("0.#", CultureInfo.InvariantCulture);

    UIElement BuildParametric(DeviceEq eq)
    {
        var sp = new StackPanel();
        sp.Children.Add(Ui.Text("Exact filters, the same kind AutoEq and Equalizer APO use. They add on top of the 10-band EQ.", 12, brush: "Muted"));
        var header = Row(new[] { "On", "Type", "Frequency (Hz)", "Gain (dB)", "Q", "" }, true);
        header.Margin = new Thickness(0, 10, 0, 4);
        sp.Children.Add(header);
        foreach (var f in eq.Parametric.ToList())
        {
            var flt = f;
            var on = new CheckBox { IsChecked = f.On, VerticalAlignment = VerticalAlignment.Center };
            on.Click += (_, _) => { flt.On = on.IsChecked == true; A.EqChanged(); };
            var type = new ComboBox { ItemsSource = new[] { "PK", "LS", "HS", "LP", "HP" }, SelectedItem = f.Type is "LSC" ? "LS" : f.Type is "HSC" ? "HS" : f.Type, Width = 80, ToolTip = "PK = peak, LS = low shelf, HS = high shelf, LP = low pass, HP = high pass" };
            type.SelectionChanged += (_, _) => { flt.Type = (string)type.SelectedItem; A.EqChanged(); };
            var fc = Num(f.Fc, 20, 20000, v => flt.Fc = v);
            var gain = Num(f.Gain, -24, 24, v => flt.Gain = v);
            var q = Num(f.Q, 0.1, 20, v => flt.Q = v);
            var del = new Button { Content = "✕", Padding = new Thickness(10, 2, 10, 2), MinHeight = 28, Margin = new Thickness(0) };
            del.Click += (_, _) => { eq.Parametric.Remove(flt); BuildEq(); A.EqChanged(); };
            sp.Children.Add(Row(new UIElement[] { on, type, fc, gain, q, del }));
        }
        var add = Ui.Btn("+ Add filter", () => { eq.Parametric.Add(new EqFilter { Type = "PK", Fc = 1000, Gain = 0, Q = 1 }); BuildEq(); });
        add.HorizontalAlignment = HorizontalAlignment.Left;
        add.Margin = new Thickness(0, 10, 0, 0);
        sp.Children.Add(add);
        return sp;
    }

    TextBox Num(double v, double min, double max, Action<double> set)
    {
        var t = new TextBox { Text = v.ToString("0.##", CultureInfo.InvariantCulture), Width = 100 };
        t.LostFocus += (_, _) => Commit();
        t.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) Commit(); };
        void Commit()
        {
            if (double.TryParse(t.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var x))
            {
                x = Math.Clamp(x, min, max);
                t.Text = x.ToString("0.##", CultureInfo.InvariantCulture);
                set(x);
                A.EqChanged();
            }
        }
        return t;
    }

    static Grid Row(IEnumerable<object> cells, bool header = false)
    {
        var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        foreach (var w in new[] { 50, 100, 130, 130, 130, 60 }) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
        int i = 0;
        foreach (var c in cells)
        {
            var e = c as UIElement ?? Ui.Text((string)c, 12, header, "Muted");
            Grid.SetColumn(e, i++);
            if (e is FrameworkElement fe) { fe.HorizontalAlignment = HorizontalAlignment.Left; fe.VerticalAlignment = VerticalAlignment.Center; }
            g.Children.Add(e);
        }
        return g;
    }

    UIElement BuildHeadphone(DeviceEq eq)
    {
        var sp = new StackPanel();
        var all = HeadphoneLibrary.All;
        sp.Children.Add(Ui.Text($"Pick your headphones or earbuds and Cypress Sound corrects them toward a neutral, studio-like sound, using the AutoEq measurements ({all.Count:N0} models). Your 10-band EQ still works on top.", 13, wrap: 760));
        var current = Ui.Text(eq.Headphone == null ? "None selected" : "Using: " + eq.Headphone, 14, true, eq.Headphone == null ? "Muted" : "Green");
        current.Margin = new Thickness(0, 10, 0, 6);
        sp.Children.Add(current);
        var dp = new DockPanel();
        var clear = Ui.Btn("Turn off", () => { eq.Headphone = null; BuildEq(); A.EqChanged(); });
        clear.IsEnabled = eq.Headphone != null;
        DockPanel.SetDock(clear, Dock.Right);
        dp.Children.Add(clear);
        var box = new TextBox { Margin = new Thickness(0, 0, 8, 8), ToolTip = "Type a brand or model, e.g. Sony WH-1000XM4, AirPods, HD 600" };
        dp.Children.Add(box);
        sp.Children.Add(dp);
        var list = new ListBox { Height = 230, Background = Ui.B("Bg2"), Foreground = Ui.B("Ink"), BorderBrush = Ui.B("Line") };
        void Search()
        {
            list.ItemsSource = (box.Text.Trim().Length == 0 ? all.Take(0) : HeadphoneLibrary.Search(box.Text)).Take(80).ToList();
        }
        box.TextChanged += (_, _) => Search();
        list.MouseDoubleClick += (_, _) => Pick();
        list.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) Pick(); };
        void Pick()
        {
            if (list.SelectedItem is not HeadphoneProfile p) return;
            eq.Headphone = p.ToString();
            eq.Enabled = true;
            BuildEq();
            A.EqChanged();
        }
        sp.Children.Add(list);
        var use = Ui.Btn("Use selected", Pick, primary: true);
        use.HorizontalAlignment = HorizontalAlignment.Left;
        use.Margin = new Thickness(0, 8, 0, 0);
        sp.Children.Add(use);
        if (all.Count == 0) sp.Children.Add(Ui.Text("The headphone list is missing from this copy of the app. Reinstall Cypress Sound to get it.", 12, brush: "Warn"));
        Dispatcher.BeginInvoke(() => box.Focus(), DispatcherPriority.Input);
        return sp;
    }

    void BuildCards(DeviceEq eq)
    {
        // Preamp + clip guard
        var preLabel = Ui.Text("", 22, true);
        var clipNote = Ui.Text("", 11, brush: "Muted", wrap: 260);
        void PreText()
        {
            var hp = eq.Headphone == null ? null : HeadphoneLibrary.Find(eq.Headphone);
            var boost = Math.Max(0, Response.MaxBoost(eq, hp?.Filters));
            preLabel.Text = Fmt(eq.Preamp) + " dB";
            clipNote.Text = eq.ClipGuard && boost > 0.05 ? $"Clip guard is lowering the level {boost:0.#} dB so boosts never distort." : "No distortion risk with this EQ.";
        }
        PreText();
        var preSlider = Ui.Slider(-20, 10, eq.Preamp, 0.5, v => { if (_loading) return; eq.Preamp = v; PreText(); A.EqChanged(); });
        var clip = Ui.Switch("Clip guard", eq.ClipGuard, on => { eq.ClipGuard = on; PreText(); A.EqChanged(); });
        _preText = PreText;
        Cards.Children.Add(Ui.Card("VOLUME (PREAMP)", preLabel, preSlider, clip, clipNote));

        if (!IsMic)
        {
            // Night mode
            var strength = Ui.Slider(0, 1, eq.NightStrength, 0.05, v => { if (_loading) return; eq.NightStrength = v; A.EqChanged(); });
            strength.IsEnabled = eq.NightMode;
            var night = Ui.Switch("Night mode", eq.NightMode, on => { eq.NightMode = on; strength.IsEnabled = on; A.EqChanged(); });
            Cards.Children.Add(Ui.Card("NIGHT MODE", night, Ui.Text("Clearer dialogue, softer explosions and bass, so you can keep the volume low.", 11, brush: "Muted", wrap: 260), strength));

            // Bass boost
            var bassLabel = Ui.Text(eq.BassBoost <= 0 ? "Off" : $"+{eq.BassBoost:0.#} dB", 22, true);
            var bass = Ui.Slider(0, 12, eq.BassBoost, 0.5, v => { if (_loading) return; eq.BassBoost = v; bassLabel.Text = v <= 0 ? "Off" : $"+{v:0.#} dB"; A.EqChanged(); });
            Cards.Children.Add(Ui.Card("BASS BOOST", bassLabel, bass, Ui.Text("Deep, clean low end below 80 Hz.", 11, brush: "Muted")));

            // Mono + balance
            var balLabel = Ui.Text(BalText(eq.Balance), 13);
            var bal = Ui.Slider(-1, 1, eq.Balance, 0.05, v => { if (_loading) return; eq.Balance = v; balLabel.Text = BalText(v); A.EqChanged(); });
            bal.MouseDoubleClick += (_, _) => bal.Value = 0;
            var mono = Ui.Switch("Mono", eq.Mono, on => { eq.Mono = on; A.EqChanged(); });
            Cards.Children.Add(Ui.Card("LEFT / RIGHT", mono, balLabel, bal));
        }

        // Level meter + device volume
        _meterBar = new Border { Height = 10, CornerRadius = new CornerRadius(5), Background = Ui.B("Green"), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        _meterText = Ui.Text("", 11, brush: "Muted");
        var track = new Border { Height = 10, CornerRadius = new CornerRadius(5), Background = Ui.B("Line"), Margin = new Thickness(0, 10, 0, 6), Child = _meterBar };
        var id = _selected!;
        var vol = Ui.Slider(0, 100, Math.Round(A.Devices.DeviceVolume(id) * 100), 1, v => A.Devices.SetDeviceVolume(id, (float)(v / 100)));
        Cards.Children.Add(Ui.Card(IsMic ? "MIC LEVEL" : "OUTPUT LEVEL", track, _meterText, Ui.Text("Device volume", 11, brush: "Muted"), vol));

        if (!IsMic)
        {
            var boostLabel = Ui.Text(eq.Boost ? $"+{eq.BoostDb:0.#} dB" : "Off", 22, true);
            var boostSlider = Ui.Slider(0, 12, eq.BoostDb, 0.5, v =>
            {
                if (_loading) return;
                eq.BoostDb = v;
                if (v > 0 && !eq.Boost) { eq.Boost = true; _boostSwitch!.IsChecked = true; }
                boostLabel.Text = eq.Boost ? $"+{v:0.#} dB" : "Off";
                A.EqChanged();
            });
            _boostSwitch = Ui.Switch("Boost", eq.Boost, on => { eq.Boost = on; boostLabel.Text = on ? $"+{eq.BoostDb:0.#} dB" : "Off"; A.EqChanged(); });
            Cards.Children.Add(Ui.Card("VOLUME BOOST", boostLabel, boostSlider, _boostSwitch,
                Ui.Text("Louder than Windows' 100%. Up to +12 dB.", 11, brush: "Muted", wrap: 260),
                Ui.Text("Very loud parts can crackle at high boost. +3 to +6 dB is the sweet spot.", 11, brush: "Warn", wrap: 260)));
        }
    }

    CheckBox? _boostSwitch;

    // ---------- sound bar ----------

    void BuildSoundBar(DeviceEq eq)
    {
        SoundBarHost.Children.Clear();
        if (IsMic || Device is not { } d) return;
        var open = Settings.Current.SoundBarOpen || eq.SoundBar;
        var header = new Button { Style = Ui.S("DeviceRow"), Background = Ui.B("Panel"), BorderBrush = eq.SoundBar ? Ui.B("Green") : Ui.B("Line"), Margin = new Thickness(0, 0, 0, 12) };
        var hdr = new DockPanel();
        var arrow = Ui.Text(Settings.Current.SoundBarOpen ? "▴" : "▾", 16, brush: "Muted");
        DockPanel.SetDock(arrow, Dock.Right);
        hdr.Children.Add(arrow);
        hdr.Children.Add(Ui.Text("🔈  Sound bar setup" + (eq.SoundBar ? "  ·  on" : ""), 15, true, eq.SoundBar ? "Green" : null));
        header.Content = hdr;
        header.Click += (_, _) => { Settings.Current.SoundBarOpen = !Settings.Current.SoundBarOpen; A.EqChanged(); BuildSoundBar(eq); };
        SoundBarHost.Children.Add(header);
        if (!Settings.Current.SoundBarOpen) return;

        var multi = d.Channels >= 6;
        var body = new StackPanel();
        body.Children.Add(Ui.Switch($"{A.NameOf(d)} plays through a sound bar", eq.SoundBar, on => { eq.SoundBar = on; A.EqChanged(); BuildSoundBar(eq); DrawCurve(); }));
        body.Children.Add(Ui.Text(multi
            ? $"Windows sends {d.Channels} channels here, so each speaker gets its own level."
            : "Windows sends 2 channels here (normal for a sound bar on HDMI ARC or the optical cable). The sound bar builds its center, sub and rear sound from them; these sliders steer how much goes to each.",
            12, brush: "Muted", wrap: 820));

        var grid = new UniformGrid { Columns = 3, Margin = new Thickness(0, 10, 0, 0), IsEnabled = eq.SoundBar };
        UIElement Level(string title, string note, double min, double max, double value, Func<double, string> fmt, Action<double> set)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 20, 0) };
            var top = new DockPanel();
            var val = Ui.Text(fmt(value), 14, true);
            DockPanel.SetDock(val, Dock.Right);
            top.Children.Add(val);
            top.Children.Add(Ui.Text(title));
            sp.Children.Add(top);
            var s = Ui.Slider(min, max, value, 0.5, v => { if (_loading) return; set(v); val.Text = fmt(v); A.EqChanged(); });
            s.MouseDoubleClick += (_, _) => s.Value = 0;
            sp.Children.Add(s);
            sp.Children.Add(Ui.Text(note, 11, brush: "Muted", wrap: 260));
            return sp;
        }
        static string Db(double v) => Math.Abs(v) < 0.05 ? "0 dB" : (v > 0 ? "+" : "") + v.ToString("0.#", CultureInfo.InvariantCulture) + " dB";
        grid.Children.Add(Level("Subwoofer", multi ? "Level of the sub channel" : "Deep bass below 90 Hz, which is what your sub plays", -6, 10, eq.SbSub, Db, v => eq.SbSub = v));
        if (multi)
            grid.Children.Add(Level("Rear speakers", "Level of the rear channels", -10, 10, eq.SbRear, Db, v => eq.SbRear = v));
        else
            grid.Children.Add(Level("Rear speakers (surround)", "Wider stereo gives the bar's Surround mode more to send to the rears", 0, 10, eq.SbSurround * 10, v => v < 0.05 ? "Off" : $"{v:0}/10", v => eq.SbSurround = v / 10));
        grid.Children.Add(Level("Center (voices)", "Clearer dialogue in movies and TV", -6, 8, eq.SbVoice, Db, v => eq.SbVoice = v));
        body.Children.Add(grid);

        if (multi)
        {
            var fill = Ui.Switch("Fill rear speakers and sub from stereo (YouTube, music, older games)", eq.SbFill, on => { eq.SbFill = on; A.EqChanged(); });
            fill.IsEnabled = eq.SoundBar;
            body.Children.Add(fill);
        }

        var testRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var testNow = Ui.Text("", 13, true, "Green");
        testNow.VerticalAlignment = VerticalAlignment.Center;
        var test = Ui.Btn("▶  Test speakers", () => { });
        var id = d.Id;
        test.Click += async (_, _) =>
        {
            test.IsEnabled = false;
            try { await A.Devices.TestSpeakers(id, s => testNow.Text = s.Length > 0 ? "Playing: " + s : ""); }
            catch (Exception ex) { testNow.Text = "Couldn't play the test: " + ex.Message; }
            test.IsEnabled = true;
        };
        testRow.Children.Add(test);
        testRow.Children.Add(testNow);
        body.Children.Add(testRow);

        if (!multi)
            body.Children.Add(Ui.Text("For the rears and sub to play PC sound, set the sound bar to Surround mode with its remote (Sound Mode → Surround). " +
                "The exact sub and rear levels can also be set on the remote or in the SmartThings app; these sliders add to that.", 12, brush: "Warn", wrap: 820));

        SoundBarHost.Children.Add(new Border { Background = Ui.B("Panel"), CornerRadius = new CornerRadius(14), Padding = new Thickness(16, 12, 16, 14), Margin = new Thickness(0, -6, 0, 12), Child = body });
    }

    void SavePanel(string path)
    {
        try
        {
            RightPanel.UpdateLayout();
            double w = RightPanel.ActualWidth, h = RightPanel.ActualHeight;
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Ui.B("Bg"), null, new Rect(0, 0, w, h));
                dc.DrawRectangle(new VisualBrush(RightPanel), null, new Rect(0, 0, w, h));
            }
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)w, (int)h, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
            using var f = System.IO.File.Create(path);
            enc.Save(f);
        }
        catch (Exception e) { App.Log("shot: " + e); }
    }

    Action? _preText;

    static string BalText(double b) => Math.Abs(b) < 0.03 ? "Balance: centered" : b < 0 ? $"Balance: {(-b):P0} left" : $"Balance: {b:P0} right";

    void UpdateMeter()
    {
        if (_meterBar == null || _selected == null || _meterBar.Parent is not Border track) return;
        var p = A.Devices.Peak(_selected);
        var w = track.ActualWidth * Math.Clamp(p, 0, 1);
        _meterBar.Width = Math.Max(0, w * 0.6 + _meterBar.Width * 0.4);
        _meterBar.Background = Ui.B(p > 0.98 ? "Red" : p > 0.85 ? "Warn" : "Green");
        _meterText!.Text = p <= 0.0001 ? "Silent" : $"{20 * Math.Log10(p):0} dB";
    }

    void DrawCurve()
    {
        _preText?.Invoke();
        Curve.Children.Clear();
        // draw what's actually playing (an auto profile's tuning while it runs)
        var eq = Device is { } dev && dev.Flow == DataFlow.Render ? A.EffectiveEq(dev) : Eq;
        double w = Curve.ActualWidth, h = Curve.ActualHeight;
        if (eq == null || w < 10) return;
        double X(double f) => w * Math.Log(f / 20) / Math.Log(1000);
        double Y(double db) => h / 2 - db / 15 * (h / 2);
        foreach (var f in new double[] { 50, 100, 200, 500, 1000, 2000, 5000, 10000 })
            Curve.Children.Add(new Line { X1 = X(f), X2 = X(f), Y1 = 0, Y2 = h, Stroke = Ui.B("Line"), StrokeThickness = 1 });
        foreach (var db in new double[] { -12, -6, 0, 6, 12 })
            Curve.Children.Add(new Line { X1 = 0, X2 = w, Y1 = Y(db), Y2 = Y(db), Stroke = Ui.B("Line"), StrokeThickness = db == 0 ? 1.5 : 0.7 });
        var hp = eq.Headphone == null || IsMic ? null : HeadphoneLibrary.Find(eq.Headphone);
        var line = new Polyline { Stroke = Ui.B(eq.Enabled ? "Green" : "Muted"), StrokeThickness = 2.5 };
        for (int i = 0; i <= 120; i++)
        {
            var f = 20 * Math.Pow(1000, i / 120.0);
            var db = eq.Enabled ? Response.TotalDb(eq, hp?.Filters, f) : 0;
            line.Points.Add(new Point(X(f), Math.Clamp(Y(db), 1, h - 1)));
        }
        Curve.Children.Add(line);
        var cap = Ui.Text(eq.Enabled ? "What you'll hear (before volume)" : "EQ is off: sound plays unchanged", 11, brush: "Muted");
        Canvas.SetLeft(cap, 4);
        Canvas.SetTop(cap, 2);
        Curve.Children.Add(cap);
    }
}
