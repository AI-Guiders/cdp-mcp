#nullable enable

namespace CdpMcp;

/// <summary>Provider registry for AutoIgnition fire channels (2 seats: Cursor / OpenCode).</summary>
internal static class IdeAutoiFireProvider
{
    public static IAutoiFireProvider Resolve() =>
        IdeAutoiFireProviderOpencode.Instance.IsActive() ? IdeAutoiFireProviderOpencode.Instance : IdeAutoiFireProviderCursor.Instance;
}

/// <summary>Cursor seat — inject into Cursor Composer via CDT (:9222). Default provider.</summary>
internal sealed class IdeAutoiFireProviderCursor : IAutoiFireProvider
{
    public static readonly IdeAutoiFireProviderCursor Instance = new();
    IdeAutoiFireProviderCursor() { }

    public string Channel => "cursor";

    public bool IsActive() => true;

    public Task<object> FireAsync(string message, int waitSeconds, CancellationToken ct) =>
        IdeIgniteChannel.FireAsync(IdeIgniteChannel.DefaultPort, message, chat: null, waitSeconds, ct);
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
