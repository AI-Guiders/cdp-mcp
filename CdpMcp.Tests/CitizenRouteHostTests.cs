#nullable enable
using Xunit;

namespace CdpMcp.Tests;

public sealed class CitizenRouteHostTests
{
    [Fact]
    public void Execute_go_places_organ_on_seat()
    {
        IdeDeskSeats.EnsureDefaultsFromSettings();
        IdeDeskSeats.Clear();
        IdeDeskSeats.TryPlaceExplicit("p", "plan");
        IdeDeskSeats.TryPlaceExplicit("forward", "editor_scene");
        IdeDeskSeats.TryPlaceExplicit("m", "browser");

        var routes = new[] { CitizenIntentRouter.RouteOne("go=alert") };
        var applied = CitizenRouteHost.Execute(routes);

        Assert.Single(applied);
        Assert.True(applied[0].Ok);
        Assert.Equal("place", applied[0].Action);
        Assert.Equal("alert", applied[0].Go);
        Assert.NotNull(applied[0].Seat);

        var map = IdeDeskSeats.Snapshot();
        Assert.Contains(map, kv => string.Equals(kv.Value, "alert", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Execute_refuse_is_skipped()
    {
        var routes = new[] { CitizenIntentRouter.RouteOne("seats_detail=full") };
        var applied = CitizenRouteHost.Execute(routes);
        Assert.Single(applied);
        Assert.False(applied[0].Ok);
        Assert.Equal("refuse", applied[0].Action);
    }

    [Fact]
    public void Execute_open_path_opens_buffer_and_places_editor()
    {
        var root = Path.Combine(Path.GetTempPath(), "cdp-open-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var rel = "probe.txt";
        var full = Path.Combine(root, rel);
        File.WriteAllText(full, "wave28\n");

        var store = new DocumentBufferStore();
        IdeLanguageTools.BindDocumentStore(store);
        var prevRoot = IdeCockpitHostChannel.ProjectRootResolver;
        IdeDeskSeats.EnsureDefaultsFromSettings();
        IdeDeskSeats.Clear();
        IdeDeskSeats.TryPlaceExplicit("forward", "browser");

        try
        {
            IdeCockpitHostChannel.ProjectRootResolver = () => root;
            var routes = new[] { CitizenIntentRouter.RouteOne("open path=" + rel) };
            var applied = CitizenRouteHost.Execute(routes);

            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("open", applied[0].Action);
            Assert.Equal("editor_scene", applied[0].Go);
            Assert.Equal(Path.GetFullPath(full), applied[0].Path);
            Assert.False(string.IsNullOrWhiteSpace(applied[0].DocId));

            var map = IdeDeskSeats.Snapshot();
            Assert.Contains(map, kv => string.Equals(kv.Value, "editor_scene", StringComparison.OrdinalIgnoreCase));
            Assert.True(store.TryGet(full, out _));
        }
        finally
        {
            IdeCockpitHostChannel.ProjectRootResolver = prevRoot;
            try { Directory.Delete(root, recursive: true); } catch { /* temp */ }
        }
    }

    [Fact]
    public void Channel_dry_run_execute_true_runs_host()
    {
        IdeDeskSeats.EnsureDefaultsFromSettings();
        using var doc = System.Text.Json.JsonDocument.Parse("""
            {"op":"turn","message":"@intent go=plan","dry_run":true,"execute":true,"inject":false}
            """);
        var args = doc.RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase);
        var json = IdeCitizenChannel.HandleJson(args);
        using var outDoc = System.Text.Json.JsonDocument.Parse(json);
        Assert.True(outDoc.RootElement.GetProperty("ok").GetBoolean());
        Assert.True(outDoc.RootElement.GetProperty("execute").GetBoolean());
        Assert.True(outDoc.RootElement.TryGetProperty("executed", out var executed));
        Assert.Equal(System.Text.Json.JsonValueKind.Array, executed.ValueKind);
        Assert.True(executed.GetArrayLength() >= 1);
        Assert.True(executed[0].GetProperty("ok").GetBoolean());
        Assert.Equal("place", executed[0].GetProperty("action").GetString());
    }

    [Fact]
    public void Channel_dry_run_default_skips_execute()
    {
        using var doc = System.Text.Json.JsonDocument.Parse("""
            {"op":"turn","message":"@intent go=plan","dry_run":true,"inject":false}
            """);
        var args = doc.RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase);
        var json = IdeCitizenChannel.HandleJson(args);
        using var outDoc = System.Text.Json.JsonDocument.Parse(json);
        Assert.True(outDoc.RootElement.GetProperty("ok").GetBoolean());
        Assert.False(outDoc.RootElement.GetProperty("execute").GetBoolean());
        Assert.True(outDoc.RootElement.TryGetProperty("executed", out var executed));
        Assert.Equal(System.Text.Json.JsonValueKind.Null, executed.ValueKind);
    }

    [Fact]
    public void Channel_live_mock_provider_routes_are_host_executed()
    {
        IdeDeskSeats.EnsureDefaultsFromSettings();
        IdeDeskSeats.TryPlaceExplicit("m", "browser");

        var payload = """
            {"choices":[{"message":{"role":"assistant","content":"@intent go=alert\nok"}}]}
            """;
        CitizenCompletions.TestOpenAiApiKey = "sk-cloud-ru-test-abcdefghijklmnop";
        CitizenCompletions.TestOpenAiBaseUrl = "https://foundation-models.api.cloud.ru/v1";
        CitizenCompletions.TestHandler = new StubHandler(System.Net.HttpStatusCode.OK, payload);
        CitizenCompletions.ResetHttpForTests();

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse("""
                {"op":"turn","message":"status?","inject":false}
                """);
            var args = doc.RootElement.EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase);
            var json = IdeCitizenChannel.HandleJson(args);
            using var outDoc = System.Text.Json.JsonDocument.Parse(json);
            Assert.True(outDoc.RootElement.GetProperty("ok").GetBoolean());
            Assert.False(outDoc.RootElement.GetProperty("dry_run").GetBoolean());
            Assert.True(outDoc.RootElement.GetProperty("execute").GetBoolean());
            Assert.Equal("alert", outDoc.RootElement.GetProperty("routes")[0].GetProperty("go").GetString());
            var executed = outDoc.RootElement.GetProperty("executed");
            Assert.Equal(System.Text.Json.JsonValueKind.Array, executed.ValueKind);
            Assert.True(executed[0].GetProperty("ok").GetBoolean());
            Assert.Equal("place", executed[0].GetProperty("action").GetString());
            Assert.Equal("alert", executed[0].GetProperty("go").GetString());

            var map = IdeDeskSeats.Snapshot();
            Assert.Contains(map, kv => string.Equals(kv.Value, "alert", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            CitizenCompletions.TestHandler = null;
            CitizenCompletions.TestOpenAiApiKey = null;
            CitizenCompletions.TestOpenAiBaseUrl = null;
            CitizenCompletions.ResetHttpForTests();
        }
    }

    [Fact]
    public void Channel_live_mock_provider_open_path_is_host_executed()
    {
        var root = Path.Combine(Path.GetTempPath(), "cdp-open-live-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var rel = "CitizenRouteHost.cs";
        var full = Path.Combine(root, rel);
        File.WriteAllText(full, "// wave28 open-path\n");

        var store = new DocumentBufferStore();
        IdeLanguageTools.BindDocumentStore(store);
        var prevRoot = IdeCockpitHostChannel.ProjectRootResolver;
        IdeDeskSeats.EnsureDefaultsFromSettings();
        IdeDeskSeats.TryPlaceExplicit("forward", "browser");

        var payload =
            """{"choices":[{"message":{"role":"assistant","content":"@intent open path="""
            + rel
            + """\nok"}}]}""";
        CitizenCompletions.TestOpenAiApiKey = "sk-cloud-ru-test-abcdefghijklmnop";
        CitizenCompletions.TestOpenAiBaseUrl = "https://foundation-models.api.cloud.ru/v1";
        CitizenCompletions.TestHandler = new StubHandler(System.Net.HttpStatusCode.OK, payload);
        CitizenCompletions.ResetHttpForTests();

        try
        {
            IdeCockpitHostChannel.ProjectRootResolver = () => root;
            using var doc = System.Text.Json.JsonDocument.Parse("""
                {"op":"turn","message":"open the route host","inject":false}
                """);
            var args = doc.RootElement.EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase);
            var json = IdeCitizenChannel.HandleJson(args);
            using var outDoc = System.Text.Json.JsonDocument.Parse(json);
            Assert.True(outDoc.RootElement.GetProperty("ok").GetBoolean());
            Assert.True(outDoc.RootElement.GetProperty("execute").GetBoolean());
            Assert.Equal(rel, outDoc.RootElement.GetProperty("routes")[0].GetProperty("path").GetString());

            var executed = outDoc.RootElement.GetProperty("executed");
            Assert.Equal(System.Text.Json.JsonValueKind.Array, executed.ValueKind);
            Assert.True(executed[0].GetProperty("ok").GetBoolean());
            Assert.Equal("open", executed[0].GetProperty("action").GetString());
            Assert.Equal("editor_scene", executed[0].GetProperty("go").GetString());
            Assert.Equal(Path.GetFullPath(full), executed[0].GetProperty("path").GetString());
            Assert.False(string.IsNullOrWhiteSpace(executed[0].GetProperty("doc_id").GetString()));

            var map = IdeDeskSeats.Snapshot();
            Assert.Contains(map, kv => string.Equals(kv.Value, "editor_scene", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            IdeCockpitHostChannel.ProjectRootResolver = prevRoot;
            CitizenCompletions.TestHandler = null;
            CitizenCompletions.TestOpenAiApiKey = null;
            CitizenCompletions.TestOpenAiBaseUrl = null;
            CitizenCompletions.ResetHttpForTests();
            try { Directory.Delete(root, recursive: true); } catch { /* temp */ }
        }
    }
}
