// Talks to Equalizer APO, the open-source audio engine that applies the EQ to everything Windows plays:
// finds it, checks which devices it is turned on for, and writes the EQ as an Equalizer APO config file.
// Equalizer APO watches its config folder and applies changes within a moment, no restart needed.
using System.Globalization;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;
using CypressSound.Audio;

namespace CypressSound.Eq;

public static class ApoConfig
{
    public const string ApoClsid = "{EACD2258-FCAC-4FF4-B36D-419E924A6D79}";
    public const string OurFile = "cypress-sound.txt";
    const string IncludeLine = "Include: " + OurFile;

    public static string? InstallPath()
    {
        try
        {
            using var k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(@"SOFTWARE\EqualizerAPO");
            var p = k?.GetValue("InstallPath") as string;
            if (!string.IsNullOrEmpty(p) && Directory.Exists(p)) return p;
        }
        catch { }
        var def = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "EqualizerAPO");
        return File.Exists(Path.Combine(def, "EqualizerAPO.dll")) ? def : null;
    }

    public static bool Installed => InstallPath() != null;

    public static string? ConfigDir()
    {
        try
        {
            using var k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(@"SOFTWARE\EqualizerAPO");
            var p = k?.GetValue("ConfigPath") as string;
            if (!string.IsNullOrEmpty(p) && Directory.Exists(p)) return p;
        }
        catch { }
        var ip = InstallPath();
        return ip == null ? null : Path.Combine(ip, "config");
    }

    /// <summary>Is Equalizer APO switched on for this device (ticked in its Device Selector)?</summary>
    public static bool EnabledFor(AudioDevice d)
    {
        try
        {
            var flow = d.Flow == DataFlow.Render ? "Render" : "Capture";
            using var k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                .OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\{flow}\{d.Guid}\FxProperties");
            if (k == null) return false;
            foreach (var name in k.GetValueNames())
            {
                var v = k.GetValue(name);
                if (v is string s && s.Contains(ApoClsid, StringComparison.OrdinalIgnoreCase)) return true;
                if (v is string[] arr && arr.Any(x => x.Contains(ApoClsid, StringComparison.OrdinalIgnoreCase))) return true;
            }
        }
        catch { }
        return false;
    }

    /// <summary>True when config.txt includes our file and we may write to the config folder.</summary>
    public static bool Ready()
    {
        var dir = ConfigDir();
        if (dir == null) return false;
        try
        {
            var cfg = Path.Combine(dir, "config.txt");
            if (!File.Exists(cfg) || !File.ReadAllText(cfg).Contains(IncludeLine, StringComparison.OrdinalIgnoreCase)) return false;
            var test = Path.Combine(dir, OurFile);
            using (new FileStream(test, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite)) { }
            return true;
        }
        catch { return false; }
    }

    /// <summary>Run as administrator (the installer does this, and the app asks once if needed):
    /// point config.txt at our file, keeping the old config as a backup, and let the app write the file.</summary>
    public static void SetUpConfigFolder()
    {
        var dir = ConfigDir() ?? throw new InvalidOperationException("Equalizer APO is not installed.");
        var cfg = Path.Combine(dir, "config.txt");
        var ours = Path.Combine(dir, OurFile);
        if (File.Exists(cfg))
        {
            var old = File.ReadAllText(cfg);
            if (!old.Contains(IncludeLine, StringComparison.OrdinalIgnoreCase))
            {
                File.WriteAllText(Path.Combine(dir, "config.before-cypress-sound.txt"), old);
                File.WriteAllText(cfg, "# Managed by Cypress Sound. Your previous config is in config.before-cypress-sound.txt.\r\n" + IncludeLine + "\r\n");
            }
        }
        else File.WriteAllText(cfg, IncludeLine + "\r\n");
        if (!File.Exists(ours)) File.WriteAllText(ours, "# Cypress Sound\r\n");
        // Let normal (non-admin) users change our file; the rest of the folder stays protected.
        var fi = new FileInfo(ours);
        var acl = fi.GetAccessControl();
        acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.Modify, AccessControlType.Allow));
        fi.SetAccessControl(acl);
    }

    static string N(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>The Equalizer APO config text for every device's EQ. Each device gets its own section,
    /// matched by its endpoint GUID, so switching outputs switches EQ with no delay.</summary>
    public static string Build(IEnumerable<(AudioDevice device, DeviceEq eq)> devices, Func<string, HeadphoneProfile?> headphone)
    {
        var sb = new StringBuilder();
        sb.Append("# Written by Cypress Sound (Cypress Works). Change the EQ in the app; edits here are replaced.\r\n");
        foreach (var (d, eq) in devices)
        {
            sb.Append("\r\n# ").Append(d.Name.Replace('\n', ' ')).Append(eq.Enabled ? "" : " (EQ off)").Append("\r\n");
            sb.Append("Device: ").Append(d.Guid).Append("\r\n");
            if (!eq.Enabled) continue;
            var hp = eq.Headphone != null ? headphone(eq.Headphone) : null;
            double pre = eq.Preamp + (hp?.Preamp ?? 0);
            if (eq.ClipGuard)
            {
                var boost = Response.MaxBoost(eq, hp?.Filters) + eq.Preamp + (hp?.Preamp ?? 0);
                if (boost > 0) pre -= boost;
            }
            if (Math.Abs(pre) > 0.005) sb.Append("Preamp: ").Append(N(pre)).Append(" dB\r\n");
            if (eq.Bands.Any(b => Math.Abs(b) > 0.005))
            {
                sb.Append("GraphicEQ: ");
                sb.Append(string.Join("; ", DeviceEq.BandHz.Select((hz, i) => $"{N(hz)} {N(eq.Bands[i])}")));
                sb.Append("\r\n");
            }
            foreach (var f in eq.Parametric.Concat(Response.ExtraFilters(eq)).Concat((IEnumerable<EqFilter>?)hp?.Filters ?? Array.Empty<EqFilter>()))
            {
                if (!f.On) continue;
                // Equalizer APO: plain LS/HS/LP/HP have a fixed slope, the C/Q variants take a Q.
                var type = f.Type switch { "LS" => "LSC", "HS" => "HSC", "LP" => "LPQ", "HP" => "HPQ", var t => t };
                sb.Append("Filter: ON ").Append(type).Append(" Fc ").Append(N(f.Fc)).Append(" Hz");
                if (f.Type is "PK" or "LS" or "HS" or "LSC" or "HSC") sb.Append(" Gain ").Append(N(f.Gain)).Append(" dB");
                sb.Append(" Q ").Append(N(f.Q)).Append("\r\n");
            }
            if (eq.Mono) sb.Append("Copy: L=0.5*L+0.5*R R=0.5*L+0.5*R\r\n");
            if (Math.Abs(eq.Balance) > 0.01)
            {
                // pan by turning the other side down (up to -30 dB at full balance)
                var cut = -30 * Math.Abs(eq.Balance);
                sb.Append("Channel: ").Append(eq.Balance > 0 ? "L" : "R").Append("\r\n");
                sb.Append("Preamp: ").Append(N(cut)).Append(" dB\r\n");
                sb.Append("Channel: all\r\n");
            }
        }
        sb.Append("\r\nDevice: all\r\n");
        return sb.ToString();
    }

    public static void Write(string text)
    {
        var dir = ConfigDir() ?? throw new InvalidOperationException("Equalizer APO is not installed.");
        var path = Path.Combine(dir, OurFile);
        // write in place (the folder itself is read-only for normal users)
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        var bytes = new UTF8Encoding(false).GetBytes(text);
        fs.Write(bytes, 0, bytes.Length);
    }
}
