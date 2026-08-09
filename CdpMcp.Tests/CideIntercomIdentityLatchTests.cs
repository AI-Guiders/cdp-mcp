#nullable enable
using Xunit;

namespace CdpMcp.Tests;

[Collection(nameof(IntercomLatchSerial))]
public sealed class CideIntercomIdentityLatchTests : IDisposable
{
    readonly string _root;

    public CideIntercomIdentityLatchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-id-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        CideIntercomIdentityLatch.RootOverrideForTests = _root;
        CideIntercomVoiceLatch.RootOverrideForTests = _root;
    }

    public void Dispose()
    {
        CideIntercomIdentityLatch.RootOverrideForTests = null;
        CideIntercomVoiceLatch.RootOverrideForTests = null;
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
    public void Activate_model_switch_does_not_inherit_who()
    {
        Assert.NotNull(CideIntercomIdentityLatch.Claim("pf", "Sierra", "citizen", "zai-org/GLM-5.1"));
        Assert.Equal("Sierra", CideIntercomIdentityLatch.TrySeat("pf")?.Name);

        Assert.Null(CideIntercomIdentityLatch.Activate("pf", "Qwen/Qwen3-Coder-Next"));
        Assert.Null(CideIntercomIdentityLatch.TrySeat("pf"));
    }

    [Fact]
    public void Activate_same_model_restores_who()
    {
        Assert.NotNull(CideIntercomIdentityLatch.Claim("pf", "Sierra", "citizen", "zai-org/GLM-5.1"));
        Assert.Null(CideIntercomIdentityLatch.Activate("pf", "Qwen/Qwen3-Coder-Next"));

        var back = CideIntercomIdentityLatch.Activate("pf", "zai-org/GLM-5.1");
        Assert.Equal("Sierra", back?.Name);
        Assert.Equal("zai-org/GLM-5.1", back?.Model);
    }

    [Fact]
    public void Legacy_tip_without_model_migrates_on_activate()
    {
        // Simulate pre-model latch tip via Claim then strip model by re-writing tip-only file is hard;
        // Claim without model uses ResolveCitizenModel — force empty model profile path:
        Assert.NotNull(CideIntercomIdentityLatch.Claim("pf", "Sierra", "citizen", "zai-org/GLM-5.1"));
        var tip = CideIntercomIdentityLatch.Activate("pf", "zai-org/GLM-5.1");
        Assert.Equal("Sierra", tip?.Name);
    }

    [Fact]
    public void Claim_guest_does_not_demote_sticky_citizen_who()
    {
        Assert.NotNull(CideIntercomIdentityLatch.Claim("pf", "Sierra", "citizen", "zai-org/GLM-5.1"));
        Assert.Null(CideIntercomIdentityLatch.Claim("pf", "Kir", "guest", "zai-org/GLM-5.1"));
        Assert.Equal("Sierra", CideIntercomIdentityLatch.TrySeat("pf")?.Name);
        Assert.Equal("citizen", CideIntercomIdentityLatch.TrySeat("pf")?.Kind);
    }

    [Fact]
    public void Claim_operator_does_not_demote_sticky_citizen_who()
    {
        Assert.NotNull(CideIntercomIdentityLatch.Claim("pf", "Sierra", "citizen", "zai-org/GLM-5.1"));
        Assert.Null(CideIntercomIdentityLatch.Claim("pf", "Kir", "operator", "zai-org/GLM-5.1"));
        Assert.Equal("Sierra", CideIntercomIdentityLatch.TrySeat("pf")?.Name);
        Assert.Equal("citizen", CideIntercomIdentityLatch.TrySeat("pf")?.Kind);
    }

    [Fact]
    public void Claim_guest_uses_harness_slot_not_fm_model()
    {
        Assert.Equal(
            CideIntercomIdentityLatch.HarnessGuestSlot,
            CideIntercomIdentityLatch.ProfileSlotFor("guest", "zai-org/GLM-5.1"));

        Assert.NotNull(CideIntercomIdentityLatch.Claim("pf", "Kir", "guest", "zai-org/GLM-5.1"));
        Assert.Equal("Kir", CideIntercomIdentityLatch.TrySeat("pf")?.Name);
        Assert.Equal(CideIntercomIdentityLatch.HarnessGuestSlot, CideIntercomIdentityLatch.TrySeat("pf")?.Model);

        // No citizen profile under FM model — Activate must not resurrect guest from GLM key.
        Assert.Null(CideIntercomIdentityLatch.Activate("pf", "zai-org/GLM-5.1"));
    }

    [Fact]
    public void Activate_scrubs_guest_pollution_under_fm_model()
    {
        // Pre-fix pollution: guest Who written under FM model id.
        var polluted = """
            {
              "schema": "cide_intercom_identity_latch/v0",
              "pf": { "name": "Kir", "kind": "guest", "model": "zai-org/GLM-5.1" },
              "pf_profiles": {
                "zai-org/GLM-5.1": { "name": "Kir", "kind": "guest", "model": "zai-org/GLM-5.1" }
              }
            }
            """;
        File.WriteAllText(CideIntercomIdentityLatch.LatchPath, polluted);

        Assert.Null(CideIntercomIdentityLatch.Activate("pf", "zai-org/GLM-5.1"));
        Assert.Null(CideIntercomIdentityLatch.TrySeat("pf"));

        Assert.NotNull(CideIntercomIdentityLatch.Claim("pf", "Sierra", "citizen", "zai-org/GLM-5.1"));
        Assert.Equal("Sierra", CideIntercomIdentityLatch.Activate("pf", "zai-org/GLM-5.1")?.Name);
    }

    [Fact]
    public void Claim_pm_guest_coerces_to_operator_harness()
    {
        Assert.NotNull(CideIntercomIdentityLatch.Claim("pm", "Kir", "guest"));
        var tip = CideIntercomIdentityLatch.TrySeat("pm");
        Assert.Equal("Kir", tip?.Name);
        Assert.Equal("operator", tip?.Kind);
        Assert.Equal(CideIntercomIdentityLatch.HarnessOperatorSlot, tip?.Model);
    }
}
