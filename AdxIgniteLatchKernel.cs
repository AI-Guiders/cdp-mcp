#nullable enable

namespace CdpMcp;

/// <summary>
/// Ignite continuity latch invariants (ADX assertion ADX-IG-001).
/// halt stop-world · last_once → awaiting when autonomous off;
/// under autonomous last_once fire must not invent-ban (0.5.528+). Proven via Z3.
/// </summary>
internal static class AdxIgniteLatchKernel
{
    /// <summary>op=halt world: autonomy+HILD off, await partner on.</summary>
    public static bool HaltWorldOk(bool autonomous, bool hild, bool awaitPartner) =>
        !autonomous && !hild && awaitPartner;

    /// <summary>
    /// last_once fired: awaiting when autonomous off; under autonomous must NOT await (ACC).
    /// </summary>
    public static bool LastOnceFireAwaitingOk(
        bool lastOnce, bool fired, bool awaiting, bool autonomous = false) =>
        !(lastOnce && fired) || (autonomous ? !awaiting : awaiting);

    /// <summary>Armed work and await-partner latch must not both be "flying".</summary>
    public static bool NotArmedWhileAwaiting(bool hasArmedTimer, bool awaitPartner) =>
        !(hasArmedTimer && awaitPartner);

    public static object CheckHalt(bool autonomous, bool hild, bool awaitPartner)
    {
        var ok = HaltWorldOk(autonomous, hild, awaitPartner);
        return new
        {
            id = "ADX-IG-001.halt",
            ok,
            autonomous,
            hild,
            await_partner = awaitPartner,
            pulse = ok ? "ignite_halt ok" : "ignite_halt FAIL"
        };
    }

    public static object CheckLastOnce(
        bool lastOnce, bool fired, bool awaiting, bool autonomous = false)
    {
        var ok = LastOnceFireAwaitingOk(lastOnce, fired, awaiting, autonomous);
        return new
        {
            id = "ADX-IG-001.last_once",
            ok,
            last_once = lastOnce,
            fired,
            awaiting,
            autonomous,
            pulse = ok ? "ignite_last_once ok" : "ignite_last_once FAIL"
        };
    }
}
