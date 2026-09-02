#nullable enable

using System.Collections.Concurrent;



namespace CdpMcp;



/// <summary>Per-bridge (+ conversation) composer identity for same-window multi-chat (ADR-0200).</summary>

internal static class CdpTenantComposerLatch

{

    readonly record struct ComposerLatch(string Composer, string? ChatTitle);



    static readonly ConcurrentDictionary<string, ComposerLatch> ByKey = new(StringComparer.Ordinal);



    public static string LatchKey(string bridgeSession, string? conversationId = null)

    {

        if (string.IsNullOrWhiteSpace(conversationId))

            return bridgeSession;

        return bridgeSession + ":" + conversationId.Trim();

    }



    public static string Get(string bridgeSession, string? conversationId = null) =>

        ByKey.TryGetValue(LatchKey(bridgeSession, conversationId), out var latch) ? latch.Composer : "main";



    /// <summary>CDT chat= default — human title (spaces preserved), not sanitized tenant segment.</summary>

    public static string? ResolveDefaultChat(string? bridgeSession, string? conversationId = null)

    {

        if (string.IsNullOrWhiteSpace(bridgeSession))

            return null;

        if (!ByKey.TryGetValue(LatchKey(bridgeSession, conversationId), out var latch))

            return null;

        return latch.ChatTitle;

    }



    public static bool TrySet(string? bridgeSession, string? composer, string? conversationId = null)

    {

        if (string.IsNullOrWhiteSpace(bridgeSession)

            || bridgeSession.Equals(CdpTenantKey.LegacyDefault.BridgeSession, StringComparison.Ordinal))

            return false;



        var raw = string.IsNullOrWhiteSpace(composer) ? "main" : composer.Trim();

        var key = CdpTenantKey.Normalize(bridgeSession, "default", raw);

        var chatTitle = key.Composer.Equals("main", StringComparison.OrdinalIgnoreCase) ? null : raw;

        ByKey[LatchKey(bridgeSession, conversationId)] = new ComposerLatch(key.Composer, chatTitle);

        return true;

    }



    public static object Snapshot(string bridgeSession, string? conversationId = null)

    {

        if (!ByKey.TryGetValue(LatchKey(bridgeSession, conversationId), out var latch))

            return new

            {

                bridge = bridgeSession,

                conversation = conversationId,

                composer = "main",

                chat = (string?)null,

                adr = "0200"

            };

        return new

        {

            bridge = bridgeSession,

            conversation = conversationId,

            composer = latch.Composer,

            chat = latch.ChatTitle,

            adr = "0200"

        };

    }

}

