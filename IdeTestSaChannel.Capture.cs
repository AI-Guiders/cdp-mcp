#nullable enable
using System.Text.Json;
using Cdp.Core;
using DotNetBuildTest.Core;

namespace CdpMcp;

internal static partial class IdeTestSaChannel
{
    static Snap Capture(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!IdeSessionLifecycle.TryResolveTarget(session, args, out var target, out var err))
            return new Snap(false, err, null, null);

        var last = TestRunCache.TryGet(target);
        return new Snap(true, null, target, last);
    }
}
