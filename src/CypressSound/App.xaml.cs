using System.Diagnostics;
using System.Windows;
using CypressSound.Eq;
using CypressSound.Services;
using CypressSound.Views;
using Forms = System.Windows.Forms;

namespace CypressSound;

public partial class App : Application
{
    static Mutex? _single;
    public static AudioController Audio { get; private set; } = null!;
    Forms.NotifyIcon? _tray;
    public static Hotkeys? Hotkeys { get; private set; }
    MainWindow? _main;
    TrayFlyout? _flyout;
    public static UpdateInfo? PendingUpdate { get; set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Elevated helper mode, started by the installer or by "Finish setup" in the app.
        if (e.Args.Contains("--setup-apo-config"))
        {
            try { ApoConfig.SetUpConfigFolder(); Shutdown(0); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Cypress Sound"); Shutdown(1); }
            return;
        }
        _single = new Mutex(true, @"Local\CypressWorks.CypressSound", out var first);
        if (!first)
        {
            // already running: ask that copy to show its window
            NativeSignal.Post();
            Shutdown();
            return;
        }
        DispatcherUnhandledException += (_, ex) =>
        {
            Log(ex.Exception.ToString());
            ex.Handled = true;
        };

        // --theme NAME: show a theme without saving it (used for the build's screenshots)
        var themeArg = e.Args.SkipWhile(a => a != "--theme").Skip(1).FirstOrDefault();
        Theme.Apply(themeArg ?? Settings.Current.Theme);
        Theme.Changed += () => { _flyout?.Close(); _flyout = null; }; // the tray panel is rebuilt in the new colours
        Audio = new AudioController();
        Hotkeys = new Hotkeys();
        Hotkeys.Output += n => Audio.SwitchToHotkey(n);
        Hotkeys.NextOutput += Audio.NextOutput;
        Hotkeys.ToggleEq += Audio.ToggleEq;
        Hotkeys.Enable(Settings.Current.GlobalHotkeys);
        Audio.Notice += Osd.Show;
        NativeSignal.Listen(() => Dispatcher.BeginInvoke(ShowMain));
        AutoStart.Apply(Settings.Current.StartWithWindows);

        _tray = new Forms.NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "Cypress Sound",
            Visible = true,
        };
        _tray.MouseClick += (_, me) =>
        {
            if (me.Button == Forms.MouseButtons.Left) Dispatcher.BeginInvoke(ToggleFlyout);
        };
        _tray.DoubleClick += (_, _) => Dispatcher.BeginInvoke(ShowMain);
        _tray.ContextMenuStrip = new Forms.ContextMenuStrip();
        _tray.ContextMenuStrip.Opening += (_, _) => BuildTrayMenu();
        BuildTrayMenu();

        if (!e.Args.Contains("--tray")) ShowMain();
        if (Settings.Current.CheckUpdates) _ = CheckForUpdate();
    }

    static System.Drawing.Icon LoadIcon()
    {
        try
        {
            var s = GetResourceStream(new Uri("pack://application:,,,/Assets/CypressSound.ico"))?.Stream;
            if (s != null) return new System.Drawing.Icon(s);
        }
        catch { }
        return System.Drawing.SystemIcons.Application;
    }

    void BuildTrayMenu()
    {
        var m = _tray!.ContextMenuStrip!;
        m.Items.Clear();
        foreach (var d in Audio.Outputs)
        {
            var hk = Settings.Current.Hotkey.TryGetValue(d.Id, out var n) ? $"\tCtrl+Alt+{n}" : "";
            var item = new Forms.ToolStripMenuItem($"{d.Icon}  {Audio.NameOf(d)}{hk}") { Checked = d.Id == Audio.DefaultOutput };
            var id = d.Id;
            item.Click += (_, _) => Audio.SwitchTo(id);
            m.Items.Add(item);
        }
        m.Items.Add(new Forms.ToolStripSeparator());
        var eqOn = Audio.DefaultOutput != null && Settings.EqFor(Audio.DefaultOutput).Enabled;
        var eqItem = new Forms.ToolStripMenuItem("EQ on\tCtrl+Alt+E") { Checked = eqOn };
        eqItem.Click += (_, _) => Audio.ToggleEq();
        m.Items.Add(eqItem);
        m.Items.Add("Open Cypress Sound", null, (_, _) => ShowMain());
        m.Items.Add("Windows sound settings", null, (_, _) => Process.Start(new ProcessStartInfo("ms-settings:sound") { UseShellExecute = true }));
        m.Items.Add(new Forms.ToolStripSeparator());
        m.Items.Add("Quit", null, (_, _) => Quit());
    }

    public void ShowMain()
    {
        _main ??= new MainWindow();
        _main.Show();
        if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
        _main.Activate();
    }

    void ToggleFlyout()
    {
        if (_flyout is { IsVisible: true }) { _flyout.Hide(); return; }
        _flyout ??= new TrayFlyout();
        _flyout.ShowNearTray();
    }

    public static async Task CheckForUpdate()
    {
        try
        {
            PendingUpdate = await Updater.CheckAsync();
            if (PendingUpdate != null) Audio.EqChanged(); // refresh the UI so the banner shows
        }
        catch { /* offline: try again next start */ }
    }

    /// <summary>Close and open again (used after changing the colour theme so every part picks it up).</summary>
    public void Restart()
    {
        try { _single?.ReleaseMutex(); _single?.Dispose(); _single = null; } catch { }
        try { Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true }); } catch { }
        Quit();
    }

    public void Quit()
    {
        Hotkeys?.Dispose();
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        Audio.Dispose();
        Shutdown();
    }

    public static void Log(string text)
    {
        try
        {
            Directory.CreateDirectory(Settings.Folder);
            File.AppendAllText(Path.Combine(Settings.Folder, "log.txt"), $"{DateTime.Now:s} {text}\r\n");
        }
        catch { }
    }
}

/// <summary>Lets a second copy of the app tell the first one to show its window.</summary>
static class NativeSignal
{
    static readonly string Name = @"Local\CypressWorks.CypressSound.Show";
    public static void Post()
    {
        try { using var ev = EventWaitHandle.OpenExisting(Name); ev.Set(); } catch { }
    }
    public static void Listen(Action onShow)
    {
        var ev = new EventWaitHandle(false, EventResetMode.AutoReset, Name);
        new Thread(() => { while (true) { ev.WaitOne(); onShow(); } }) { IsBackground = true }.Start();
    }
}
