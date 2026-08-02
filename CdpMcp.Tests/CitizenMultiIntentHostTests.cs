#nullable enable
using Xunit;

namespace CdpMcp.Tests;

/// <summary>Wave31: multi-intent host-execute + live_desk bind (omit board=).</summary>
public sealed class CitizenMultiIntentHostTests
{
    [Fact]
    public void Execute_multi_routes_places_each()
    {
        IdeDeskSeats.EnsureDefaultsFromSettings();
        IdeDeskSeats.Clear();
        IdeDeskSeats.TryPlaceExplicit("p", "plan");
        IdeDeskSeats.TryPlaceExplicit("forward", "editor_scene");
        IdeDeskSeats.TryPlaceExplicit("m", "browser");

        var routes = new[]
        {
            CitizenIntentRouter.RouteOne("go=plan"),
            CitizenIntentRouter.RouteOne("go=health")
        };
        var applied = CitizenRouteHost.Execute(routes);

        Assert.Equal(2, applied.Count);
        Assert.All(applied, a => Assert.True(a.Ok));
        Assert.Equal("plan", applied[0].Go);
        Assert.Equal("health", applied[1].Go);

        var map = IdeDeskSeats.Snapshot();
        Assert.Contains(map, kv => string.Equals(kv.Value, "health", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Channel_live_mock_provider_multi_intent_host_executed()
    {
        IdeDeskSeats.EnsureDefaultsFromSettings();
        IdeDeskSeats.Clear();
        IdeDeskSeats.TryPlaceExplicit("p", "plan");
        IdeDeskSeats.TryPlaceExplicit("forward", "editor_scene");
        IdeDeskSeats.TryPlaceExplicit("m", "browser");

        // Two wire lines — host must execute both (not only the first).
        var payload =
            """{"choices":[{"message":{"role":"assistant","content":"@intent go=plan\n@intent go=health\nok"}}]}""";
        CitizenCompletions.TestOpenAiApiKey = "sk-cloud-ru-test-abcdefghijklmnop";
        CitizenCompletions.TestOpenAiBaseUrl = "https://foundation-models.api.cloud.ru/v1";
        CitizenCompletions.TestHandler = new StubHandler(System.Net.HttpStatusCode.OK, payload);
        CitizenCompletions.ResetHttpForTests();

        try
        {
            // Omit board= → live_desk auto-bind; unforced user text (no ONLY @intent).
            using var doc = System.Text.Json.JsonDocument.Parse("""
                {"op":"turn","message":"desk needs plan and health — act"}
                """);
            var args = doc.RootElement.EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase);
            var json = IdeCitizenChannel.HandleJson(args);
            using var outDoc = System.Text.Json.JsonDocument.Parse(json);
            Assert.True(outDoc.RootElement.GetProperty("ok").GetBoolean());
            Assert.True(outDoc.RootElement.GetProperty("execute").GetBoolean());
            Assert.True(outDoc.RootElement.GetProperty("live_desk").GetBoolean());

            var routes = outDoc.RootElement.GetProperty("routes");
            Assert.Equal(2, routes.GetArrayLength());
            Assert.Equal("plan", routes[0].GetProperty("go").GetString());
            Assert.Equal("health", routes[1].GetProperty("go").GetString());

            var executed = outDoc.RootElement.GetProperty("executed");
            Assert.Equal(System.Text.Json.JsonValueKind.Array, executed.ValueKind);
            Assert.Equal(2, executed.GetArrayLength());
            Assert.True(executed[0].GetProperty("ok").GetBoolean());
            Assert.True(executed[1].GetProperty("ok").GetBoolean());
            Assert.Equal("plan", executed[0].GetProperty("go").GetString());
            Assert.Equal("health", executed[1].GetProperty("go").GetString());

            var map = IdeDeskSeats.Snapshot();
            Assert.Contains(map, kv => string.Equals(kv.Value, "health", StringComparison.OrdinalIgnoreCase));
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
