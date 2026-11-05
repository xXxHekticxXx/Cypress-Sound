using CypressSound.Audio;
using CypressSound.Eq;
using Xunit;

namespace CypressSound.Tests;

public class EqTests
{
    static AudioDevice Dev(string name = "Headphones") =>
        new("{0.0.0.00000000}.{11111111-2222-3333-4444-555555555555}", name, "Realtek", DataFlow.Render, "🎧");

    [Fact]
    public void Guid_is_the_endpoint_part()
    {
        Assert.Equal("{11111111-2222-3333-4444-555555555555}", Dev().Guid);
    }

    [Fact]
    public void Flat_eq_writes_only_the_device_line()
    {
        var text = ApoConfig.Build(new[] { (Dev(), new DeviceEq()) }, _ => null);
        Assert.Contains("Device: {11111111-2222-3333-4444-555555555555}", text);
        Assert.DoesNotContain("GraphicEQ", text);
        Assert.DoesNotContain("Preamp", text);
        Assert.EndsWith("Device: all\r\n", text);
    }

    [Fact]
    public void Graphic_eq_and_clip_guard()
    {
        var eq = new DeviceEq { Bands = new double[] { 6, 5, 4, 1, 0, 0, 0, 0, 0, 0 } };
        var text = ApoConfig.Build(new[] { (Dev(), eq) }, _ => null);
        Assert.Contains("GraphicEQ: 31 6; 62 5; 125 4; 250 1; 500 0; 1000 0; 2000 0; 4000 0; 8000 0; 16000 0", text);
        Assert.Contains("Preamp: -6 dB", text);
    }

    [Fact]
    public void Eq_off_writes_nothing_for_that_device()
    {
        var eq = new DeviceEq { Enabled = false, Bands = new double[] { 6, 5, 4, 1, 0, 0, 0, 0, 0, 0 } };
        var text = ApoConfig.Build(new[] { (Dev(), eq) }, _ => null);
        Assert.Contains("(EQ off)", text);
        Assert.DoesNotContain("GraphicEQ", text);
    }

    [Fact]
    public void Mono_balance_bass_and_night()
    {
        var eq = new DeviceEq { Mono = true, Balance = 0.5, BassBoost = 6, NightMode = true, ClipGuard = false };
        var text = ApoConfig.Build(new[] { (Dev(), eq) }, _ => null);
        Assert.Contains("Copy: L=0.5*L+0.5*R R=0.5*L+0.5*R", text);
        Assert.Contains("Channel: L\r\nPreamp: -15 dB\r\nChannel: all", text);
        Assert.Contains("Filter: ON LSC Fc 80 Hz Gain 6 dB Q 0.71", text);
        Assert.Contains("Filter: ON PK Fc 2500 Hz", text);
    }

    [Fact]
    public void Peak_filter_has_its_gain_at_the_center()
    {
        var f = new EqFilter { Type = "PK", Fc = 1000, Gain = 6, Q = 1 };
        Assert.InRange(Response.FilterDb(f, 1000), 5.9, 6.1);
        Assert.InRange(Response.FilterDb(f, 50), -0.2, 0.2);
    }

    [Fact]
    public void Shelf_reaches_its_gain_far_below()
    {
        var f = new EqFilter { Type = "LSC", Fc = 100, Gain = 8, Q = 0.71 };
        Assert.InRange(Response.FilterDb(f, 20), 7.5, 8.1);
        Assert.InRange(Response.FilterDb(f, 5000), -0.1, 0.1);
    }

    [Fact]
    public void Graphic_interpolates_between_bands()
    {
        var b = new double[] { 0, 0, 0, 0, 0, 6, 0, 0, 0, 0 };
        Assert.Equal(6, Response.GraphicDb(b, 1000), 3);
        Assert.InRange(Response.GraphicDb(b, 1414), 2.9, 3.1);
    }

    [Fact]
    public void AutoEq_text_parses()
    {
        var p = HeadphoneLibrary.Parse("Preamp: -6.2 dB\nFilter 1: ON LSC Fc 105 Hz Gain 5.5 dB Q 0.70\nFilter 2: ON PK Fc 180 Hz Gain -3.3 dB Q 0.57");
        Assert.Equal(-6.2, p.Preamp, 3);
        Assert.Equal(2, p.Filters.Count);
        Assert.Equal("LSC", p.Filters[0].Type);
        Assert.Equal(-3.3, p.Filters[1].Gain, 3);
    }

    [Fact]
    public void Headphone_library_ships_with_the_app()
    {
        Assert.True(HeadphoneLibrary.All.Count > 3000, $"only {HeadphoneLibrary.All.Count} headphone profiles");
        Assert.NotEmpty(HeadphoneLibrary.Search("HD 600"));
    }

    [Fact]
    public void Presets_all_have_ten_bands()
    {
        Assert.All(Presets.Builtin, p => Assert.Equal(10, p.Bands.Length));
    }
}
