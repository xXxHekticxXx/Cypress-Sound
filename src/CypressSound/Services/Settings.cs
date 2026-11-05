// Everything the app remembers, saved as JSON in %APPDATA%\Cypress Works\Cypress Sound\settings.json.
using System.Text.Json;
using CypressSound.Eq;

namespace CypressSound.Services;

public sealed class AutoProfile
{
    public string Name { get; set; } = "New profile";
    public bool Enabled { get; set; } = true;
    /// <summary>Program names that switch this profile on, e.g. "D2R" or "D2R.exe".</summary>
    public List<string> Programs { get; set; } = new();
    /// <summary>Output to switch to while the program runs (null = leave as is).</summary>
    public string? OutputId { get; set; }
    /// <summary>Preset to load on that output while the program runs (null = leave as is).</summary>
    public string? Preset { get; set; }
    public bool NightMode { get; set; }
}

public sealed class AppSettings
{
    public Dictionary<string, DeviceEq> Eq { get; set; } = new();          // by device id
    public Dictionary<string, int> Hotkey { get; set; } = new();           // device id -> Ctrl+Alt+N
    public Dictionary<string, string> Nickname { get; set; } = new();      // device id -> your name for it
    public List<string> Hidden { get; set; } = new();                      // device ids not shown
    public List<Preset> UserPresets { get; set; } = new();
    public List<AutoProfile> Profiles { get; set; } = new();
    public bool StartWithWindows { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public bool CheckUpdates { get; set; } = true;
    public bool GlobalHotkeys { get; set; } = true;
    public string Mode { get; set; } = "graphic"; // graphic | parametric | headphone
    public string? LastSelected { get; set; }
    public bool SoundBarOpen { get; set; }
}

public static class Settings
{
    public static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cypress Works", "Cypress Sound");
    static string FilePath => Path.Combine(Folder, "settings.json");
    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public static AppSettings Current { get; private set; } = Load();

    static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath)) return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Opts) ?? Defaults();
        }
        catch
        {
            try { File.Copy(FilePath, FilePath + ".broken", true); } catch { }
        }
        return Defaults();
    }

    static AppSettings Defaults()
    {
        var s = new AppSettings();
        s.Profiles.Add(new AutoProfile { Name = "Diablo II", Programs = { "D2R", "Diablo II" }, Preset = "Footsteps (gaming)" });
        s.Profiles.Add(new AutoProfile { Name = "Movies", Programs = { "vlc", "Plex", "Netflix" }, Preset = "Movie night", NightMode = true, Enabled = false });
        return s;
    }

    public static void Save()
    {
        Directory.CreateDirectory(Folder);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(Current, Opts));
        File.Move(tmp, FilePath, true);
    }

    public static DeviceEq EqFor(string deviceId)
    {
        if (!Current.Eq.TryGetValue(deviceId, out var eq))
        {
            eq = new DeviceEq();
            Current.Eq[deviceId] = eq;
        }
        return eq;
    }

    public static IEnumerable<Preset> AllPresets(bool mic) =>
        Presets.Builtin.Where(p => p.ForMic == mic || p.Name == "Flat").Concat(Current.UserPresets.Where(p => p.ForMic == mic));
}
