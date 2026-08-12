#nullable enable
using Xunit;

namespace CdpMcp.Tests;

public sealed class HabitatExperienceLedgerTests : IDisposable
{
    readonly string _root;

    public HabitatExperienceLedgerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-xp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        HabitatExperienceLedger.RootOverrideForTests = _root;
        HabitatExperienceLedger.ResetForTests();
    }

    public void Dispose()
    {
        HabitatExperienceLedger.ResetForTests();
        HabitatExperienceLedger.RootOverrideForTests = null;
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
            /* ignore */
        }
    }

    [Fact]
    public void Record_advances_guest_Junior_to_Middle()
    {
        Assert.Equal(HabitatExperienceLedger.Position.Junior, HabitatExperienceLedger.GetPosition("guest").Position);

        HabitatExperienceLedger.Record("tip", "burn", "IDE all-in-one without lived refuse = louder crash");
        HabitatExperienceLedger.Record("guest", "refuse", "place=into wiped method — use replace");
        HabitatExperienceLedger.Record("composer", "dogfood", "PathMutate via buffer, not Cursor Write");

        var state = HabitatExperienceLedger.GetPosition("guest");
        Assert.Equal(HabitatExperienceLedger.Position.Middle, state.Position);
        Assert.Equal(3, state.LessonCount);
        Assert.False(state.PositionPinned);

        var aff = HabitatExperienceLedger.AffordanceFor(state.Position);
        Assert.True(aff.CanPromoteExperience);
        Assert.True(aff.MutateNarrow);
    }

    [Fact]
    public void SetPosition_Architect_pins_against_recompute()
    {
        HabitatExperienceLedger.SetPosition("citizen", HabitatExperienceLedger.Position.Architect);
        HabitatExperienceLedger.Record("citizen", "burn", "should not demote pinned Architect");
        var state = HabitatExperienceLedger.GetPosition("face");
        Assert.Equal(HabitatExperienceLedger.Position.Architect, state.Position);
        Assert.True(state.PositionPinned);
        // Affordance frozen: ladder decorative — no curriculum/wide-mutate from position alone.
        Assert.False(HabitatExperienceLedger.AffordanceFor(state.Position).CanSeedCurriculum);
        Assert.True(HabitatExperienceLedger.AffordanceFor(state.Position).MutateNarrow);
    }

    [Fact]
    public void Learn_xp_record_and_scene_ok()
    {
        var session = new Cdp.Core.SessionContext { ProjectRoot = _root };
        var recorded = IdeLearnChannel.Handle(session, new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = System.Text.Json.JsonSerializer.SerializeToElement("xp_record"),
            ["principal"] = System.Text.Json.JsonSerializer.SerializeToElement("human"),
            ["kind"] = System.Text.Json.JsonSerializer.SerializeToElement("vision"),
            ["line"] = System.Text.Json.JsonSerializer.SerializeToElement("Position ladder is habitat-wide, not SoftFL")
        });
        var json = System.Text.Json.JsonSerializer.Serialize(recorded);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean(), json);
        var lesson = doc.RootElement.GetProperty("lesson");
        var principal = lesson.TryGetProperty("principal", out var p)
            ? p.GetString()
            : lesson.GetProperty("Principal").GetString();
        Assert.Equal("human", principal);

        using var scene = System.Text.Json.JsonDocument.Parse(IdeLearnChannel.HandleJson(session, new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = System.Text.Json.JsonSerializer.SerializeToElement("xp_scene")
        }));
        Assert.True(scene.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(HabitatExperienceLedger.Schema, scene.RootElement.GetProperty("schema").GetString());
    }
}
