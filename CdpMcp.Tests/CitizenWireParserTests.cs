#nullable enable
using Xunit;

namespace CdpMcp.Tests;

public class CitizenWireParserTests
{
    // Prefer repo-relative from test project dir when BaseDirectory layout differs.
    static string ReadFixture(string name)
    {
        var fromCwd = Path.GetFullPath(Path.Combine(
            Directory.GetCurrentDirectory(),
            "docs", "design", "citizen-wire-fixtures", name));
        if (File.Exists(fromCwd))
            return File.ReadAllText(fromCwd);

        var fromProj = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "docs", "design", "citizen-wire-fixtures", name));
        if (File.Exists(fromProj))
            return File.ReadAllText(fromProj);

        // Fallback: search upward for docs/design/citizen-wire-fixtures
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "docs", "design", "citizen-wire-fixtures", name);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            dir = dir.Parent;
        }

        throw new FileNotFoundException("citizen wire fixture: " + name);
    }

    [Fact]
    public void Clear_desk_fixture_parses_peer_required_fields()
    {
        var msgs = CitizenWireParser.Parse(ReadFixture("01-clear-desk.txt"));
        Assert.Single(msgs);
        var m = msgs[0];
        Assert.Equal(CitizenWireParser.Kind.Frame, m.Kind);
        Assert.Equal("desk", m.Type);
        Assert.Equal("v0", m.Version);
        Assert.Equal("A", m.Fields["cost"]);
        Assert.Contains("peer", m.Fields.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("gen=1", m.Fields["peer"]);
    }

    [Fact]
    public void Drill_fixture_parses_intent_organ_and_desk()
    {
        var msgs = CitizenWireParser.Parse(ReadFixture("02-drill-editor.txt"));
        Assert.Equal(3, msgs.Count);
        Assert.Equal(CitizenWireParser.Kind.Intent, msgs[0].Kind);
        Assert.Equal("drill editor", msgs[0].IntentText);
        Assert.Equal("organ", msgs[1].Type);
        Assert.Equal("C", msgs[1].Fields["cost"]);
        Assert.Contains("CitizenWireParser.cs", msgs[1].TrailingBody);
        Assert.Equal("desk", msgs[2].Type);
    }

    [Fact]
    public void Remount_fixture_parses_event_then_desk()
    {
        var msgs = CitizenWireParser.Parse(ReadFixture("03-remount-event.txt"));
        Assert.Equal(2, msgs.Count);
        Assert.Equal(CitizenWireParser.Kind.Event, msgs[0].Kind);
        Assert.Equal("peer", msgs[0].Type);
        Assert.Equal("remounted", msgs[0].Fields["kind"]);
        Assert.Equal("desk", msgs[1].Type);
        Assert.Contains("gen=2", msgs[1].Fields["peer"]);
    }

    [Fact]
    public void Refuse_fixture_parses_intent_and_routes_refuse()
    {
        var msgs = CitizenWireParser.Parse(ReadFixture("04-refuse-w-spray.txt"));
        Assert.Equal(2, msgs.Count);
        Assert.Equal(CitizenWireParser.Kind.Intent, msgs[0].Kind);
        Assert.Equal("seats_detail=full", msgs[0].IntentText);
        Assert.Equal("desk", msgs[1].Type);

        var routes = CitizenIntentRouter.RouteAll(msgs);
        Assert.Single(routes);
        Assert.False(routes[0].Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Refuse, routes[0].Verb);
        Assert.Contains("refuse_w_spray", routes[0].Reason!);
    }

    [Fact]
    public void Empty_and_noise_are_safe()
    {
        Assert.Empty(CitizenWireParser.Parse(null));
        Assert.Empty(CitizenWireParser.Parse(""));
        Assert.Empty(CitizenWireParser.Parse("not a wire line\n"));
    }
}
