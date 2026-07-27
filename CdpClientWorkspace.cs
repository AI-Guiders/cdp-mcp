#nullable enable
using Cdp.Core;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CdpMcp;

/// <summary>
/// Pull MCP client roots (ADR 0199 primary isolation) and keep <see cref="CdpProfile"/> in sync.
/// </summary>
internal static class CdpClientWorkspace
{
    static readonly object Gate = new();
    static bool _wired;
    static DateTimeOffset _lastAttemptUtc = DateTimeOffset.MinValue;
    static DateTimeOffset _lastOkUtc = DateTimeOffset.MinValue;
    static string? _lastError;

    public static string? LastError
    {
        get { lock (Gate) return _lastError; }
    }

    public static void Wire(McpServer server)
    {
        lock (Gate)
        {
            if (_wired) return;
            _wired = true;
        }

        server.RegisterNotificationHandler(
            NotificationMethods.RootsListChangedNotification,
            async (_, ct) =>
            {
                await RefreshAsync(server, ct, force: true).ConfigureAwait(false);
            });

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(400).ConfigureAwait(false);
                await RefreshAsync(server, CancellationToken.None, force: true).ConfigureAwait(false);
            }
            catch
            {
                /* ignore boot race */
            }
        });
    }

    public static Task<bool> RefreshAsync(McpServer? server, CancellationToken ct) =>
        RefreshAsync(server, ct, force: false);

    public static async Task<bool> RefreshAsync(McpServer? server, CancellationToken ct, bool force)
    {
        if (server is null) return false;

        lock (Gate)
        {
            if (!force && _lastOkUtc != DateTimeOffset.MinValue &&
                DateTimeOffset.UtcNow - _lastAttemptUtc < TimeSpan.FromSeconds(20))
                return false;
            _lastAttemptUtc = DateTimeOffset.UtcNow;
        }

        try
        {
            var result = await server.RequestRootsAsync(new ListRootsRequestParams(), ct)
                .ConfigureAwait(false);
            var uris = result.Roots.Select(r => r.Uri).ToArray();
            var changed = CdpProfile.ApplyClientRoots(uris);
            lock (Gate)
            {
                _lastError = null;
                _lastOkUtc = DateTimeOffset.UtcNow;
            }
            if (changed)
                Console.Error.WriteLine(
                    $"CdpMcp isolation=client_roots state_root={CdpProfile.StateRoot} roots={uris.Length}");
            return changed;
        }
        catch (Exception ex)
        {
            lock (Gate) _lastError = ex.Message;
            return false;
        }
    }

    /// <summary>Cheap sync hook before tools that touch WitDB / settings.</summary>
    public static void EnsureSessionFallback(SessionContext session)
    {
        var root = session.ScmRoot ?? session.ProjectRoot;
        CdpProfile.ApplySessionWorkspace(root);
    }

    public static object StatusCard() => new
    {
        kind = CdpProfile.Kind,
        state_root = CdpProfile.StateRoot,
        env_profile = CdpProfile.Name,
        workspace = CdpProfile.WorkspaceLabel,
        client_roots = CdpProfile.ClientRoots,
        adr = "0199",
        last_roots_error = LastError,
        last_attempt_utc = _lastAttemptUtc == DateTimeOffset.MinValue ? null : (DateTimeOffset?)_lastAttemptUtc
    };
}
