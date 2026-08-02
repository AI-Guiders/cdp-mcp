#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=teeth</c> / Meta <c>cdp_teeth</c> — one-glance guest-host environment.
/// Afferent timeline for OOM tooth, CDT, remount/oom wake, partner away/here (ADR-0029).
/// </summary>
internal static partial class IdeTeethChannel
{
    public const string SchemaVersion = "teeth_channel/v1";
    public const string ToolName = "cdp_teeth";
    public const string GoName = "teeth";

    static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string HandleJson(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args = null) =>
        JsonSerializer.Serialize(Handle(session, args), Pretty);

    public static object Handle(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args = null)
    {
        _ = session;
        args ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var op = (Opt(args, "op") ?? Opt(args, "cmd") ?? "scene").Trim().ToLowerInvariant();
        return op switch
        {
            "scene" or "help" or "status" => Scene(args),
            "tail" or "list" or "recent" => Tail(args),
            "explain" or "why" => Explain(args),
            _ => Fail("unknown_op", "op=scene|tail|explain")
        };
    }

    public static string PulseLine()
    {
        try
        {
            // Health/ops embed this pulse — never leave cdt=? when CDT is reachable.
            // Full live sample only when unknown or stale (OOM watch / fire already refresh often).
            return BuildPulse(BuildNow(cdtLive: ShouldRefreshCdtSample()));
        }
        catch
        {
            return "teeth · ? · go=teeth";
        }
    }

    /// <summary>Refresh CDT sample when unknown or older than <see cref="CdtSampleTtl"/>.</summary>
    internal static readonly TimeSpan CdtSampleTtl = TimeSpan.FromSeconds(15);

    internal static bool ShouldRefreshCdtSample(DateTimeOffset? nowUtc = null)
    {
        if (IdeTeethTape.LastCdtUp is null)
            return true;
        var now = nowUtc ?? DateTimeOffset.UtcNow;
        var noted = IdeTeethTape.LastCdtNoteUtc;
        if (noted is null)
            return true;
        return now - noted.Value >= CdtSampleTtl;
    }


    internal readonly record struct ArmRow(
        string Id,
        string Status,
        string? Reason,
        string? ChargeMode,
        string? Task,
        bool? SendOk,
        string? SendError,
        DateTimeOffset? SendInvokedUtc,
        string Verdict);

    internal readonly record struct NowSnap(
        bool? CdtUp,
        string? SubmitKind,
        bool RemountPending,
        bool OomWatch,
        int OomClicks,
        int OomWakeScheduled,
        string? LiveVersion,
        string Partner,
        bool Autonomous,
        ArmRow[] Arms,
        string? HildPulse);
}
