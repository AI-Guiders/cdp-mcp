#nullable enable

namespace CdpMcp;

/// <summary>Slot model id for citizen Who/dialog binding (not Anthropic-only default).</summary>
internal static class CitizenIdentity
{
    /// <summary>Live citizen slot model from ai-keys / FM default.</summary>
    public static string ResolveCitizenModel(string? overrideModel = null)
    {
        if (!string.IsNullOrWhiteSpace(overrideModel))
            return overrideModel.Trim();

        var keys = CitizenAiKeys.Load();
        if (keys.HasOpenAi)
            return keys.ResolvedOpenAiModel;
        if (keys.HasAnthropic)
            return CitizenCompletions.DefaultModel;
        return CitizenAiKeys.DefaultOpenAiModel;
    }

    /// <summary>Filesystem-safe fragment for per-model dialog files.</summary>
    public static string SanitizeModelKey(string model)
    {
        var t = model.Trim();
        if (t.Length == 0)
            return "default";
        Span<char> buf = stackalloc char[Math.Min(t.Length, 96)];
        var n = 0;
        foreach (var c in t)
        {
            if (n >= buf.Length)
                break;
            buf[n++] = c is '/' or '\\' or ':' or '<' or '>' or '|' or '*' or '?' or '"'
                ? '_'
                : c;
        }

        return new string(buf[..n]);
    }
}
