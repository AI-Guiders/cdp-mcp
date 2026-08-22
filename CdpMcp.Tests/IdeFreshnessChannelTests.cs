#nullable enable
using System.Text.Json;
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

public sealed class IdeFreshnessChannelTests
{
    [Fact]
    public void Scene_exposes_mlp_ops()
    {
        var session = new SessionContext();
        var json = IdeFreshnessChannel.HandleJson(session, Dict(("op", "scene")));
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("freshness_channel/v1", doc.RootElement.GetProperty("schema").GetString());
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("W2-W4", doc.RootElement.GetProperty("mlp").GetProperty("status").GetString());
        Assert.True(doc.RootElement.GetProperty("safety").GetProperty("digest_is_not_provereno").GetBoolean());
    }

    [Fact]
    public void Aliases_include_avalonia_atom()
    {
        var json = IdeFreshnessChannel.HandleJson(new SessionContext(), Dict(("op", "aliases")));
        using var doc = JsonDocument.Parse(json);
        var aliases = doc.RootElement.GetProperty("aliases").EnumerateArray()
            .Select(e => e.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("avalonia", aliases);
        Assert.Contains("baseline2026", aliases);
    }

    [Fact]
    public void Watchlist_resolves_alias()
    {
        var json = IdeFreshnessChannel.HandleJson(new SessionContext(), Dict(("op", "watchlist"), ("alias", "avalonia")));
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(1, doc.RootElement.GetProperty("count").GetInt32());
        Assert.Contains("releases.atom", doc.RootElement.GetProperty("urls")[0].GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Feed_parse_atom_latest()
    {
        const string atom = """
            <?xml version="1.0" encoding="utf-8"?>
            <feed xmlns="http://www.w3.org/2005/Atom">
              <title>Releases</title>
              <entry>
                <id>tag:github.com,2008:Repository/123/12.1.0</id>
                <title>12.1.0</title>
                <updated>2026-07-09T13:14:06Z</updated>
                <link rel="alternate" href="https://github.com/AvaloniaUI/Avalonia/releases/tag/12.1.0"/>
                <summary>Avalonia 12.1.0</summary>
              </entry>
            </feed>
            """;
        Assert.True(IdeFreshnessFeed.LooksLikeFeed("application/atom+xml", atom));
        var items = IdeFreshnessFeed.Parse(atom);
        Assert.Single(items);
        Assert.Equal("12.1.0", items[0].Title);
        Assert.Contains("12.1.0", items[0].Id, StringComparison.Ordinal);
    }

    [Fact]
    public void Scan_empty_watchlist_fails_clean()
    {
        var json = IdeFreshnessChannel.HandleJson(new SessionContext(), Dict(("op", "scan")));
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("watchlist_empty", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void Nrt_extract_triggers_from_markdown()
    {
        const string md = """
            ## Next review triggers
            - PHP 8.5 stable
            - Laravel 13+

            ## Other
            - ignore
            """;
        var triggers = IdeFreshnessNrt.ExtractTriggers(md);
        Assert.Equal(2, triggers.Count);
        Assert.Contains("PHP 8.5 stable", triggers[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Nrt_op_resolves_php_alias()
    {
        var json = IdeFreshnessChannel.HandleJson(new SessionContext(), Dict(("op", "nrt"), ("alias", "php")));
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("freshness_nrt/v1", doc.RootElement.GetProperty("schema").GetString());
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("software-php-laravel", doc.RootElement.GetProperty("domain").GetString());
        Assert.True(doc.RootElement.GetProperty("triggers").GetArrayLength() >= 1);
    }

    [Fact]
    public void Arm_nightly_schedule_persists()
    {
        var json = IdeFreshnessChannel.HandleJson(
            new SessionContext(),
            Dict(("op", "arm"), ("when", "nightly"), ("alias", "avalonia")));
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("armed").GetBoolean());
        Assert.Equal("nightly", doc.RootElement.GetProperty("when").GetString());
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("due_utc").GetString()));

        var disarm = IdeFreshnessChannel.HandleJson(new SessionContext(), Dict(("op", "disarm")));
        using var d2 = JsonDocument.Parse(disarm);
        Assert.False(d2.RootElement.GetProperty("armed").GetBoolean());
    }

    [Fact]
    public void Clear_all_cache_ok()
    {
        var json = IdeFreshnessChannel.HandleJson(new SessionContext(), Dict(("op", "clear")));
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("all", doc.RootElement.GetProperty("scope").GetString());
    }

    static Dictionary<string, JsonElement> Dict(params (string Key, string Value)[] pairs)
    {
        var d = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (k, v) in pairs)
            d[k] = JsonSerializer.SerializeToElement(v);
        return d;
    }
}
