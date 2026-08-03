#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent verify_wave|cdp_verify_wave — IdeVerifyWaveChannel.</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteVerifyWave(string raw)
    {
        var work = NormalizeVerifyWaveCompound(raw);
        var op = ExtractKeyedValue(work, "op") ?? ExtractKeyedValue(work, "cmd");
        if (string.IsNullOrWhiteSpace(op)
            && (work.StartsWith("verify_wave ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("verify_wave_desk ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("wave_verify ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("cdp_verify_wave ", StringComparison.OrdinalIgnoreCase)))
        {
            var sp = work.IndexOf(' ');
            var rest = sp < 0 ? "" : work[(sp + 1)..].Trim();
            var headSp = rest.IndexOf(' ');
            var head = headSp < 0 ? rest : rest[..headSp];
            if (head.Length > 0 && !head.Contains('=', StringComparison.Ordinal))
                op = head;
        }

        op = string.IsNullOrWhiteSpace(op) ? "scene" : op.Trim().ToLowerInvariant();
        if (op is not ("scene" or "pulse" or "a"))
            return new Route(Verb.VerifyWave, raw, Ok: false, Reason: "verify_wave_op_unknown");
        if (op == "a") op = "pulse";

        return new Route(Verb.VerifyWave, raw, Ok: true, Op: op, Go: "verify_wave");
    }

    static string NormalizeVerifyWaveCompound(string raw)
    {
        foreach (var (prefix, op) in VerifyWaveCompounds)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return "verify_wave " + op;
            if (!raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;
            return "verify_wave " + op + raw[prefix.Length..];
        }

        return raw;
    }

    static readonly (string Prefix, string Op)[] VerifyWaveCompounds =
    [
        ("cdp_verify_wave_scene", "scene"),
        ("cdp_verify_wave_pulse", "pulse"),
        ("verify_wave_scene", "scene"),
        ("verify_wave_pulse", "pulse")
    ];
}
