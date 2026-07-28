#nullable enable

namespace CdpMcp;

internal static class IdeExplainability
{
    public sealed record ExplainCard(
        string Source,
        string Reason,
        string Authority,
        string NextStep)
    {
        public string WhyLine => $"{Source} · {Reason} · {Authority} · next {NextStep}";
    }

    public static ExplainCard New(
        string source,
        string reason,
        string authority,
        string nextStep) =>
        new(source, reason, authority, nextStep);

    public static object? ToObject(ExplainCard? explain) =>
        explain is null
            ? null
            : new
            {
                source = explain.Source,
                reason = explain.Reason,
                authority = explain.Authority,
                next_step = explain.NextStep,
                why = explain.WhyLine
            };
}
