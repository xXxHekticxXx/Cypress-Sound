// Global hotkeys, start with Windows, watching for programs (auto profiles) and update checks.
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Interop;
using Microsoft.Win32;

namespace CypressSound.Services;

/// <summary>Ctrl+Alt+1..9 (outputs), Ctrl+Alt+0 (next output), Ctrl+Alt+E (EQ on/off). Work in any app or game.</summary>
public sealed class Hotkeys : IDisposable
{
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_NOREPEAT = 0x4000;
    const int WM_HOTKEY = 0x0312;

    readonly HwndSource _src;
    readonly List<int> _ids = new();
    public event Action<int>? Output;   // 1..9
    public event Action? NextOutput;
    public event Action? ToggleEq;

    public Hotkeys()
    {
        _src = new HwndSource(new HwndSourceParameters("CypressSoundHotkeys") { Width = 0, Height = 0, WindowStyle = 0 });
        _src.AddHook(Hook);
    }

    public void Enable(bool on)
    {
        foreach (var id in _ids) UnregisterHotKey(_src.Handle, id);
        _ids.Clear();
        if (!on) return;
        for (int n = 0; n <= 9; n++) Reg(100 + n, (uint)(0x30 + n));
        Reg(200, 0x45); // E
    }

    void Reg(int id, uint vk)
    {
        if (RegisterHotKey(_src.Handle, id, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, vk)) _ids.Add(id);
    }

    IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_HOTKEY) return IntPtr.Zero;
        var id = wParam.ToInt32();
        if (id == 100) NextOutput?.Invoke();
        else if (id is > 100 and <= 109) Output?.Invoke(id - 100);
        else if (id == 200) ToggleEq?.Invoke();
        handled = true;
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Enable(false);
        _src.Dispose();
    }
}

public static class AutoStart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static void Apply(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (k == null) return;
            if (on) k.SetValue("Cypress Sound", $"\"{Environment.ProcessPath}\" --tray");
            else k.DeleteValue("Cypress Sound", false);
        }
        catch { }
    }
}

/// <summary>Checks every few seconds which programs are running and turns auto profiles on and off.</summary>
public sealed class ProfileWatcher : IDisposable
{
    readonly System.Threading.Timer _timer;
    string? _active;
    public event Action<AutoProfile?>? Changed; // a profile started, or null when it ended

    public ProfileWatcher() => _timer = new System.Threading.Timer(_ => Tick(), null, 2000, 3000);

    void Tick()
    {
        try
        {
            var running = new HashSet<string>(Process.GetProcesses().Select(p => { try { return p.ProcessName; } catch { return ""; } }), StringComparer.OrdinalIgnoreCase);
            var match = Settings.Current.Profiles.FirstOrDefault(p => p.Enabled && p.Programs.Any(n => running.Contains(Path.GetFileNameWithoutExtension(n.Trim()))));
            var name = match?.Name;
            if (name != _active)
            {
                _active = name;
                Changed?.Invoke(match);
            }
        }
        catch { }
    }

    public void Dispose() => _timer.Dispose();
}

public sealed record UpdateInfo(Version Version, string Url, string Notes);

/// <summary>New versions are published as GitHub releases of xXxHekticxXx/Cypress-Sound.</summary>
public static class Updater
{
    public const string Repo = "xXxHekticxXx/Cypress-Sound";
    public static Version Current => typeof(Updater).Assembly.GetName().Version ?? new Version(1, 0);

    public static async Task<UpdateInfo?> CheckAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("CypressSound-Updater");
        var json = await http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest");
        using var doc = JsonDocument.Parse(json);
        var tag = doc.RootElement.GetProperty("tag_name").GetString()?.TrimStart('v') ?? "0";
        if (!Version.TryParse(tag, out var v) || v <= Current) return null;
        foreach (var a in doc.RootElement.GetProperty("assets").EnumerateArray())
        {
            var name = a.GetProperty("name").GetString() ?? "";
            if (name.EndsWith("Setup.exe", StringComparison.OrdinalIgnoreCase))
                return new UpdateInfo(v, a.GetProperty("browser_download_url").GetString()!, doc.RootElement.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "");
        }
        return null;
    }

    /// <summary>Downloads the new installer and runs it; it closes this app, updates it in place and starts it again.</summary>
    public static async Task InstallAsync(UpdateInfo u, IProgress<double>? progress = null)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("CypressSound-Updater");
        var path = Path.Combine(Path.GetTempPath(), $"CypressSound-{u.Version}-Setup.exe");
        using (var resp = await http.GetAsync(u.Url, HttpCompletionOption.ResponseHeadersRead))
        {
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? 0;
            await using var src = await resp.Content.ReadAsStreamAsync();
            await using var dst = File.Create(path);
            var buf = new byte[81920];
            long done = 0;
            int n;
            while ((n = await src.ReadAsync(buf)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n));
                done += n;
                if (total > 0) progress?.Report((double)done / total);
            }
        }
        Process.Start(new ProcessStartInfo(path, "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS") { UseShellExecute = true });
    }
}
