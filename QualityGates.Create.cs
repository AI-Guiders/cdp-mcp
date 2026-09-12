namespace CdpMcp;

internal static partial class QualityGates
{
    public static object? ForCreateResult(DocBuffer buf, string? projectRoot)
    {
        var policy = LoadEffective(projectRoot);
        if (!policy.Enabled)
            return null;

        var finding = TryCreateEmptyFinding(buf);
        if (finding is null)
        {
            return new
            {
                schema = SchemaVersion,
                ok = true,
                pulse = "gates ok",
                warn = 0,
                fail = 0,
                findings = Array.Empty<object>(),
                next = SuggestNext(policy, [], EditSniper.HasHold)
            };
        }

        var findings = new List<QualityFinding> { finding };
        return new
        {
            schema = SchemaVersion,
            ok = true,
            pulse = "gates WARN×1",
            warn = 1,
            fail = 0,
            findings = findings.Select(FindingCard).ToArray(),
            next = SuggestNext(policy, findings, EditSniper.HasHold),
            hint = "Empty code create is allowed but suspicious — confirm intent or add text=."
        };
    }

    public static bool ShouldWarnCreateEmpty(DocBuffer buf) =>
        buf.Text.Length == 0 && IsFileLinesSubject(buf);

    public static string CreateEmptyHint(string path) =>
        $"create landed empty: {ShortPath(path)} — pass text= (or content=/body= alias) with body.";

    static QualityFinding? TryCreateEmptyFinding(DocBuffer buf)
    {
        if (buf.Text.Length > 0 || !IsFileLinesSubject(buf))
            return null;

        return new QualityFinding(
            "create_empty",
            "warn",
            buf.Path,
            null,
            "char_count",
            0,
            1,
            $"{ShortPath(buf.Path)}: create landed empty — code file with 0 chars",
            "cdp_buffer op=edit edit_op=set_text or recreate with text=");
    }
}
