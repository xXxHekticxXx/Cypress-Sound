// What the EQ for one device looks like, the built-in presets, and the frequency response math
// (used for the curve on screen and for the clipping guard).
namespace CypressSound.Eq;

public sealed class EqFilter
{
    public bool On { get; set; } = true;
    /// <summary>PK = peak/bell, LS/HS = low/high shelf, LP/HP = low/high pass (Equalizer APO names).</summary>
    public string Type { get; set; } = "PK";
    public double Fc { get; set; } = 1000;
    public double Gain { get; set; }
    public double Q { get; set; } = 1.0;
    public EqFilter Clone() => (EqFilter)MemberwiseClone();
}

public sealed class DeviceEq
{
    public static readonly double[] BandHz = { 31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };
    public static readonly string[] BandLabel = { "31", "62", "125", "250", "500", "1k", "2k", "4k", "8k", "16k" };

    public bool Enabled { get; set; } = true;
    public string Preset { get; set; } = "Flat";
    public bool Edited { get; set; }
    public double[] Bands { get; set; } = new double[10];
    public List<EqFilter> Parametric { get; set; } = new();
    public string? Headphone { get; set; } // AutoEq profile name, applied on top
    public double Preamp { get; set; }
    public bool ClipGuard { get; set; } = true;
    public bool NightMode { get; set; }
    public double NightStrength { get; set; } = 0.5; // 0..1
    public double BassBoost { get; set; }             // dB, 0..12
    public bool Mono { get; set; }
    public double Balance { get; set; }               // -1 (left) .. 1 (right)

    // Volume boost: louder than Windows' 100%.
    public bool Boost { get; set; }
    public double BoostDb { get; set; } = 6;           // 0..12

    // Sound bar setup. Over HDMI ARC / optical the PC only sends 2 channels, so the bar makes its own
    // center, sub and rear sound from them; these settings steer how much goes to each.
    public bool SoundBar { get; set; }
    public double SbSub { get; set; }                  // dB, -6..10: deep bass (what the subwoofer plays)
    public double SbVoice { get; set; }                // dB, -6..8: voice band (what the center plays)
    public double SbSurround { get; set; }             // 0..1: stereo width (what the bar's surround mode sends to the rears)
    public double SbRear { get; set; }                 // dB, -10..10: rear level, only when Windows sends real 5.1/7.1
    public bool SbFill { get; set; } = true;           // 5.1/7.1 only: send stereo to center, sub and rears

    /// <summary>Channels Windows sends to this device right now (2 over HDMI ARC/optical). Not saved.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public int Channels { get; set; } = 2;

    public DeviceEq Clone()
    {
        var c = (DeviceEq)MemberwiseClone();
        c.Bands = (double[])Bands.Clone();
        c.Parametric = Parametric.Select(f => f.Clone()).ToList();
        return c;
    }
}

public sealed class Preset
{
    public string Name { get; set; } = "";
    public double[] Bands { get; set; } = new double[10];
    public double BassBoost { get; set; }
    public bool NightMode { get; set; }
    public bool Builtin { get; set; }
    public bool ForMic { get; set; }
    /// <summary>Only used by an auto profile, not shown with the EQ presets.</summary>
    public bool ProfileOnly { get; set; }
}

public static class Presets
{
    public static readonly IReadOnlyList<Preset> Builtin = new List<Preset>
    {
        P("Flat", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
        P("Bass boost", 6, 5, 4, 1, 0, 0, 0, 0, 0, 0),
        P("Voice / podcast", -6, -4, -1, 0, 1, 2, 4, 3, 1, -1),
        P("Footsteps (gaming)", -4, -3, -2, -1, 0, 2, 4, 5, 3, 0),
        // Modern Warfare 4 tunings for the Call of Duty auto profile (bands: 31 62 125 250 500 1k 2k 4k 8k 16k Hz).
        // Built from the shape pro DMZ player Expel tuned into MW4's own hearing EQ (+250 Hz footstep thud, dip at
        // 1-2 kHz where gunfire and ambience pile up, lift at 4-6 kHz for boot scuffs and gear rattle) scaled to safe dB,
        // plus the usual footstep advice: cut sub-bass rumble that masks steps, and even out loud vs quiet sounds
        // (night mode = the "Loudness Equalization" the guides tell PC players to turn on).
        // Headphones: sit right at the ear, so the sub-bass cut is strong and the treble lift is moderate (no fatigue).
        Only(P("MW4 · Headphones", -6, -4, -1, 3, 0, -2, -1, 3, 3, 1, night: true)),
        // TV / speakers through the PC: the room smears detail and small speakers can't play deep bass, so the lowest
        // band is cut harder (frees headroom), low-mid boxiness is trimmed and the 4-8 kHz detail is lifted a bit more.
        Only(P("MW4 · TV & speakers", -8, -3, 1, 3, -1, -2, 0, 4, 4, 1, night: true)),
        // kept so older settings that name it still work
        Only(P("Call of Duty", -6, -4, 0, -2, -2, 1, 4, 6, 3, -1, night: true)),
        P("Movie night", 6, 5, 3, 0, -1, 1, 3, 2, 0, -2, night: true),
        P("TV speakers fix", 4, 4, 3, 1, 0, 1, 3, 3, 1, 0),
        P("Music: V-shape", 5, 4, 2, 0, -2, -2, 0, 2, 4, 5),
        P("Treble boost", 0, 0, 0, 0, 0, 1, 2, 4, 5, 5),
        P("Sound bar: clear dialogue", -2, -1, -1, 0, 1, 3, 4, 3, 1, 0),
        P("Sound bar: music", 3, 3, 1, 0, -1, 0, 1, 2, 3, 3),
        P("Laptop speakers", -8, -6, -2, 1, 2, 2, 3, 2, 1, 0),
        P("Mic: clear voice", -12, -10, -4, -1, 0, 1, 3, 3, 1, -2, mic: true),
        P("Mic: deep radio voice", -10, -3, 3, 2, 0, -1, 2, 3, 1, -3, mic: true),
    };

    static Preset Only(Preset p) { p.ProfileOnly = true; return p; }

    static Preset P(string name, double a, double b, double c, double d, double e, double f, double g, double h, double i, double j, bool night = false, bool mic = false) =>
        new() { Name = name, Bands = new[] { a, b, c, d, e, f, g, h, i, j }, NightMode = night, Builtin = true, ForMic = mic };
}

/// <summary>Frequency response of the whole chain, in dB, using the same filter formulas Equalizer APO uses
/// (Robert Bristow-Johnson's biquads; GraphicEQ interpolates linearly on a log-frequency scale).</summary>
public static class Response
{
    const double Fs = 48000;

    public static double GraphicDb(double[] bands, double f)
    {
        var hz = DeviceEq.BandHz;
        if (f <= hz[0]) return bands[0];
        if (f >= hz[^1]) return bands[^1];
        for (int i = 0; i < hz.Length - 1; i++)
        {
            if (f >= hz[i] && f <= hz[i + 1])
            {
                var t = (Math.Log(f) - Math.Log(hz[i])) / (Math.Log(hz[i + 1]) - Math.Log(hz[i]));
                return bands[i] + t * (bands[i + 1] - bands[i]);
            }
        }
        return 0;
    }

    public static double FilterDb(EqFilter flt, double f)
    {
        if (!flt.On) return 0;
        double A = Math.Pow(10, flt.Gain / 40), w0 = 2 * Math.PI * flt.Fc / Fs, cos = Math.Cos(w0), sin = Math.Sin(w0);
        double q = flt.Q <= 0 ? 0.707 : flt.Q;
        double alpha = sin / (2 * q);
        double b0, b1, b2, a0, a1, a2;
        switch (flt.Type)
        {
            case "LS":
            case "LSC":
            {
                var sa = 2 * Math.Sqrt(A) * alpha;
                b0 = A * ((A + 1) - (A - 1) * cos + sa); b1 = 2 * A * ((A - 1) - (A + 1) * cos); b2 = A * ((A + 1) - (A - 1) * cos - sa);
                a0 = (A + 1) + (A - 1) * cos + sa; a1 = -2 * ((A - 1) + (A + 1) * cos); a2 = (A + 1) + (A - 1) * cos - sa;
                break;
            }
            case "HS":
            case "HSC":
            {
                var sa = 2 * Math.Sqrt(A) * alpha;
                b0 = A * ((A + 1) + (A - 1) * cos + sa); b1 = -2 * A * ((A - 1) + (A + 1) * cos); b2 = A * ((A + 1) + (A - 1) * cos - sa);
                a0 = (A + 1) - (A - 1) * cos + sa; a1 = 2 * ((A - 1) - (A + 1) * cos); a2 = (A + 1) - (A - 1) * cos - sa;
                break;
            }
            case "LP":
                b0 = (1 - cos) / 2; b1 = 1 - cos; b2 = (1 - cos) / 2; a0 = 1 + alpha; a1 = -2 * cos; a2 = 1 - alpha; break;
            case "HP":
                b0 = (1 + cos) / 2; b1 = -(1 + cos); b2 = (1 + cos) / 2; a0 = 1 + alpha; a1 = -2 * cos; a2 = 1 - alpha; break;
            default: // PK
                b0 = 1 + alpha * A; b1 = -2 * cos; b2 = 1 - alpha * A; a0 = 1 + alpha / A; a1 = -2 * cos; a2 = 1 - alpha / A; break;
        }
        double w = 2 * Math.PI * f / Fs;
        // |H(e^jw)|^2
        double cw = Math.Cos(w), c2w = Math.Cos(2 * w), sw = Math.Sin(w), s2w = Math.Sin(2 * w);
        double nr = b0 + b1 * cw + b2 * c2w, ni = -(b1 * sw + b2 * s2w);
        double dr = a0 + a1 * cw + a2 * c2w, di = -(a1 * sw + a2 * s2w);
        double mag2 = (nr * nr + ni * ni) / (dr * dr + di * di);
        return 10 * Math.Log10(Math.Max(mag2, 1e-12));
    }

    public static IEnumerable<EqFilter> ExtraFilters(DeviceEq eq)
    {
        if (eq.BassBoost > 0.05) yield return new EqFilter { Type = "LS", Fc = 80, Gain = eq.BassBoost, Q = 0.71 };
        if (eq.SoundBar && eq.Channels < 6)
        {
            // Sound bars split stereo themselves: deep bass goes to the sub, the voice band to the center.
            if (Math.Abs(eq.SbSub) > 0.05) yield return new EqFilter { Type = "LS", Fc = 90, Gain = eq.SbSub, Q = 0.71 };
            if (Math.Abs(eq.SbVoice) > 0.05) yield return new EqFilter { Type = "PK", Fc = 2200, Gain = eq.SbVoice, Q = 0.8 };
        }
        if (eq.NightMode)
        {
            // Night mode: lift dialogue, tame the deepest bass and the harshest highs.
            yield return new EqFilter { Type = "PK", Fc = 2500, Gain = 1 + 3 * eq.NightStrength, Q = 0.9 };
            yield return new EqFilter { Type = "LS", Fc = 60, Gain = -4 * eq.NightStrength, Q = 0.71 };
            yield return new EqFilter { Type = "HS", Fc = 9000, Gain = -2 * eq.NightStrength, Q = 0.71 };
        }
    }

    /// <summary>Total boost in dB at frequency f (graphic + parametric + extras + headphone profile).</summary>
    public static double TotalDb(DeviceEq eq, IReadOnlyList<EqFilter>? headphone, double f)
    {
        double db = GraphicDb(eq.Bands, f);
        foreach (var p in eq.Parametric) db += FilterDb(p, f);
        foreach (var p in ExtraFilters(eq)) db += FilterDb(p, f);
        if (headphone != null) foreach (var p in headphone) db += FilterDb(p, f);
        return db;
    }

    /// <summary>Highest boost anywhere from 20 Hz to 20 kHz; the clipping guard lowers the preamp by this much.</summary>
    public static double MaxBoost(DeviceEq eq, IReadOnlyList<EqFilter>? headphone)
    {
        double max = double.NegativeInfinity;
        for (int i = 0; i <= 240; i++)
        {
            var f = 20 * Math.Pow(1000, i / 240.0);
            max = Math.Max(max, TotalDb(eq, headphone, f));
        }
        return max;
    }
}
