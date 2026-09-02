#nullable enable

using Microsoft.AspNetCore.Http;



namespace CdpMcp;



internal static class CdpTenantHeaders

{

    public const string BridgeSession = "X-CDP-Bridge-Session";

    public const string WorkspaceKey = "X-CDP-Workspace-Key";

    public const string Composer = "X-CDP-Composer";

    public const string ConversationId = "X-CDP-Conversation-Id";



    public static string? ReadConversationId(IHeaderDictionary? headers)

    {

        if (headers is null || !headers.TryGetValue(ConversationId, out var vals) || vals.Count == 0)

            return null;

        var raw = vals[0]?.Trim();

        return string.IsNullOrEmpty(raw) ? null : raw;

    }



    public static CdpTenantKey? TryParse(IHeaderDictionary? headers)

    {

        if (headers is null)

            return null;



        if (!headers.TryGetValue(BridgeSession, out var bridgeVals) || bridgeVals.Count == 0)

            return null;



        headers.TryGetValue(WorkspaceKey, out var wsVals);

        headers.TryGetValue(Composer, out var composerVals);

        var bridge = bridgeVals[0]!;

        var conversationId = ReadConversationId(headers);

        var composerHeader = composerVals.Count > 0 ? composerVals[0] : null;

        var composer = ResolveComposerSegment(bridge, conversationId, composerHeader);

        return CdpTenantKey.Normalize(bridge, wsVals.Count > 0 ? wsVals[0] : null, composer);

    }



    internal static string ResolveComposerSegment(string bridge, string? conversationId, string? composerHeader)

    {

        var header = string.IsNullOrWhiteSpace(composerHeader) ? "main" : composerHeader.Trim();

        if (!header.Equals("main", StringComparison.OrdinalIgnoreCase))

            return header;



        var latched = CdpTenantComposerLatch.Get(bridge, conversationId);

        return string.IsNullOrWhiteSpace(latched) ? "main" : latched;

    }



    public static bool IsPresent(IHeaderDictionary? headers) =>

        headers is not null && headers.ContainsKey(BridgeSession);

}

