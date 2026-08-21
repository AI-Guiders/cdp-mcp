using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace CdpMcpBridge;

internal sealed class CdpBridgeTenantHeadersState
{
    public required string BridgeSessionId { get; init; }
    public string WorkspaceKey { get; set; } = "default";
    public string Composer { get; set; } = "main";
}

internal sealed class CdpBridgeTenantHeadersHandler : DelegatingHandler
{
    readonly CdpBridgeTenantHeadersState _state;
    readonly HttpClient _latchClient;
    readonly string? _tokenPath;
    string _token;

    public CdpBridgeTenantHeadersHandler(
        CdpBridgeTenantHeadersState state,
        Uri baseUrl,
        string token,
        string? tokenPath = null)
    {
        _state = state;
        _token = token;
        _tokenPath = tokenPath;
        _latchClient = new HttpClient { BaseAddress = baseUrl };
        ApplyLatchAuth();
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var conversationId = CdpBridgeConversationContext.ConversationId;
        var composer = _state.Composer;

        if (!request.RequestUri?.AbsolutePath.Contains("/tenant/composer", StringComparison.OrdinalIgnoreCase) ?? true)
            composer = await RefreshComposerLatchAsync(conversationId, cancellationToken).ConfigureAwait(false)
                       ?? composer;

        ApplyTenantHeaders(request, composer, conversationId);
        ApplyRequestAuth(request);

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != System.Net.HttpStatusCode.Unauthorized || !TryReloadToken())
            return response;

        response.Dispose();
        ApplyRequestAuth(request);
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    void ApplyRequestAuth(HttpRequestMessage request) =>
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);

    void ApplyLatchAuth() =>
        _latchClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);

    void ApplyTenantHeaders(HttpRequestMessage request, string composer, string? conversationId)
    {
        request.Headers.Remove("X-CDP-Bridge-Session");
        request.Headers.Remove("X-CDP-Workspace-Key");
        request.Headers.Remove("X-CDP-Composer");
        request.Headers.Remove("X-CDP-Conversation-Id");
        request.Headers.TryAddWithoutValidation("X-CDP-Bridge-Session", _state.BridgeSessionId);
        request.Headers.TryAddWithoutValidation("X-CDP-Workspace-Key", _state.WorkspaceKey);
        request.Headers.TryAddWithoutValidation("X-CDP-Composer", composer);
        if (!string.IsNullOrWhiteSpace(conversationId))
            request.Headers.TryAddWithoutValidation("X-CDP-Conversation-Id", conversationId);
    }

    bool TryReloadToken()
    {
        if (string.IsNullOrWhiteSpace(_tokenPath) || !File.Exists(_tokenPath))
            return false;

        var fresh = File.ReadAllText(_tokenPath).Trim();
        if (fresh.Length < 16 || string.Equals(fresh, _token, StringComparison.Ordinal))
            return false;

        _token = fresh;
        ApplyLatchAuth();
        return true;
    }

    async Task<string?> RefreshComposerLatchAsync(string? conversationId, CancellationToken cancellationToken)
    {
        try
        {
            using var probe = new HttpRequestMessage(HttpMethod.Get, "/api/v1/cdp/tenant/composer");
            probe.Headers.TryAddWithoutValidation("X-CDP-Bridge-Session", _state.BridgeSessionId);
            probe.Headers.TryAddWithoutValidation("X-CDP-Workspace-Key", _state.WorkspaceKey);
            probe.Headers.TryAddWithoutValidation("X-CDP-Composer", _state.Composer);
            if (!string.IsNullOrWhiteSpace(conversationId))
                probe.Headers.TryAddWithoutValidation("X-CDP-Conversation-Id", conversationId);

            using var response = await _latchClient.SendAsync(probe, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            var payload = await response.Content
                .ReadFromJsonAsync<CdpComposerLatchResponse>(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (payload?.Composer is { Length: > 0 } c)
                return c;
        }
        catch
        {
            /* best-effort */
        }

        return null;
    }

    sealed class CdpComposerLatchResponse
    {
        public string? Composer { get; set; }
    }
}
