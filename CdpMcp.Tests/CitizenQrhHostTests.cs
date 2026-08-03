#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenQrhHostTests
{
    [Fact]
    public void Route_qrh_alone_is_index()
    {
        var r = CitizenIntentRouter.RouteOne("qrh");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Qrh, r.Verb);
        Assert.Equal("index", r.Op);
        Assert.Equal("qrh", r.Go);
    }

    [Fact]
    public void Route_eqrh_and_compounds()
    {
        var eqrh = CitizenIntentRouter.RouteOne("eqrh");
        Assert.True(eqrh.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Qrh, eqrh.Verb);
        Assert.Equal("index", eqrh.Op);

        var open = CitizenIntentRouter.RouteOne("qrh_open id=intake-brief");
        Assert.True(open.Ok);
        Assert.Equal("open", open.Op);
        Assert.Equal("intake-brief", open.Path);

        var search = CitizenIntentRouter.RouteOne("qrh search q=path");
        Assert.True(search.Ok);
        Assert.Equal("search", search.Op);

        var positional = CitizenIntentRouter.RouteOne("qrh open dap-pdb-lock");
        Assert.True(positional.Ok);
        Assert.Equal("open", positional.Op);
        Assert.Equal("dap-pdb-lock", positional.Path);
    }

    [Fact]
    public void Route_no_steal_bare_search_or_open()
    {
        var bareSearch = CitizenIntentRouter.RouteOne("search");
        Assert.NotEqual(CitizenIntentRouter.Verb.Qrh, bareSearch.Verb);

        var bareOpen = CitizenIntentRouter.RouteOne("open");
        Assert.NotEqual(CitizenIntentRouter.Verb.Qrh, bareOpen.Verb);

        var bareFind = CitizenIntentRouter.RouteOne("find");
        Assert.NotEqual(CitizenIntentRouter.Verb.Qrh, bareFind.Verb);
    }

    [Fact]
    public void Route_qrh_unknown_op_fails()
    {
        var r = CitizenIntentRouter.RouteOne("qrh boom");
        Assert.False(r.Ok);
        Assert.Equal("qrh_op_unknown", r.Reason);
    }

    [Fact]
    public void Execute_index_with_override_ok()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.QrhHandleOverride = (_, _) =>
            new { schema = "qrh_organ/v0", ok = true, mode = "index", pulse = "qrh · intake-brief +3" };
        try
        {
            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("qrh")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("qrh", applied[0].Action);
            Assert.Contains("qrh", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.QrhHandleOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_open_passes_id()
    {
        CitizenRouteHost.UnbindLifecycle();
        IReadOnlyDictionary<string, JsonElement>? seen = null;
        CitizenRouteHost.QrhHandleOverride = (_, args) =>
        {
            seen = args;
            return new { schema = "qrh_organ/v0", ok = true, mode = "open", pulse = "qrh · intake-brief" };
        };
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("qrh open id=intake-brief")
            ]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.NotNull(seen);
            Assert.Equal("open", seen!["op"].GetString());
            Assert.Equal("intake-brief", seen["id"].GetString());
        }
        finally
        {
            CitizenRouteHost.QrhHandleOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }
}
