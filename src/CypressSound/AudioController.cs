// The app's brain: the device lists, switching outputs, writing the EQ for Equalizer APO, and auto profiles.
// Windows and the UI both call in here; the UI listens to Changed.
using System.Windows;
using System.Windows.Threading;
using CypressSound.Audio;
using CypressSound.Eq;
using CypressSound.Services;

namespace CypressSound;

public sealed class AudioController : IDisposable
{
    public DeviceService Devices { get; } = new();
    public IReadOnlyList<AudioDevice> Outputs { get; private set; } = Array.Empty<AudioDevice>();
    public IReadOnlyList<AudioDevice> Inputs { get; private set; } = Array.Empty<AudioDevice>();
    public string? DefaultOutput { get; private set; }
    public string? DefaultInput { get; private set; }
    public AutoProfile? ActiveProfile { get; private set; }
    public string? LastError { get; private set; }
    public event Action? Changed;
    public event Action<string>? Notice; // short on-screen message (e.g. after a hotkey switch)

    readonly Dispatcher _ui;
    readonly DispatcherTimer _writeTimer;
    readonly ProfileWatcher _profiles = new();
    string? _outputBeforeProfile;

    public AudioController()
    {
        _ui = Application.Current.Dispatcher;
        _writeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _writeTimer.Tick += (_, _) => { _writeTimer.Stop(); WriteNow(); };
        Devices.Changed += () => _ui.BeginInvoke(Refresh);
        _profiles.Changed += p => _ui.BeginInvoke(() => OnProfile(p));
        Refresh();
    }

    /// <summary>--demo: made-up devices, for screenshots on machines without sound hardware.</summary>
    public static readonly bool Demo = Environment.GetCommandLineArgs().Contains("--demo");
    string? _demoOut, _demoIn;
    static readonly AudioDevice[] DemoOutputs =
    {
        new("{0.0.0.00000000}.{d0000000-0000-0000-0000-000000000001}", "Headphones", "Realtek(R) Audio", DataFlow.Render, "🎧"),
        new("{0.0.0.00000000}.{d0000000-0000-0000-0000-000000000002}", "LG TV SSCR2", "NVIDIA High Definition Audio", DataFlow.Render, "📺"),
        new("{0.0.0.00000000}.{d0000000-0000-0000-0000-000000000003}", "Speakers", "Realtek(R) Audio", DataFlow.Render, "🔊"),
    };
    static readonly AudioDevice[] DemoInputs =
    {
        new("{0.0.1.00000000}.{d0000000-0000-0000-0000-000000000004}", "Microphone", "Blue Yeti", DataFlow.Capture, "🎙"),
    };

    public void Refresh()
    {
        var hidden = Settings.Current.Hidden;
        if (Demo)
        {
            Outputs = DemoOutputs;
            Inputs = DemoInputs;
            DefaultOutput = _demoOut ??= DemoOutputs[0].Id;
            DefaultInput = _demoIn ??= DemoInputs[0].Id;
        }
        else
        {
            Outputs = Devices.Devices(DataFlow.Render).Where(d => !hidden.Contains(d.Id)).ToList();
            Inputs = Devices.Devices(DataFlow.Capture).Where(d => !hidden.Contains(d.Id)).ToList();
            DefaultOutput = Devices.DefaultId(DataFlow.Render);
            DefaultInput = Devices.DefaultId(DataFlow.Capture);
        }
        foreach (var d in Outputs.Concat(Inputs)) Settings.EqFor(d.Id).Channels = d.Channels;
        // hotkeys: give new outputs the next free number
        foreach (var d in Outputs)
        {
            if (Settings.Current.Hotkey.ContainsKey(d.Id)) continue;
            var used = Settings.Current.Hotkey.Values.ToHashSet();
            var n = Enumerable.Range(1, 9).FirstOrDefault(i => !used.Contains(i));
            if (n > 0) Settings.Current.Hotkey[d.Id] = n;
        }
        ScheduleWrite();
        Changed?.Invoke();
    }

    public string NameOf(AudioDevice d) => Settings.Current.Nickname.TryGetValue(d.Id, out var n) && !string.IsNullOrWhiteSpace(n) ? n : d.Name;

    public void SwitchTo(string deviceId, bool announce = false)
    {
        try
        {
            if (Demo)
            {
                if (DemoInputs.Any(i => i.Id == deviceId)) _demoIn = deviceId; else _demoOut = deviceId;
            }
            else WindowsAudio.SetDefault(deviceId);
            var d = Outputs.Concat(Inputs).FirstOrDefault(x => x.Id == deviceId);
            if (announce && d != null) Notice?.Invoke($"{d.Icon}  {NameOf(d)}");
            LastError = null;
        }
        catch (Exception e)
        {
            LastError = "Windows didn't switch the device: " + e.Message;
        }
        Refresh();
    }

    public void SwitchToHotkey(int n)
    {
        var id = Settings.Current.Hotkey.FirstOrDefault(kv => kv.Value == n && Outputs.Any(o => o.Id == kv.Key)).Key;
        if (id != null) SwitchTo(id, announce: true);
    }

    public void NextOutput()
    {
        if (Outputs.Count == 0) return;
        var i = Outputs.ToList().FindIndex(o => o.Id == DefaultOutput);
        SwitchTo(Outputs[(i + 1) % Outputs.Count].Id, announce: true);
    }

    public void ToggleEq()
    {
        if (DefaultOutput == null) return;
        var eq = Settings.EqFor(DefaultOutput);
        eq.Enabled = !eq.Enabled;
        Notice?.Invoke(eq.Enabled ? "EQ on" : "EQ off");
        EqChanged();
    }

    /// <summary>Call after any EQ edit: saves settings and rewrites the Equalizer APO file a moment later.</summary>
    public void EqChanged()
    {
        ScheduleWrite();
        Changed?.Invoke();
    }

    void ScheduleWrite()
    {
        _writeTimer.Stop();
        _writeTimer.Start();
    }

    /// <summary>The EQ actually used for a device right now (an auto profile can override the preset).</summary>
    public DeviceEq EffectiveEq(AudioDevice d)
    {
        var eq = Settings.EqFor(d.Id);
        var p = ActiveProfile;
        if (p == null || d.Flow != DataFlow.Render) return eq;
        var target = p.OutputId ?? DefaultOutput;
        if (target != d.Id) return eq;
        var e = eq.Clone();
        var preset = p.Preset == null ? null : Settings.AllPresets(false).FirstOrDefault(x => x.Name == p.Preset);
        if (preset != null) { e.Bands = (double[])preset.Bands.Clone(); e.Preset = preset.Name; e.Enabled = true; }
        if (p.NightMode) e.NightMode = true;
        return e;
    }

    void WriteNow()
    {
        try
        {
            Settings.Save();
            if (!ApoConfig.Ready()) return;
            var all = Outputs.Concat(Inputs).Select(d => (d, EffectiveEq(d))).ToList();
            ApoConfig.Write(ApoConfig.Build(all, name => HeadphoneLibrary.Find(name)));
            LastError = null;
        }
        catch (Exception e)
        {
            LastError = "Couldn't save the EQ: " + e.Message;
            Changed?.Invoke();
        }
    }

    void OnProfile(AutoProfile? p)
    {
        if (p != null)
        {
            ActiveProfile = p;
            if (p.OutputId != null && p.OutputId != DefaultOutput && Outputs.Any(o => o.Id == p.OutputId))
            {
                _outputBeforeProfile = DefaultOutput;
                SwitchTo(p.OutputId);
            }
            Notice?.Invoke($"Profile: {p.Name}");
        }
        else
        {
            var was = ActiveProfile;
            ActiveProfile = null;
            if (_outputBeforeProfile != null && Outputs.Any(o => o.Id == _outputBeforeProfile)) SwitchTo(_outputBeforeProfile);
            _outputBeforeProfile = null;
            if (was != null) Notice?.Invoke($"{was.Name} closed: back to your EQ");
        }
        EqChanged();
    }

    public void Dispose()
    {
        _profiles.Dispose();
        WriteNow();
        Devices.Dispose();
    }
}
