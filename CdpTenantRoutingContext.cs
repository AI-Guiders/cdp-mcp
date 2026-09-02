#nullable enable

namespace CdpMcp;

/// <summary>HTTP tenant routing axis for the current invoke (conversation ≠ bridge).</summary>
internal static class CdpTenantRoutingContext
{
    static readonly AsyncLocal<string?> ConversationId = new();

    public static string? CurrentConversationId => ConversationId.Value;

    public static IDisposable Enter(string? conversationId) => new Scope(conversationId);

        sealed class Scope : IDisposable
    {
        readonly string? _prior = ConversationId.Value;

        public Scope(string? conversationId) => ConversationId.Value = conversationId;

        public void Dispose() => ConversationId.Value = _prior;
    }
}
