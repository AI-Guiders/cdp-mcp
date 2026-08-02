#nullable enable
using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

public class CitizenCompletionsTests : IDisposable
{
    public CitizenCompletionsTests()
    {
        CitizenWire.Inject = false;
        CitizenCompletions.TestHandler = null;
        CitizenCompletions.TestApiKey = null;
        CitizenCompletions.TestOpenAiApiKey = null;
        CitizenCompletions.TestOpenAiBaseUrl = null;
        CitizenCompletions.ResetHttpForTests();
    }

    public void Dispose()
    {
        CitizenWire.Inject = false;
        CitizenCompletions.TestHandler = null;
        CitizenCompletions.TestApiKey = null;
        CitizenCompletions.TestOpenAiApiKey = null;
        CitizenCompletions.TestOpenAiBaseUrl = null;
        CitizenCompletions.ResetHttpForTests();
        CitizenDialogHistory.ResetForTests();
    }

    [Fact]
    public void Dialog_history_prepends_prior_turns()
    {
        CitizenDialogHistory.ResetForTests();
        CitizenDialogHistory.SetTestMemory(
        [
            new CitizenCompletions.ChatMessage("user", "раз"),
            new CitizenCompletions.ChatMessage("assistant", "два"),
        ]);
        try
        {
            var built = CitizenCompletions.Build(
                "три",
                inject: false,
                mode: CitizenTurnMode.Dialog,
                history: true);
            Assert.Equal(3, built.Messages.Count);
            Assert.Equal("раз", built.Messages[0].Content);
            Assert.Equal("два", built.Messages[1].Content);
            Assert.Equal("три", built.Messages[2].Content);
        }
        finally
        {
            CitizenDialogHistory.ResetForTests();
        }
    }

    [Fact]
    public void Dialog_history_false_skips_priors()
    {
        CitizenDialogHistory.ResetForTests();
        CitizenDialogHistory.SetTestMemory(
        [
            new CitizenCompletions.ChatMessage("user", "old"),
            new CitizenCompletions.ChatMessage("assistant", "reply"),
        ]);
        try
        {
            var built = CitizenCompletions.Build(
                "new",
                inject: false,
                mode: CitizenTurnMode.Dialog,
                history: false);
            Assert.Single(built.Messages);
            Assert.Equal("new", built.Messages[0].Content);
        }
        finally
        {
            CitizenDialogHistory.ResetForTests();
        }
    }

    [Fact]
    public void Build_dialog_uses_prose_persona()
    {
        var built = CitizenCompletions.Build("привет", inject: false, mode: CitizenTurnMode.Dialog);
        Assert.Equal(CitizenTurnMode.Dialog, built.Mode);
        Assert.Contains("dialog peer", built.System, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WIRE OUTPUT CONTRACT", built.System, StringComparison.Ordinal);
        Assert.Contains("Prose is primary", built.System, StringComparison.Ordinal);
        Assert.Contains("Equal standing", built.System, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Света", built.System, StringComparison.Ordinal);
        Assert.Contains("Memory:", built.System, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_dialog_inject_adds_dialog_and_sticky_lines()
    {
        CitizenDialogHistory.ResetForTests();
        CitizenStickyFacts.ResetForTests();
        CitizenDialogHistory.SetTestMemory(
        [
            new CitizenCompletions.ChatMessage("user", "привет"),
            new CitizenCompletions.ChatMessage("assistant", "здесь"),
        ]);
        CitizenStickyFacts.SetTestMemory(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["call_me"] = "агентка"
        });
        try
        {
            var built = CitizenCompletions.Build(
                "как меня звать?",
                boardLines: ["P  plan · x"],
                inject: true,
                mode: CitizenTurnMode.Dialog,
                history: true);
            Assert.NotNull(built.AfferentPulse);
            Assert.Contains("dialog | pairs=1", built.AfferentPulse!, StringComparison.Ordinal);
            Assert.Contains("sticky | call_me=агентка", built.AfferentPulse!, StringComparison.Ordinal);
        }
        finally
        {
            CitizenDialogHistory.ResetForTests();
            CitizenStickyFacts.ResetForTests();
        }
    }

    [Fact]
    public void Build_wire_keeps_hard_contract()
    {
        var built = CitizenCompletions.Build("ping", inject: false, mode: CitizenTurnMode.Wire);
        Assert.Equal(CitizenTurnMode.Wire, built.Mode);
        Assert.Contains("WIRE OUTPUT CONTRACT", built.System, StringComparison.Ordinal);
    }

    [Fact]
    public void Turn_dry_run_dialog_hint()
    {
        var r = CitizenCompletions.Turn("hi", dryRun: true, mode: CitizenTurnMode.Dialog);
        Assert.True(r.Ok);
        Assert.Contains("dialog prose", r.Hint!, StringComparison.Ordinal);
        Assert.Equal(CitizenTurnMode.Dialog, r.Built!.Mode);
    }

    [Fact]
    public void Build_injects_afferent_before_user()
    {
        var built = CitizenCompletions.Build(
            "hello",
            boardLines: ["P  plan · #9 host", "F  editor · 0", "M  shell · shell"],
            inject: true);

        Assert.Contains("citizen of Cognitive Dev Platform", built.System, StringComparison.Ordinal);
        Assert.True(built.Injected);
        Assert.Equal(2, built.Messages.Count);
        Assert.StartsWith("@frame desk v0", built.Messages[0].Content, StringComparison.Ordinal);
        Assert.Equal("hello", built.Messages[1].Content);
        Assert.NotNull(built.AfferentPulse);
    }

    [Fact]
    public void Build_wire_injects_latched_peer_event_with_pulse()
    {
        CitizenPeerAck.ResetForTests();
        CitizenPeerAck.FromExecuted(
        [
            new CitizenRouteHost.Applied(
                Raw: "@intent build",
                Verb: "Build",
                Ok: true,
                Action: "build",
                Pulse: "build ok E×0 W×12")
        ]);

        try
        {
            var built = CitizenCompletions.Build(
                "verify build",
                boardLines: ["P  plan · x"],
                peer: CitizenPeerAck.LastPeer,
                inject: true,
                mode: CitizenTurnMode.Wire);

            Assert.NotNull(built.AfferentPulse);
            Assert.Contains("@event peer v0", built.AfferentPulse!, StringComparison.Ordinal);
            Assert.Contains("pulse | build ok E×0 W×12", built.AfferentPulse!, StringComparison.Ordinal);
            Assert.Contains("WIRE OUTPUT CONTRACT", built.System, StringComparison.Ordinal);
            Assert.Contains("@event peer", built.System, StringComparison.Ordinal);
        }
        finally
        {
            CitizenPeerAck.ResetForTests();
        }
    }

    [Fact]
    public void Build_without_inject_is_single_user()
    {
        var built = CitizenCompletions.Build("ping", inject: false);
        Assert.False(built.Injected);
        Assert.Single(built.Messages);
        Assert.Equal("ping", built.Messages[0].Content);
    }

    [Fact]
    public void Turn_dry_run_skips_provider()
    {
        var r = CitizenCompletions.Turn("hi", dryRun: true);
        Assert.True(r.Ok);
        Assert.True(r.DryRun);
        Assert.Equal("dry_run", r.Provider);
        Assert.Null(r.Text);
        Assert.NotNull(r.Built);
        Assert.True(r.Built!.Injected);
    }

    [Fact]
    public void Turn_dry_run_resolves_openai_model_label()
    {
        CitizenCompletions.TestApiKey = null;
        CitizenCompletions.TestOpenAiApiKey = "sk-test-openai-abcdefghijklmnop";
        CitizenCompletions.TestOpenAiBaseUrl = "https://foundation-models.api.cloud.ru/v1";
        try
        {
            var r = CitizenCompletions.Turn("hi", dryRun: true);
            Assert.True(r.Ok);
            Assert.True(r.DryRun);
            Assert.Equal("dry_run", r.Provider);
            Assert.Equal(CitizenAiKeys.DefaultOpenAiModel, r.Model);
            Assert.NotEqual(CitizenCompletions.DefaultModel, r.Model);
        }
        finally
        {
            CitizenCompletions.TestOpenAiApiKey = null;
            CitizenCompletions.TestOpenAiBaseUrl = null;
        }
    }

    [Fact]
    public void Turn_live_parses_wire_intents_from_mock()
    {
        var payload = """
            {"content":[{"type":"text","text":"@intent go=plan\n@frame desk v0\nboard | P:plan\ncost | A\n"}]}
            """;
        CitizenCompletions.TestApiKey = "sk-ant-test-abcdefghijklmnop";
        CitizenCompletions.TestHandler = new StubHandler(HttpStatusCode.OK, payload);
        CitizenCompletions.ResetHttpForTests();

        var r = CitizenCompletions.Turn("status?", dryRun: false);
        Assert.True(r.Ok);
        Assert.False(r.DryRun);
        Assert.Equal("anthropic", r.Provider);
        Assert.Contains("@intent go=plan", r.Text!);
        Assert.NotNull(r.WireIntents);
        Assert.True(r.WireIntents!.Count >= 1);
        Assert.Equal(CitizenWireParser.Kind.Intent, r.WireIntents[0].Kind);
        Assert.NotNull(r.Routes);
        Assert.Single(r.Routes!);
        Assert.True(r.Routes[0].Ok);
        Assert.Equal("plan", r.Routes[0].Go);
    }

    [Fact]
    public void Turn_openai_compat_parses_choices_message()
    {
        var payload = """
            {"choices":[{"message":{"role":"assistant","content":"@intent go=plan\nok"}}]}
            """;
        CitizenCompletions.TestOpenAiApiKey = "sk-cloud-ru-test-abcdefghijklmnop";
        CitizenCompletions.TestOpenAiBaseUrl = "https://foundation-models.api.cloud.ru/v1";
        CitizenCompletions.TestHandler = new StubHandler(HttpStatusCode.OK, payload);
        CitizenCompletions.ResetHttpForTests();

        var r = CitizenCompletions.Turn("status?", dryRun: false);
        Assert.True(r.Ok);
        Assert.Equal(CitizenCompletions.ProviderOpenAiCompat, r.Provider);
        Assert.Equal(CitizenAiKeys.DefaultOpenAiModel, r.Model);
        Assert.Contains("@intent go=plan", r.Text!);
        Assert.NotNull(r.Routes);
        Assert.Single(r.Routes!);
        Assert.Equal("plan", r.Routes[0].Go);
    }

    [Fact]
    public void ChatCompletionsUrl_normalizes_cloud_ru_base()
    {
        Assert.Equal(
            "https://foundation-models.api.cloud.ru/v1/chat/completions",
            CitizenCompletions.ChatCompletionsUrl("https://foundation-models.api.cloud.ru"));
        Assert.Equal(
            "https://foundation-models.api.cloud.ru/v1/chat/completions",
            CitizenCompletions.ChatCompletionsUrl("https://foundation-models.api.cloud.ru/v1/"));
    }

    [Fact]
    public void Channel_turn_dry_run_json_ok()
    {
        using var doc = JsonDocument.Parse("""
            {"op":"turn","message":"hello","dry_run":true,"board":"P  plan · test"}
            """);
        var args = doc.RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase);
        var json = IdeCitizenChannel.HandleJson(args);
        using var outDoc = JsonDocument.Parse(json);
        Assert.True(outDoc.RootElement.GetProperty("ok").GetBoolean());
        Assert.True(outDoc.RootElement.GetProperty("dry_run").GetBoolean());
        Assert.True(outDoc.RootElement.GetProperty("injected").GetBoolean());
    }

    [Fact]
    public void Channel_scene_exposes_invite_ready_gate()
    {
        var json = IdeCitizenChannel.HandleJson(new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["op"] = JsonSerializer.SerializeToElement("scene")
        });
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.True(doc.RootElement.TryGetProperty("invite_ready", out var invite));
        Assert.True(invite.TryGetProperty("Status", out _) || invite.TryGetProperty("status", out _)
                    || invite.ValueKind == JsonValueKind.Object);
        Assert.Contains("invite=", doc.RootElement.GetProperty("pulse").GetString(), StringComparison.Ordinal);
    }

}

sealed class StubHandler(HttpStatusCode code, string body) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var resp = new HttpResponseMessage(code)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        return Task.FromResult(resp);
    }
}
