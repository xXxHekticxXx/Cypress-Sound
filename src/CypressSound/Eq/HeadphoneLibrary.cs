// Headphone correction profiles from the AutoEq project (github.com/jaakkopasanen/AutoEq, MIT license):
// about 5,000 headphones and earbuds, each a short list of filters that makes it sound neutral.
// tools/build_autoeq.py packs them into Assets/autoeq.json when the app is built.
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CypressSound.Eq;

public sealed class HeadphoneProfile
{
    public string Name { get; set; } = "";
    public string Source { get; set; } = "";
    public double Preamp { get; set; }
    public List<EqFilter> Filters { get; set; } = new();
    public override string ToString() => $"{Name}  ·  {Source}";
}

public static partial class HeadphoneLibrary
{
    static List<HeadphoneProfile>? _all;

    public static IReadOnlyList<HeadphoneProfile> All
    {
        get
        {
            if (_all != null) return _all;
            _all = new List<HeadphoneProfile>();
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "Assets", "autoeq.json");
                if (File.Exists(path))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(path));
                    foreach (var e in doc.RootElement.EnumerateArray())
                    {
                        var p = Parse(e.GetProperty("eq").GetString() ?? "");
                        p.Name = e.GetProperty("name").GetString() ?? "";
                        p.Source = e.GetProperty("source").GetString() ?? "";
                        _all.Add(p);
                    }
                }
            }
            catch { }
            return _all;
        }
    }

    public static HeadphoneProfile? Find(string key) => All.FirstOrDefault(p => p.ToString() == key) ?? All.FirstOrDefault(p => p.Name == key);

    public static IEnumerable<HeadphoneProfile> Search(string q)
    {
        var words = q.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return All.Where(p => words.All(w => p.Name.Contains(w, StringComparison.OrdinalIgnoreCase))).Take(200);
    }

    [GeneratedRegex(@"Filter\s*\d*\s*:\s*ON\s+(PK|LSC|HSC|LS|HS|LP|HP)\s+Fc\s+([\d.]+)\s*Hz(?:\s+Gain\s+(-?[\d.]+)\s*dB)?(?:\s+Q\s+([\d.]+))?", RegexOptions.IgnoreCase)]
    private static partial Regex FilterRe();
    [GeneratedRegex(@"Preamp\s*:\s*(-?[\d.]+)\s*dB", RegexOptions.IgnoreCase)]
    private static partial Regex PreampRe();

    /// <summary>Reads AutoEq's "ParametricEQ.txt" format (which is Equalizer APO's own filter syntax).</summary>
    public static HeadphoneProfile Parse(string text)
    {
        var p = new HeadphoneProfile();
        var pm = PreampRe().Match(text);
        if (pm.Success) p.Preamp = double.Parse(pm.Groups[1].Value, CultureInfo.InvariantCulture);
        foreach (Match m in FilterRe().Matches(text))
        {
            p.Filters.Add(new EqFilter
            {
                Type = m.Groups[1].Value.ToUpperInvariant(),
                Fc = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                Gain = m.Groups[3].Success ? double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) : 0,
                Q = m.Groups[4].Success ? double.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture) : 0.71,
            });
        }
        return p;
    }
}
