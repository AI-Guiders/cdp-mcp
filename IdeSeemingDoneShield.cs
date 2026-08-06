#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>
/// Being ≠ seeming — refuse human-faced Done/shipped without PNG on disk + fresh domain stamp.
/// Shared by #CIDE task done, wave shipped, and feature close.
/// </summary>
internal static class IdeSeemingDoneShield
{
    internal const string RefuseHumanFaceId = IdeHumanFaceShield.RefuseId;
    internal const string RefuseDomainStampId = IdeDomainStampShield.RefuseId;

    static readonly string[] HumanFaceTokens =
    [
        "glass", "softorgan", "mfd", "fds", "peel", "intercom", "citizen", "fullready", "full-ready",
        "share", "human", "viz", "cockpit", "topic", "hci", "cide", "#cide", "softfl", "dogfood"
    ];

    internal static bool IsHumanFacedText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var blob = text.ToLowerInvariant();
        foreach (var tok in HumanFaceTokens)
        {
            if (blob.Contains(tok, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    internal static string WaveBlob(IdeWaveChannel.WaveDoc doc)
    {
        var parts = new List<string> { doc.Title };
        parts.AddRange(doc.Items.Select(i => i.Label));
        return string.Join(' ', parts);
    }

    internal static string? InferDomain(string blob)
    {
        var b = blob.ToLowerInvariant();
        if (b.Contains("softorgan", StringComparison.Ordinal)
            || b.Contains("human-viz", StringComparison.Ordinal)
            || b.Contains("human viz", StringComparison.Ordinal))
            return "softorgan-human-viz";
        if (b.Contains("citizen", StringComparison.Ordinal)
            || b.Contains("fullready", StringComparison.Ordinal)
            || b.Contains("full-ready", StringComparison.Ordinal))
            return "citizen";
        if (b.Contains("glass", StringComparison.Ordinal)
            || b.Contains("mfd", StringComparison.Ordinal)
            || b.Contains("fds", StringComparison.Ordinal)
            || b.Contains("peel", StringComparison.Ordinal)
            || b.Contains("intercom", StringComparison.Ordinal)
            || b.Contains("topic", StringComparison.Ordinal)
            || b.Contains("hci", StringComparison.Ordinal)
            || b.Contains("cockpit", StringComparison.Ordinal)
            || b.Contains("share", StringComparison.Ordinal))
            return "glass";
        return null;
    }

    internal static void RefuseHumanFaceShipWithoutTeeth(
        IReadOnlyDictionary<string, JsonElement>? args,
        string textBlob,
        string verb)
    {
        if (ForceArg(args))
            return;
        if (!IsHumanFacedText(textBlob))
            return;

        if (!IdeHumanFaceShield.HasShotEvidence(args))
        {
            throw new ArgumentException(
                $"{verb} refused — {RefuseHumanFaceId}: human-faced ship needs evidence=path.png on disk " +
                "(shot=true bool alone is illegal). Being ≠ seeming — Read PNG into chat. force=true escape.");
        }

        var domain = DomainArg(args) ?? InferDomain(textBlob);
        if (string.IsNullOrWhiteSpace(domain))
        {
            throw new ArgumentException(
                $"{verb} refused — {RefuseDomainStampId}: human-faced ship needs domain=<card-id> " +
                "(or inferrable glass|citizen|softorgan from wave/feature title). force=true escape.");
        }

        var root = Opt(args, "project_root")
                   ?? Opt(args, "workspace_path")
                   ?? IdePressureChannel.TryPeekProjectRoot();
        if (!IdeDomainStampShield.HasFreshStamp(root, domain.Trim(), leafNotBefore: null, out var detail))
        {
            throw new ArgumentException(
                $"{verb} refused — {RefuseDomainStampId}: domain={domain.Trim()} — {detail}. " +
                "Stamp ## last_ship this turn. force=true escape.");
        }

        IdeDomainStampPending.Clear();
    }

    static string? DomainArg(IReadOnlyDictionary<string, JsonElement>? args)
    {
        if (args is null)
            return null;
        foreach (var key in new[] { "domain", "stamp", "card", "domain_id" })
        {
            var v = Opt(args, key);
            if (v is { Length: > 0 })
                return v;
        }

        if (args.TryGetValue("go_args", out var ga) && ga.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in new[] { "domain", "stamp", "card", "domain_id" })
            {
                if (ga.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.String
                    && el.GetString() is { Length: > 0 } s)
                    return s;
            }
        }

        return null;
    }

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return null;
        return el.ValueKind == JsonValueKind.String ? el.GetString() : null;
    }

    static bool Boolish(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return false;
        if (el.ValueKind == JsonValueKind.True)
            return true;
        return el.ValueKind == JsonValueKind.String
               && bool.TryParse(el.GetString(), out var b)
               && b;
    }

    static bool ForceArg(IReadOnlyDictionary<string, JsonElement>? args)
    {
        if (args is null)
            return false;
        if (Boolish(args, "force"))
            return true;
        if (args.TryGetValue("go_args", out var ga) && ga.ValueKind == JsonValueKind.Object
            && ga.TryGetProperty("force", out var f))
        {
            if (f.ValueKind == JsonValueKind.True)
                return true;
            if (f.ValueKind == JsonValueKind.String && bool.TryParse(f.GetString(), out var b) && b)
                return true;
        }

        return false;
    }
}
