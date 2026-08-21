using System.Net.Http.Headers;

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

    public CdpBridgeTenantHeadersHandler(CdpBridgeTenantHeadersState state, Uri baseUrl, string token)
    {
        _state = state;
        _latchClient = new HttpClient { BaseAddress = baseUrl };
        _latchClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!request.RequestUri?.AbsolutePath.Contains("/tenant/composer", StringComparison.OrdinalIgnoreCase) ?? true)
            await RefreshComposerLatchAsync(cancellationToken).ConfigureAwait(false);

        request.Headers.Remove("X-CDP-Bridge-Session");
        request.Headers.Remove("X-CDP-Workspace-Key");
        request.Headers.Remove("X-CDP-Composer");
        request.Headers.TryAddWithoutValidation("X-CDP-Bridge-Session", _state.BridgeSessionId);
        request.Headers.TryAddWithoutValidation("X-CDP-Workspace-Key", _state.WorkspaceKey);
        request.Headers.TryAddWithoutValidation("X-CDP-Composer", _state.Composer);
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    async Task RefreshComposerLatchAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var probe = new HttpRequestMessage(HttpMethod.Get, "/api/v1/cdp/tenant/composer");
            probe.Headers.TryAddWithoutValidation("X-CDP-Bridge-Session", _state.BridgeSessionId);
            probe.Headers.TryAddWithoutValidation("X-CDP-Workspace-Key", _state.WorkspaceKey);
            probe.Headers.TryAddWithoutValidation("X-CDP-Composer", _state.Composer);
            using var response = await _latchClient.SendAsync(probe, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return;
            var payload = await response.Content.ReadFromJsonAsync<CdpComposerLatchResponse>(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (payload?.Composer is { Length: > 0 } c)
                _state.Composer = c;
        }
        catch
        {
            /* best-effort */
        }
    }

    sealed class CdpComposerLatchResponse
    {
        public string? Composer { get; set; }
    }
}
