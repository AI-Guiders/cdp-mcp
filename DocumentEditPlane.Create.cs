using System.Text.Json;

namespace CdpMcp;

internal static partial class DocumentEditPlane
{
    readonly record struct CreateBodyResolve(string Body, string? Hint, bool UsedAlias);

    /// <summary>
    /// ADX-HX-003: create accepts text= (preferred), content=/body= aliases — no silent empty when alias supplied.
    /// </summary>
    static CreateBodyResolve ResolveCreateBody(IReadOnlyDictionary<string, JsonElement> args)
    {
        var hasText = args.ContainsKey("text");
        var hasContent = args.ContainsKey("content");
        var hasBody = args.ContainsKey("body");
        var text = OptString(args, "text");
        var content = OptString(args, "content");
        var body = OptString(args, "body");

        if (hasText)
        {
            var hint = hasContent || hasBody
                ? "create: text= wins over content=/body= aliases."
                : null;
            return new(text ?? "", hint, UsedAlias: false);
        }

        if (content is not null)
            return new(content, "create: used content= alias (prefer text=).", UsedAlias: true);

        if (body is not null)
            return new(body, "create: used body= alias (prefer text=).", UsedAlias: true);

        return new("", null, UsedAlias: false);
    }

    static string? MergeCreateHints(string? resolveHint, DocBuffer buf)
    {
        var hints = new List<string>();
        if (!string.IsNullOrWhiteSpace(resolveHint))
            hints.Add(resolveHint!);
        if (QualityGates.ShouldWarnCreateEmpty(buf))
            hints.Add(QualityGates.CreateEmptyHint(buf.Path));
        return hints.Count == 0 ? null : string.Join(" ", hints);
    }
}
