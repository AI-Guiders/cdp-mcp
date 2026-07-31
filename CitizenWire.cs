#nullable enable

namespace CdpMcp;

/// <summary>
/// Citizen wire host seam (CDP-ADR-0028 peel #7).
/// Afferent packer + prepend API behind <see cref="Inject"/> (default off).
/// No live completions host — synthetic tests only until habitat chat lands.
/// </summary>
internal static class CitizenWire
{
    public const string EnvInject = "CDP_CITIZEN_WIRE_INJECT";

    /// <summary>Process latch — tests flip this; host may set true when ready.</summary>
    public static bool Inject { get; set; }

    public static bool IsInjectEnabled()
    {
        if (Inject)
            return true;
        var env = Environment.GetEnvironmentVariable(EnvInject);
        return string.Equals(env, "1", StringComparison.Ordinal)
            || string.Equals(env, "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Desk A pulse fields for <c>@frame desk</c>.</summary>
    public sealed record DeskPulse(
        string Board,
        string Sa,
        string? Peer = null,
        string? Next = null,
        string? Tm = null,
        string Cost = "A");

    public static string PackDesk(DeskPulse pulse, string version = "v0")
    {
        ArgumentNullException.ThrowIfNull(pulse);
        var sb = new System.Text.StringBuilder();
        sb.Append("@frame desk ").Append(version).Append('\n');
        AppendField(sb, "board", pulse.Board);
        AppendField(sb, "sa", pulse.Sa);
        if (!string.IsNullOrWhiteSpace(pulse.Peer))
            AppendField(sb, "peer", pulse.Peer);
        if (!string.IsNullOrWhiteSpace(pulse.Next))
            AppendField(sb, "next", pulse.Next);
        if (!string.IsNullOrWhiteSpace(pulse.Tm))
            AppendField(sb, "tm", pulse.Tm);
        AppendField(sb, "cost", string.IsNullOrWhiteSpace(pulse.Cost) ? "A" : pulse.Cost.Trim());
        return sb.ToString();
    }

    /// <summary>
    /// Prepend afferent pulse to host message bodies when inject is on.
    /// Guest Cursor path must leave <see cref="Inject"/> false.
    /// </summary>
    public static IReadOnlyList<string> PrependAfferent(
        IReadOnlyList<string> messages,
        string? afferentPulse)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (!IsInjectEnabled() || string.IsNullOrWhiteSpace(afferentPulse))
            return messages;

        var list = new List<string>(messages.Count + 1) { afferentPulse.TrimEnd() + "\n" };
        list.AddRange(messages);
        return list;
    }

    static void AppendField(System.Text.StringBuilder sb, string key, string value)
    {
        sb.Append(key).Append(" | ").Append(value.Trim()).Append('\n');
    }
}
