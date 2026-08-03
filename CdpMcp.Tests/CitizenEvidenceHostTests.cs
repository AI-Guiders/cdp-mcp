#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenEvidenceHostTests
{
    [Fact]
    public void Route_evidence_requires_text_or_path()
    {
        var bare = CitizenIntentRouter.RouteOne("evidence");
        Assert.False(bare.Ok);
        Assert.Equal("evidence_input_required", bare.Reason);

        var withText = CitizenIntentRouter.RouteOne("evidence text=\"error CS0001: boom\"");
        Assert.True(withText.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Evidence, withText.Verb);
        Assert.Equal("auto", withText.Op);
        Assert.Equal("error CS0001: boom", withText.Tool);
        Assert.Equal("report", withText.Go);
    }

    [Fact]
    public void Route_kind_and_compounds()
    {
        var build = CitizenIntentRouter.RouteOne("evidence build text=\"error CS0001\"");
        Assert.True(build.Ok);
        Assert.Equal("build", build.Op);

        var keyed = CitizenIntentRouter.RouteOne("evidence kind=test text=\"Failed Assert.Equal\"");
        Assert.True(keyed.Ok);
        Assert.Equal("test", keyed.Op);

        var compound = CitizenIntentRouter.RouteOne("evidence_build text=\"error CS0001\"");
        Assert.True(compound.Ok);
        Assert.Equal("build", compound.Op);

        var cdp = CitizenIntentRouter.RouteOne("cdp_evidence path=logs/build.log");
        Assert.True(cdp.Ok);
        Assert.Equal("auto", cdp.Op);
        Assert.Equal("logs/build.log", cdp.Path);
    }

    [Fact]
    public void Route_unknown_kind_and_no_steal_report()
    {
        var bad = CitizenIntentRouter.RouteOne("evidence boom text=\"x\"");
        Assert.False(bad.Ok);
        Assert.Equal("evidence_kind_unknown", bad.Reason);

        var report = CitizenIntentRouter.RouteOne("report");
        Assert.NotEqual(CitizenIntentRouter.Verb.Evidence, report.Verb);
    }

    [Fact]
    public void Execute_evidence_with_override_ok()
    {
        CitizenRouteHost.UnbindLifecycle();
        IReadOnlyDictionary<string, JsonElement>? seen = null;
        CitizenRouteHost.EvidenceDispatchOverride = args =>
        {
            seen = args;
            return """{"schema":"evidence/v0","ok":true,"source":"build","itemCount":1,"pulse":"evidence · ok"}""";
        };
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("evidence kind=build text=\"error CS0001\"")
            ]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("evidence", applied[0].Action);
            Assert.Equal("report", applied[0].Go);
            Assert.NotNull(seen);
            Assert.Equal("build", seen!["kind"].GetString());
            Assert.Equal("error CS0001", seen["text"].GetString());
            Assert.Contains("evidence", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.EvidenceDispatchOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_evidence_path_passes_path()
    {
        CitizenRouteHost.UnbindLifecycle();
        IReadOnlyDictionary<string, JsonElement>? seen = null;
        CitizenRouteHost.EvidenceDispatchOverride = args =>
        {
            seen = args;
            return """{"schema":"evidence/v0","ok":true,"itemCount":0}""";
        };
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("cdp_evidence path=tmp/log.txt kind=shell")
            ]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.NotNull(seen);
            Assert.Equal("shell", seen!["kind"].GetString());
            Assert.Equal("tmp/log.txt", seen["path"].GetString());
        }
        finally
        {
            CitizenRouteHost.EvidenceDispatchOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }
}
