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
    /// <summary>Preset used instead when the output in use is headphones (null = same as Preset).</summary>
    public string? HeadphonePreset { get; set; }
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
    public bool CodProfileAdded { get; set; }
    /// <summary>Profile switched on by hand (stays on until switched off, also after a restart).</summary>
    public string? ManualProfile { get; set; }
    /// <summary>Your own EQ for the output a profile is changing right now (put back when it ends).</summary>
    public string? ProfileBackupDevice { get; set; }
    public DeviceEq? ProfileBackup { get; set; }
    public double SidebarWidth { get; set; } = 320;
    public string Theme { get; set; } = "Midnight";
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
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Opts) ?? Defaults();
                // added in 1.0.4 for people who already had settings (once, so deleting it sticks)
                if (!s.CodProfileAdded) { if (!s.Profiles.Any(p => p.Name == "Call of Duty")) s.Profiles.Add(CodProfile()); s.CodProfileAdded = true; }
                // 1.0.6: the Call of Duty profile gets separate MW4 tunings for headphones and for TV/speakers
                foreach (var p in s.Profiles.Where(p => p.Name == "Call of Duty" && p.Preset == "Call of Duty"))
                { p.Preset = "Call of Duty · TV & speakers"; p.HeadphonePreset = "Call of Duty · Headphones"; }
                foreach (var e in s.Eq.Values.Where(e => e.Preset == "Call of Duty")) e.Preset = "Call of Duty · TV & speakers";
                // 1.0.8: the MW4 tunings were renamed to show up as Call of Duty on the EQ preset buttons
                static string? Ren(string? n) => n?.Replace("MW4 · ", "Call of Duty · ");
                foreach (var p in s.Profiles) { p.Preset = Ren(p.Preset); p.HeadphonePreset = Ren(p.HeadphonePreset); }
                foreach (var e in s.Eq.Values) e.Preset = Ren(e.Preset) ?? e.Preset;
                if (s.ProfileBackup != null) s.ProfileBackup.Preset = Ren(s.ProfileBackup.Preset) ?? s.ProfileBackup.Preset;
                return s;
            }
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
        s.Profiles.Add(CodProfile());
        s.CodProfileAdded = true;
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

    /// <summary>Presets shown in the EQ menus (not the ones kept for auto profiles).</summary>
    public static IEnumerable<Preset> AllPresets(bool mic) =>
        Presets.Builtin.Where(p => (p.ForMic == mic || p.Name == "Flat") && !p.ProfileOnly).Concat(Current.UserPresets.Where(p => p.ForMic == mic));

    /// <summary>Presets an auto profile can load: the normal ones plus the game tunings made just for profiles.</summary>
    public static IEnumerable<Preset> ProfilePresets() => AllPresets(false).Concat(Presets.Builtin.Where(p => p.ProfileOnly));

    static AutoProfile CodProfile() => new()
    {
        Name = "Call of Duty",
        // cod = Black Ops 6 / Warzone / CoD HQ; the rest are older stand-alone games
        Programs = { "cod", "cod22-cod", "cod23-cod", "ModernWarfare", "BlackOpsColdWar", "Vanguard", "BlackOps4", "BlackOps3", "iw7_ship", "s2_mp64_ship" },
        Preset = "Call of Duty · TV & speakers", HeadphonePreset = "Call of Duty · Headphones", NightMode = true, Enabled = false,
    };
}
