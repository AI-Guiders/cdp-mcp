#nullable enable
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=ignite_desk</c> / Meta <c>cdp_ignite</c> — AutoIgnition via Chrome DevTools (CDT)
/// into Cursor Composer (TipTap). Not Cognitive CDP; not UIA. Dogfood 2026-07-26.
/// Button states: Voice (empty) → Send (has text) → Stop (streaming) / Queue.
/// Partials: Api (handle/probe/send), Js (CDT scripts), Models (DTO), Cdt (session), Fire (CDT inject).
/// </summary>
internal static partial class IdeIgniteChannel
{
    public const string Schema = "ignite/v0";
    public const string ToolName = "cdp_ignite";
    public const string GoName = "ignite_desk";
    public const int DefaultPort = 9222;

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
    static readonly JsonSerializerOptions Compact = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    internal const string ProviderBlockedError = "provider_blocked";

    internal static bool IsProviderBlockedError(string? error) =>
        string.Equals(error, ProviderBlockedError, StringComparison.Ordinal);

            internal static string AriaKind(string? aria)
    {
        var a = (aria ?? "").Trim().ToLowerInvariant();
        if (a.Contains("stop")) return "stop";
        if (a.Contains("queue")) return "queue";
        if (a.Contains("send")) return "send";
        if (a.Contains("voice") || a.Contains("microphone") || a.Contains("mic")) return "voice";
        if (a.Length == 0) return "empty";
        return "other";
    }


    /// <summary>HILD / watches — soft CDT sample without throwing.</summary>
    internal static async Task<(bool Ok, string Kind, string? Text)> TrySampleComposerAsync(
        int port, CancellationToken ct)
    {
        try
        {
            await using var session = await CdtSession.ConnectPageAsync(port, ct).ConfigureAwait(false);
            var state = await session.EvalStateAsync(ct).ConfigureAwait(false);
            if (!state.ComposerScoped)
                return (false, "no_composer", state.InputText);
            return (true, AriaKind(state.SubmitAria), state.InputText);
        }
        catch
        {
            return (false, "down", null);
        }
    }


    static async Task<JsonElement> GetJsonAsync(int port, string path, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var origin = $"http://127.0.0.1:{port}";
        http.DefaultRequestHeaders.TryAddWithoutValidation("Origin", origin);
        using var resp = await http.GetAsync(origin + path, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        return doc.RootElement.Clone();
    }

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key) =>
        args.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    static int? OptInt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el)) return null;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n)) return n;
        if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out n)) return n;
        return null;
    }
}
