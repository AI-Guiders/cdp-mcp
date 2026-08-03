#nullable enable
using Xunit;

namespace CdpMcp.Tests;
public sealed partial class CitizenRouteHostTests
{
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
            var args = doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase);
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
        var payload = """{"choices":[{"message":{"role":"assistant","content":"@intent open path=""" + rel + """\nok"}}]}""";
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
            var args = doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase);
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
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            { /* temp */
            }
        }
    }

    [Fact]
    public void Channel_live_mock_provider_drill_is_host_executed()
    {
        IdeDeskSeats.EnsureDefaultsFromSettings();
        IdeDeskSeats.Clear();
        IdeDeskSeats.TryPlaceExplicit("forward", "browser");
        var payload = """{"choices":[{"message":{"role":"assistant","content":"@intent drill editor\nok"}}]}""";
        CitizenCompletions.TestOpenAiApiKey = "sk-cloud-ru-test-abcdefghijklmnop";
        CitizenCompletions.TestOpenAiBaseUrl = "https://foundation-models.api.cloud.ru/v1";
        CitizenCompletions.TestHandler = new StubHandler(System.Net.HttpStatusCode.OK, payload);
        CitizenCompletions.ResetHttpForTests();
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse("""
                {"op":"turn","message":"drill the editor","inject":false}
                """);
            var args = doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase);
            var json = IdeCitizenChannel.HandleJson(args);
            using var outDoc = System.Text.Json.JsonDocument.Parse(json);
            Assert.True(outDoc.RootElement.GetProperty("ok").GetBoolean());
            Assert.True(outDoc.RootElement.GetProperty("execute").GetBoolean());
            Assert.Equal("Drill", outDoc.RootElement.GetProperty("routes")[0].GetProperty("verb").GetString());
            Assert.Equal("editor_scene", outDoc.RootElement.GetProperty("routes")[0].GetProperty("go").GetString());
            var executed = outDoc.RootElement.GetProperty("executed");
            Assert.Equal(System.Text.Json.JsonValueKind.Array, executed.ValueKind);
            Assert.True(executed[0].GetProperty("ok").GetBoolean());
            Assert.Equal("place", executed[0].GetProperty("action").GetString());
            Assert.Equal("editor_scene", executed[0].GetProperty("go").GetString());
            var map = IdeDeskSeats.Snapshot();
            Assert.Contains(map, kv => string.Equals(kv.Value, "editor_scene", StringComparison.OrdinalIgnoreCase));
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
    public void Channel_live_mock_provider_pane_full_is_host_executed()
    {
        IdeDeskSeats.EnsureDefaultsFromSettings();
        IdeDeskSeats.Clear();
        IdeDeskSeats.TryPlaceExplicit("m", "browser");
        var payload = """{"choices":[{"message":{"role":"assistant","content":"@intent pane_full=m\nok"}}]}""";
        CitizenCompletions.TestOpenAiApiKey = "sk-cloud-ru-test-abcdefghijklmnop";
        CitizenCompletions.TestOpenAiBaseUrl = "https://foundation-models.api.cloud.ru/v1";
        CitizenCompletions.TestHandler = new StubHandler(System.Net.HttpStatusCode.OK, payload);
        CitizenCompletions.ResetHttpForTests();
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse("""
                {"op":"turn","message":"need one seat dump","inject":false}
                """);
            var args = doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase);
            var json = IdeCitizenChannel.HandleJson(args);
            using var outDoc = System.Text.Json.JsonDocument.Parse(json);
            Assert.True(outDoc.RootElement.GetProperty("ok").GetBoolean());
            Assert.True(outDoc.RootElement.GetProperty("execute").GetBoolean());
            Assert.Equal("PaneFull", outDoc.RootElement.GetProperty("routes")[0].GetProperty("verb").GetString());
            var executed = outDoc.RootElement.GetProperty("executed");
            Assert.Equal(System.Text.Json.JsonValueKind.Array, executed.ValueKind);
            Assert.True(executed[0].GetProperty("ok").GetBoolean());
            Assert.Equal("pane_full", executed[0].GetProperty("action").GetString());
            Assert.Equal("m", executed[0].GetProperty("seat").GetString());
            Assert.Equal("cockpit", executed[0].GetProperty("go").GetString());
            var map = IdeDeskSeats.Snapshot();
            Assert.Contains(map, kv => string.Equals(kv.Value, "cockpit", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            CitizenCompletions.TestHandler = null;
            CitizenCompletions.TestOpenAiApiKey = null;
            CitizenCompletions.TestOpenAiBaseUrl = null;
            CitizenCompletions.ResetHttpForTests();
        }
    }
}