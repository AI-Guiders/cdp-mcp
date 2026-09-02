namespace CdpMcpBridge;

/// <summary>Per CallTool conversation scope (AsyncLocal — parallel chats on one bridge).</summary>
internal static class CdpBridgeConversationContext
{
    static readonly AsyncLocal<string?> Current = new();

    public static string? ConversationId => Current.Value;

    public static IDisposable Bind(string? conversationId) => new Scope(conversationId);

    sealed class Scope : IDisposable
    {
        readonly string? _prior = Current.Value;

        public Scope(string? conversationId) => Current.Value = conversationId;

        public void Dispose() => Current.Value = _prior;
    }
}
