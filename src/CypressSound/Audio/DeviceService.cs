// Lists output and input devices, keeps the list current when devices are plugged in or the default changes,
// and lists the apps that are playing sound.
using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace CypressSound.Audio;

public sealed record AudioDevice(string Id, string Name, string Adapter, DataFlow Flow, string Icon)
{
    /// <summary>Channels Windows mixes for this device (2 = stereo, 6 = 5.1, 8 = 7.1).</summary>
    public int Channels { get; init; } = 2;

    /// <summary>The endpoint GUID, which is how Equalizer APO's "Device:" lines and its registry keys name a device.</summary>
    public string Guid => Id.Contains("}.{") ? Id[(Id.IndexOf("}.{", StringComparison.Ordinal) + 2)..] : Id;
}

public sealed class AppSession
{
    public uint ProcessId { get; init; }
    public string Name { get; init; } = "";
    public string? ExePath { get; init; }
    public float Volume { get; set; }
    public bool Muted { get; set; }
    public string? DeviceId { get; set; } // null = follows the Windows default
}

public sealed class DeviceService : IMMNotificationClient, IDisposable
{
    readonly MMDeviceEnumerator _en = new();
    public event Action? Changed;

    public DeviceService() => _en.RegisterEndpointNotificationCallback(this);

    public IReadOnlyList<AudioDevice> Devices(DataFlow flow)
    {
        var list = new List<AudioDevice>();
        foreach (var d in _en.EnumerateAudioEndPoints(flow == DataFlow.Render ? NAudio.CoreAudioApi.DataFlow.Render : NAudio.CoreAudioApi.DataFlow.Capture, DeviceState.Active))
        {
            try
            {
                var name = d.FriendlyName;
                string adapter = "";
                try { adapter = d.DeviceFriendlyName; } catch { }
                int ch = 2;
                try { ch = d.AudioClient.MixFormat.Channels; } catch { }
                list.Add(new AudioDevice(d.ID, ShortName(name, adapter), adapter, flow, IconFor(name, flow)) { Channels = ch });
            }
            finally { d.Dispose(); }
        }
        return list;
    }

    /// <summary>"Speakers (Realtek(R) Audio)" → "Speakers"; "LG TV SSCR2 (NVIDIA High Definition Audio)" → "LG TV SSCR2".</summary>
    static string ShortName(string friendly, string adapter)
    {
        var s = friendly;
        if (adapter.Length > 0 && s.EndsWith($"({adapter})", StringComparison.Ordinal)) s = s[..^(adapter.Length + 2)].Trim();
        return s.Length > 0 ? s : friendly;
    }

    static string IconFor(string name, DataFlow flow)
    {
        var n = name.ToLowerInvariant();
        if (flow == DataFlow.Capture) return "🎙";
        if (n.Contains("headphone") || n.Contains("headset") || n.Contains("buds") || n.Contains("airpods") || n.Contains("hands-free")) return "🎧";
        if (n.Contains("tv") || n.Contains("hdmi") || n.Contains("display") || n.Contains("monitor") || n.Contains("nvidia")) return "📺";
        return "🔊";
    }

    public string? DefaultId(DataFlow flow)
    {
        try
        {
            using var d = _en.GetDefaultAudioEndpoint(flow == DataFlow.Render ? NAudio.CoreAudioApi.DataFlow.Render : NAudio.CoreAudioApi.DataFlow.Capture, NAudio.CoreAudioApi.Role.Multimedia);
            return d.ID;
        }
        catch { return null; }
    }

    /// <summary>Peak level 0..1 of what a device is playing right now (for the level meter).</summary>
    public float Peak(string deviceId)
    {
        try
        {
            using var d = _en.GetDevice(deviceId);
            return d.AudioMeterInformation.MasterPeakValue;
        }
        catch { return 0; }
    }

    public float DeviceVolume(string deviceId)
    {
        try { using var d = _en.GetDevice(deviceId); return d.AudioEndpointVolume.MasterVolumeLevelScalar; } catch { return 0; }
    }

    public void SetDeviceVolume(string deviceId, float v)
    {
        try { using var d = _en.GetDevice(deviceId); d.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(v, 0, 1); } catch { }
    }

    /// <summary>Apps with an audio session on any output, one row per app.</summary>
    public IReadOnlyList<AppSession> Apps()
    {
        var byPid = new Dictionary<uint, AppSession>();
        foreach (var d in _en.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.Render, DeviceState.Active))
        {
            try
            {
                var sessions = d.AudioSessionManager.Sessions;
                for (int i = 0; i < sessions.Count; i++)
                {
                    using var s = sessions[i];
                    if (s.IsSystemSoundsSession) continue;
                    var pid = s.GetProcessID;
                    if (pid == 0 || byPid.ContainsKey(pid)) continue;
                    if (s.State == AudioSessionState.AudioSessionStateExpired) continue;
                    string name = s.DisplayName;
                    string? path = null;
                    try
                    {
                        using var p = Process.GetProcessById((int)pid);
                        if (string.IsNullOrWhiteSpace(name) || name.StartsWith('@')) name = p.MainWindowTitle.Length is > 0 and < 40 ? p.MainWindowTitle : p.ProcessName;
                        try { path = p.MainModule?.FileName; } catch { }
                        if (path != null)
                        {
                            var desc = FileVersionInfo.GetVersionInfo(path).FileDescription;
                            if (!string.IsNullOrWhiteSpace(desc) && (string.IsNullOrWhiteSpace(s.DisplayName) || s.DisplayName.StartsWith('@'))) name = desc!;
                        }
                    }
                    catch { continue; } // the app already closed
                    byPid[pid] = new AppSession
                    {
                        ProcessId = pid, Name = name, ExePath = path,
                        Volume = s.SimpleAudioVolume.Volume, Muted = s.SimpleAudioVolume.Mute,
                        DeviceId = WindowsAudio.GetAppDevice(pid),
                    };
                }
            }
            catch { }
            finally { d.Dispose(); }
        }
        return byPid.Values.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Plays a short tone on the left, the right, the middle (center speaker) and a deep one for the sub.</summary>
    public async Task TestSpeakers(string deviceId, Action<string> step)
    {
        var tones = new (string Label, double Hz, float Pan)[]
        {
            ("Left", 440, -1f), ("Right", 440, 1f), ("Center (voices)", 660, 0f), ("Subwoofer", 50, 0f),
        };
        foreach (var (label, hz, pan) in tones)
        {
            step(label);
            await Task.Run(() =>
            {
                using var dev = _en.GetDevice(deviceId);
                using var output = new NAudio.Wave.WasapiOut(dev, AudioClientShareMode.Shared, true, 100);
                var gen = new NAudio.Wave.SampleProviders.SignalGenerator(48000, 1)
                {
                    Frequency = hz, Gain = hz < 100 ? 0.5 : 0.18, Type = NAudio.Wave.SampleProviders.SignalGeneratorType.Sin,
                };
                var tone = new NAudio.Wave.SampleProviders.PanningSampleProvider(
                    new NAudio.Wave.SampleProviders.OffsetSampleProvider(gen) { TakeSamples = (int)(48000 * 1.4) }) { Pan = pan };
                output.Init(new NAudio.Wave.SampleProviders.SampleToWaveProvider(tone));
                output.Play();
                while (output.PlaybackState == NAudio.Wave.PlaybackState.Playing) Thread.Sleep(50);
            });
            await Task.Delay(300);
        }
        step("");
    }

    public void SetAppVolume(uint pid, float volume)
    {
        foreach (var d in _en.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.Render, DeviceState.Active))
        {
            try
            {
                var sessions = d.AudioSessionManager.Sessions;
                for (int i = 0; i < sessions.Count; i++)
                {
                    using var s = sessions[i];
                    if (s.GetProcessID == pid) s.SimpleAudioVolume.Volume = Math.Clamp(volume, 0, 1);
                }
            }
            catch { }
            finally { d.Dispose(); }
        }
    }

    // ---- device change notifications (called on a Windows audio thread) ----
    public void OnDeviceStateChanged(string deviceId, DeviceState newState) => Changed?.Invoke();
    public void OnDeviceAdded(string pwstrDeviceId) => Changed?.Invoke();
    public void OnDeviceRemoved(string deviceId) => Changed?.Invoke();
    public void OnDefaultDeviceChanged(NAudio.CoreAudioApi.DataFlow flow, NAudio.CoreAudioApi.Role role, string defaultDeviceId)
    {
        if (role == NAudio.CoreAudioApi.Role.Multimedia) Changed?.Invoke();
    }
    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

    public void Dispose()
    {
        try { _en.UnregisterEndpointNotificationCallback(this); } catch { }
        _en.Dispose();
    }
}
