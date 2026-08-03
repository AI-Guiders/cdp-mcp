#nullable enable
using System.Text.Json;
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

[CollectionDefinition("IdeWaveStore", DisableParallelization = true)]
public sealed class IdeWaveStoreCollection;

[Collection("IdeWaveStore")]
public sealed class IdeWaveChannelTests
{
    [Fact]
    public void Seed_scene_item_done_shipped_roundtrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cdp-wave-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "active-wave.json");
        IdeWaveChannel.FilePathOverride = () => path;
        try
        {
            var seed = IdeWaveChannel.Handle(new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["op"] = JsonSerializer.SerializeToElement("seed"),
                ["title"] = JsonSerializer.SerializeToElement("Throughput"),
                ["items"] = JsonSerializer.SerializeToElement("a;b;c")
            });
            var seedJson = JsonSerializer.Serialize(seed);
            Assert.Contains("Throughput", seedJson, StringComparison.Ordinal);
            Assert.Contains("\"total\":3", seedJson.Replace(" ", ""), StringComparison.OrdinalIgnoreCase);
            Assert.True(IdeWaveChannel.HasActiveOpen());

            var scene = JsonSerializer.Serialize(IdeWaveChannel.Handle());
            Assert.Contains("Throughput", scene, StringComparison.Ordinal);

            IdeWaveChannel.Handle(new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["op"] = JsonSerializer.SerializeToElement("item_done"),
                ["label"] = JsonSerializer.SerializeToElement("b")
            });
            Assert.Contains("1/3", IdeWaveChannel.PulseLine(), StringComparison.Ordinal);

            var shipped = JsonSerializer.Serialize(IdeWaveChannel.Handle(new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["op"] = JsonSerializer.SerializeToElement("shipped")
            }));
            Assert.Contains("shipped", shipped, StringComparison.OrdinalIgnoreCase);
            Assert.False(IdeWaveChannel.HasActiveOpen());
        }
        finally
        {
            IdeWaveChannel.FilePathOverride = null;
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void Repl_wave_seed_direct()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cdp-wave-repl-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "active-wave.json");
        IdeWaveChannel.FilePathOverride = () => path;
        try
        {
            var applied = IdeRepl.Apply("wave seed x;y;z", new Dictionary<string, JsonElement>());
            Assert.NotNull(applied);
            Assert.NotNull(applied!.Value.Direct);
            var json = JsonSerializer.Serialize(applied.Value.Direct);
            Assert.Contains("x", json, StringComparison.Ordinal);
            Assert.Contains("y", json, StringComparison.Ordinal);
        }
        finally
        {
            IdeWaveChannel.FilePathOverride = null;
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void Repl_wave_seed_title_without_items_key_does_not_invent_labels()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cdp-wave-title-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "active-wave.json");
        IdeWaveChannel.FilePathOverride = () => path;
        try
        {
            var applied = IdeRepl.Apply(
                "wave seed title=0.5.646 polish inventory SoftOrgan coverage",
                new Dictionary<string, JsonElement>());
            Assert.NotNull(applied);
            var json = JsonSerializer.Serialize(applied!.Value.Direct);
            Assert.Contains("items_required", json, StringComparison.OrdinalIgnoreCase);

            var ok = IdeRepl.Apply(
                "wave seed title=0.5.646 items=a;b;c",
                new Dictionary<string, JsonElement>());
            Assert.NotNull(ok);
            var okJson = JsonSerializer.Serialize(ok!.Value.Direct);
            Assert.Contains("\"total\":3", okJson.Replace(" ", ""), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("0.5.646", okJson, StringComparison.Ordinal);
        }
        finally
        {
            IdeWaveChannel.FilePathOverride = null;
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }
}

public sealed class IdeInventoryVerifyWaveTests
{
    [Fact]
    public void Inventory_scene_lists_gaps()
    {
        var session = new SessionContext();
        var json = IdeInventoryChannel.HandleJson(session);
        Assert.Contains("gaps", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Soft FileLines", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("batch_size_recommend", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Inventory_softorgan_host_probe_reports_coverage()
    {
        var snap = IdeInventoryChannel.ProbeSoftOrganHosts();
        Assert.True(snap.Total > 0);
        Assert.True(
            snap.Covered >= snap.Total - 2,
            $"unexpected SoftOrgan host gaps: {string.Join(", ", snap.Missing)}");
        var json = IdeInventoryChannel.HandleJson(new SessionContext());
        Assert.Contains("softorgan_host", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("meta-host-softorgans", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_wave_scene_has_checklist_not_deploy()
    {
        var session = new SessionContext();
        var json = IdeVerifyWaveChannel.HandleJson(session);
        Assert.Contains("checklist", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dual_hard", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("KillRunning", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("terminal_", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Citizen_routes_inventory_and_verify_wave()
    {
        var inv = CitizenIntentRouter.RouteOne("inventory");
        Assert.True(inv.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Inventory, inv.Verb);
        Assert.Equal("inventory", inv.Go);

        var vw = CitizenIntentRouter.RouteOne("cdp_verify_wave");
        Assert.True(vw.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.VerifyWave, vw.Verb);
        Assert.Equal("verify_wave", vw.Go);
    }
}

public sealed class IdePressureWaveFieldTests
{
    [Fact]
    public void Stash_wave_arg_roundtrips_on_recall()
    {
        // Use isolated pressure path via existing seat files is hard —
        // exercise ResolveWave via stash when FilePath is live seat; prefer body ## wave parse unit via Handle.
        var session = new SessionContext { ProjectRoot = Path.GetTempPath() };
        var body = """
            ## WAVE 0.5.645
            open work

            ## wave
            - alpha
            - beta

            ## next
            ship
            """;
        var stash = IdePressureChannel.Handle(session, new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement("stash"),
            ["body"] = JsonSerializer.SerializeToElement(body),
            ["wave"] = JsonSerializer.SerializeToElement("""["gamma","delta"]""")
        });
        var stashJson = JsonSerializer.Serialize(stash);
        Assert.Contains("gamma", stashJson, StringComparison.Ordinal);
        Assert.Contains("delta", stashJson, StringComparison.Ordinal);

        var recall = IdePressureChannel.Handle(session, new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement("recall")
        });
        var recallJson = JsonSerializer.Serialize(recall);
        Assert.Contains("gamma", recallJson, StringComparison.Ordinal);
    }
}

[Collection("IdeWaveStore")]
public sealed class IdeAlertBipedMillTests
{
    [Fact]
    public void Act_without_wave_raises_biped_mill()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cdp-wave-sa-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "active-wave.json");
        IdeWaveChannel.FilePathOverride = () => path;
        try
        {
            IdeWaveChannel.Handle(new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["op"] = JsonSerializer.SerializeToElement("clear")
            });
            var quality = new QualityGates.QualitySnap(Enabled: false, Warn: 0, Fail: 0, SuggestSniper: false, Pulse: "off");
            var snap = IdeAlertChannel.Build(new IdeAlertChannel.Inputs(
                quality,
                DiskChanged: 0,
                DapActive: false,
                DapStopped: false,
                SessionAct: true,
                NoActiveWave: true));
            Assert.Contains(snap.Lines, l => l.Contains("biped_mill", StringComparison.Ordinal));
            Assert.Equal(IdeAlertChannel.Level.Warn, snap.Level);
            Assert.NotNull(snap.Explain);
            Assert.Equal("biped_mill", snap.Explain!.Reason);
        }
        finally
        {
            IdeWaveChannel.FilePathOverride = null;
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }
}
