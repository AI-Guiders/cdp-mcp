using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CdpMcpBridge;

internal static class CdpBridgeRootsSync
{
    internal static async Task RunAsync(
        McpServer server,
        CdpBridgeTenantHeadersState state,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(400, cancellationToken).ConfigureAwait(false);
            await RefreshAsync(server, state, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            /* boot race */
        }

        server.RegisterNotificationHandler(
            NotificationMethods.RootsListChangedNotification,
            async (_, ct) => await RefreshAsync(server, state, ct).ConfigureAwait(false));
    }

    static async Task RefreshAsync(
        McpServer server,
        CdpBridgeTenantHeadersState state,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await server.RequestRootsAsync(new ListRootsRequestParams(), cancellationToken)
                .ConfigureAwait(false);
            var uris = result.Roots.Select(r => r.Uri).ToArray();
            state.WorkspaceKey = CdpBridgeWorkspaceKey.FromRoots(uris);
            Console.Error.WriteLine(
                $"CdpMcpBridge workspace_key={state.WorkspaceKey} roots={uris.Length}");
        }
        catch
        {
            /* ignore */
        }
    }
}
