#nullable enable
using System.Text.Json;
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

public sealed class WebcamWindowMaximizeTests
{
    static Dictionary<string, JsonElement> Args(params (string key, object value)[] pairs)
    {
        var d = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (key, value) in pairs)
        {
            d[key] = value switch
            {
                bool b => JsonSerializer.SerializeToElement(b),
                int i => JsonSerializer.SerializeToElement(i),
                string s => JsonSerializer.SerializeToElement(s),
                _ => JsonSerializer.SerializeToElement(value)
            };
        }

        return d;
    }

    [Fact]
    public void WantMaximize_true_for_maximize_or_enlarge()
    {
        Assert.True(IdeWebcamChannel.WantMaximize(Args(("maximize", true))));
        Assert.True(IdeWebcamChannel.WantMaximize(Args(("enlarge", true))));
        Assert.False(IdeWebcamChannel.WantMaximize(Args(("maximize", false))));
        Assert.False(IdeWebcamChannel.WantMaximize(Args()));
    }

    [Fact]
    public void Opt_coerces_json_number_hwnd()
    {
        // Face SoftInstrument: go_args hwnd=1510166 as Number must survive Opt → TryParseHwnd.
        var args = Args(("hwnd", 1510166), ("op", "window_list"));
        Assert.Equal("1510166", IdeWebcamChannel.OptForTests(args, "hwnd"));
    }

    [Fact]
    public void Scene_hint_mentions_maximize_peel()
    {
        var json = IdeWebcamChannel.HandleJson(
            new SessionContext { ProjectRoot = Path.GetTempPath() },
            Args(("op", "scene")));
        using var doc = JsonDocument.Parse(json);
        var hint = doc.RootElement.GetProperty("hint").GetString();
        Assert.Contains("maximize", hint, StringComparison.OrdinalIgnoreCase);
    }
}
