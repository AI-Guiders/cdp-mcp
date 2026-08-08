#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenTakeHostTests
{
    [Fact]
    public void Route_take_bare()
    {
        var r = CitizenIntentRouter.RouteOne("take");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Take, r.Verb);
        Assert.Equal("take", r.Op);
    }

    [Fact]
    public void Route_take_path_and_anchor()
    {
        var r = CitizenIntentRouter.RouteOne("take path=a.cs anchor=[F:a.cs;M:Foo]");
        Assert.True(r.Ok);
        Assert.Equal("a.cs", r.Path);
        Assert.Equal("[F:a.cs;M:Foo]", r.Detail);
    }

    [Fact]
    public void Execute_take_passes_args_via_override()
    {
        CitizenRouteHost.UnbindLifecycle();
        IReadOnlyDictionary<string, JsonElement>? seen = null;
        CitizenRouteHost.TakeCallOverride = args =>
        {
            seen = args;
            return """{"schema":"editor_comfort/v0","ok":true,"op":"take","chars":12,"lines":2,"verify":{"status":"ok","error_count":0},"meta":{"path":"D:\\tmp\\a.cs","doc_id":"doc-1"}}""";
        };
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("take path=a.cs check=false")
            ]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("take", applied[0].Action);
            Assert.NotNull(seen);
            Assert.Equal("take", seen!["op"].GetString());
            Assert.Equal("a.cs", seen["path"].GetString());
            Assert.Equal("false", seen["check"].GetString());
            Assert.Contains("chars=12", applied[0].Pulse, StringComparison.Ordinal);
            Assert.Contains("ok", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.TakeCallOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_take_surfaces_verify_failed()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.TakeCallOverride = _ =>
            """{"schema":"editor_comfort/v0","ok":false,"op":"take","error":"verify_failed","verify":{"status":"failed","error_count":1}}""";
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("take path=a.cs")
            ]);
            Assert.Single(applied);
            Assert.False(applied[0].Ok);
            Assert.Contains("verify_failed", applied[0].Reason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CitizenRouteHost.TakeCallOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_take_ships_chat_markdown_into_Applied_Ship()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.TakeCallOverride = _ =>
            """{"schema":"editor_comfort/v0","ok":true,"op":"take","chars":11,"lines":1,"verify":{"status":"skipped","note":"no_kind_checker"},"chat_markdown":"```md\nhello ship\n```","body":"hello ship","meta":{"path":"D:\\tmp\\n.md","doc_id":"doc-9"}}""";
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("take path=n.md")
            ]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Contains("verify=n/a", applied[0].Pulse, StringComparison.Ordinal);
            Assert.DoesNotContain("skipped", applied[0].Pulse, StringComparison.Ordinal);
            Assert.Contains("ship=", applied[0].Pulse, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(applied[0].Ship));
            Assert.Contains("hello ship", applied[0].Ship!, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.TakeCallOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }
}
