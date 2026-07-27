#nullable enable
using System.Text.Json;

namespace CdpMcp;

internal static partial class IdeCrmChannel
{
    public static string? NormCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var s = raw.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        return s switch
        {
            "approved" or "approve" or "cleared" or "clear" or "confirm" or "yes" => "approved",
            "stabilized" or "stable" or "on_path" or "onpath" => "stabilized",
            "go_around" or "goaround" or "reject" or "denied" or "abort" => "go_around",
            "hold" or "standby" or "stand_by" or "wait" => "hold",
            "unable" => "unable",
            "negative" or "no" or "nop" => "negative",
            "say_again" or "sayagain" or "repeat" => "say_again",
            "continue" or "cont" => "continue",
            "roger" or "ack" => "roger",
            "wilco" => "wilco",
            _ => Lexicon.Contains(s) ? s : null
        };
    }

    static object Err(string error, string hint) => new
    {
        ok = false,
        schema = SchemaVersion,
        error,
        hint,
        lexicon = Lexicon
    };

    static IReadOnlyDictionary<string, JsonElement> FlattenGoArgs(IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!args.TryGetValue("go_args", out var ga) || ga.ValueKind != JsonValueKind.Object)
            return args;
        var flat = new Dictionary<string, JsonElement>(args, StringComparer.Ordinal);
        foreach (var p in ga.EnumerateObject())
        {
            if (!flat.ContainsKey(p.Name))
                flat[p.Name] = p.Value.Clone();
        }

        return flat;
    }

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return null;
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString()?.Trim(),
            JsonValueKind.Number => el.GetRawText(),
            _ => null
        };
    }

    public sealed record CrmSnap(
        string Schema,
        string CallId,
        string Status,
        string? Callout,
        string Kind,
        string RefId,
        string Ask,
        DateTimeOffset OpenedUtc,
        DateTimeOffset? ResolvedUtc,
        string? Why);
}
