#nullable enable
using System.Text;
using System.Text.Json;

namespace CdpMcp;

/// <summary>
/// In-habitat completions host (ADR-0028 / peel #9).
/// Providers: Anthropic Messages <b>or</b> OpenAI-compat chat.completions
/// (Cloud.ru FM / OpenAI / DeepSeek) via <see cref="CitizenAiKeys"/>;
/// wire inject via <see cref="CitizenWire"/>.
/// Pattern aligned with CIDE <c>OpenAiCompatibleProvider</c> (non-stream turn).
/// </summary>
internal static partial class CitizenCompletions
{
    public const string DefaultModel = "claude-sonnet-4-20250514";
    public const string AnthropicVersion = "2023-06-01";
    public const string MessagesUrl = "https://api.anthropic.com/v1/messages";
    public const string ProviderAnthropic = "anthropic";
    public const string ProviderOpenAiCompat = "openai_compat";

    static readonly object HttpGate = new();
    static HttpClient? SharedHttp;
    static HttpMessageHandler? BoundHandler;

    /// <summary>Tests: inject handler before Turn; clear in finally.</summary>
    internal static HttpMessageHandler? TestHandler;

    /// <summary>Tests: force Anthropic key without touching disk.</summary>
    internal static string? TestApiKey;

    /// <summary>Tests: force OpenAI-compat key without touching disk.</summary>
    internal static string? TestOpenAiApiKey;

    /// <summary>Tests: override OpenAI-compat base URL.</summary>
    internal static string? TestOpenAiBaseUrl;

    public sealed record ChatMessage(string Role, string Content);

    public sealed record BuiltTurn(
        string System,
        IReadOnlyList<ChatMessage> Messages,
        string? AfferentPulse,
        bool Injected,
        CitizenTurnMode Mode = CitizenTurnMode.Wire);

    public sealed record TurnResult(
        bool Ok,
        string? Error,
        string? Hint,
        string? Text,
        string? Model,
        string? Provider,
        BuiltTurn? Built,
        IReadOnlyList<CitizenWireParser.Message>? WireIntents,
        IReadOnlyList<CitizenIntentRouter.Route>? Routes,
        bool DryRun);

    internal static void ResetHttpForTests()
    {
        lock (HttpGate)
        {
            SharedHttp?.Dispose();
            SharedHttp = null;
            BoundHandler = null;
        }
    }

    static HttpClient Http
    {
        get
        {
            lock (HttpGate)
            {
                if (SharedHttp is null || !ReferenceEquals(BoundHandler, TestHandler))
                {
                    SharedHttp?.Dispose();
                    HttpMessageHandler handler = TestHandler ?? new HttpClientHandler();
                    BoundHandler = TestHandler;
                    SharedHttp = new HttpClient(handler, disposeHandler: TestHandler is null)
                    {
                        Timeout = TimeSpan.FromSeconds(120)
                    };
                }

                return SharedHttp;
            }
        }
    }

    public static BuiltTurn Build(
        string userText,
        IEnumerable<string>? boardLines = null,
        string? sa = null,
        string? peer = null,
        string? next = null,
        string? tm = null,
        bool inject = true,
        CitizenTurnMode mode = CitizenTurnMode.Wire,
        bool history = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userText);
        string? afferent = null;
        var injected = false;
        var msgs = new List<ChatMessage>();

        // Dialog multi-turn: prior user/assistant pairs before this turn's afferent+user.
        if (mode == CitizenTurnMode.Dialog && history)
        {
            foreach (var prior in CitizenDialogHistory.Load())
                msgs.Add(prior);
        }

        if (inject)
        {
            var prev = CitizenWire.Inject;
            try
            {
                CitizenWire.Inject = true;
                afferent = CitizenWire.PackFromDeskBoard(
                    boardLines,
                    sa: sa ?? "clear · explore/code",
                    peer: peer ?? "ok · gen=1 · mcp=live · compact=no",
                    next: next,
                    tm: tm);
                var presence = CideIntercomPresenceLatch.AfferentLine();
                if (presence is not null)
                    afferent = AppendAfferentLine(afferent, presence);

                if (mode == CitizenTurnMode.Dialog && history)
                {
                    afferent = AppendAfferentLine(afferent, CitizenDialogHistory.AfferentLine());
                    var sticky = CitizenStickyFacts.AfferentLine();
                    if (sticky is not null)
                        afferent = AppendAfferentLine(afferent, sticky);
                }
                // Afferent as its own user message, then bare user text (keeps history clean).
                if (!string.IsNullOrWhiteSpace(afferent))
                {
                    msgs.Add(new ChatMessage("user", afferent));
                    injected = true;
                }

                msgs.Add(new ChatMessage("user", userText.Trim()));
            }
            finally
            {
                CitizenWire.Inject = prev;
            }
        }
        else
        {
            msgs.Add(new ChatMessage("user", userText.Trim()));
        }

        return new BuiltTurn(CitizenPersona.ForMode(mode), msgs, afferent, injected, mode);
    }

    static string AppendAfferentLine(string? afferent, string line)
    {
        if (string.IsNullOrWhiteSpace(afferent))
            return line.TrimEnd() + "\n";
        var body = afferent.TrimEnd();
        return body + "\n" + line.TrimEnd() + "\n";
    }

    public static TurnResult Turn(
        string userText,
        IEnumerable<string>? boardLines = null,
        string? sa = null,
        string? peer = null,
        string? next = null,
        string? tm = null,
        string? model = null,
        bool dryRun = false,
        bool inject = true,
        CitizenTurnMode mode = CitizenTurnMode.Wire,
        bool history = true,
        int maxTokens = 1024,
        CancellationToken cancellationToken = default)
    {
        var built = Build(userText, boardLines, sa, peer, next, tm, inject, mode, history);
        if (dryRun)
        {
            var dryModel = ResolveDryRunModel(model);
            var modeHint = mode == CitizenTurnMode.Dialog ? "dialog prose" : "wire hands";
            var histN = mode == CitizenTurnMode.Dialog && history ? CitizenDialogHistory.Load().Count : 0;
            return new TurnResult(
                Ok: true,
                Error: null,
                Hint: "dry_run — no provider call; messages built with persona + wire inject · mode=" + modeHint
                    + (histN > 0 ? " · history=" + histN : ""),
                Text: null,
                Model: dryModel,
                Provider: "dry_run",
                Built: built,
                WireIntents: null,
                Routes: null,
                DryRun: true);
        }

        var keys = CitizenAiKeys.Load();
        var resolved = ResolveProvider(keys, model);
        if (resolved is null)
        {
            return new TurnResult(
                false,
                "keys_missing",
                "set open_ai_api_key (Cloud.ru FM) or anthropic_api_key in %LocalAppData%\\CascadeIDE\\ai-keys.toml (CDP-ADR-0026)",
                null,
                model ?? CitizenAiKeys.DefaultOpenAiModel,
                null,
                built,
                null,
                null,
                false);
        }

        try
        {
            var result = resolved.Provider == ProviderOpenAiCompat
                ? TurnOpenAiCompat(built, resolved, maxTokens, cancellationToken)
                : TurnAnthropic(built, resolved, maxTokens, cancellationToken);
            if (result.Ok && mode == CitizenTurnMode.Dialog && history && !string.IsNullOrWhiteSpace(result.Text))
                CitizenDialogHistory.Append(userText, result.Text!);
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new TurnResult(
                false,
                "turn_failed",
                Trunc(ex.Message, 240),
                null,
                resolved.Model,
                resolved.Provider,
                built,
                null,
                null,
                false);
        }
    }

    sealed record Resolved(
        string Provider,
        string ApiKey,
        string Model,
        string? BaseUrl);

}
