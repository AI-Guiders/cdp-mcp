#nullable enable

namespace CdpMcp;

/// <summary>Provider registry for AutoIgnition fire channels (2 seats: Cursor / OpenCode).</summary>
internal static class IdeAutoiFireProvider
{
    public static IAutoiFireProvider Resolve() =>
        IdeAutoiFireProviderOpencode.Instance.IsActive() ? IdeAutoiFireProviderOpencode.Instance : IdeAutoiFireProviderCursor.Instance;
}

/// <summary>Cursor seat — SDK local primary; CDT Composer (:9222) is escape. Default provider.</summary>
internal sealed class IdeAutoiFireProviderCursor : IAutoiFireProvider
{
    public static readonly IdeAutoiFireProviderCursor Instance = new();
    IdeAutoiFireProviderCursor() { }

    public string Channel => "cursor";

    public bool IsActive() => true;

    public async Task<object> FireAsync(string message, int waitSeconds, CancellationToken ct)
    {
        var arm = new IdeIgniteArmHost.IgniteArm
        {
            Id = "autoi-cursor-" + Guid.NewGuid().ToString("N")[..8],
            Message = message,
            Harness = "cursor",
            WaitSeconds = waitSeconds
        };
        var sdk = await IdeIgniteSdkLocalFire.TryDeliverAsync(arm, message, ct).ConfigureAwait(false);
        if (sdk is not null)
            return sdk;
        return await IdeIgniteChannel.FireAsync(
            IdeIgniteChannel.DefaultPort, message, chat: null, waitSeconds, ct).ConfigureAwait(false);
    }
}

/// <summary>OpenCode seat — native `opencode run -s &lt;session&gt;` wake. Config-gated.</summary>
internal sealed class IdeAutoiFireProviderOpencode : IAutoiFireProvider
{
    public static readonly IdeAutoiFireProviderOpencode Instance = new();
    IdeAutoiFireProviderOpencode() { }

    public string Channel => "opencode";

    public bool IsActive() => IdeIgniteChannel.IsOpencodeConfigured();

    public Task<object> FireAsync(string message, int waitSeconds, CancellationToken ct) =>
        IdeIgniteChannel.FireToOpencodeAsync(message, ct);
}
