#nullable enable

namespace CdpMcp;

/// <summary>
/// Cursor "Connection Problems" / Retry overlay — silent transport flake over Composer.
/// AutoI clicks Retry so the turn can continue (not a playbook; harness organ).
/// </summary>
internal static partial class IdeIgniteChannel
{
    /// <summary>On an open CDT session — click Retry if the connection overlay is visible.</summary>
    static async Task<bool> TryDismissConnectionProblemsAsync(
        CdtSession session,
        CancellationToken ct)
    {
        var hit = await session.EvalAsync<ConnectionRetryResult>(ClickConnectionRetryJs, ct)
            .ConfigureAwait(false);
        return hit is { Clicked: true };
    }

    /// <summary>
    /// Short-lived CDT connect for post-fire watch. Does not require ComposerScoped —
    /// the overlay may cover the prompt while "Planning next moves".
    /// </summary>
    public static async Task<bool> TryDismissConnectionProblemsOnPortAsync(
        int port,
        CancellationToken ct)
    {
        CdtSession? session = null;
        try
        {
            session = await CdtSession.ConnectTopPageAsync(port, ct).ConfigureAwait(false);
            return await TryDismissConnectionProblemsAsync(session, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (session is not null)
            {
                try { await session.DisposeAsync().ConfigureAwait(false); }
                catch { /* ignore */ }
            }
        }
    }
}
