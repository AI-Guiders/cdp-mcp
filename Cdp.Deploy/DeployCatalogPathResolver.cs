using AIGuiders.Platform.Execution.CommandPlane;
using AIGuiders.Platform.IntermediateRepresentation.Command;

namespace Cdp.Deploy;

/// <summary>Longest-prefix catalog resolve — mirrors <c>SlashLineResolver.TryResolveBody</c> (ADR-0150).</summary>
static class DeployCatalogPathResolver
{
    public readonly record struct Resolution(
        string CanonicalPath,
        string ArgTail,
        bool IsCatalogMatch,
        bool IsExactPathMatch,
        bool EndsWithSpaceAfterPath,
        bool HasArgTailContent);

    public static bool TryResolveBody(string body, CommandCatalogIndex catalog, out Resolution resolution)
    {
        resolution = default;
        ParseBody(body, out var tokens, out var endsWithSpace);
        if (tokens.Count == 0)
            return false;

        if (!catalog.TryResolveLongestPrefix(
                tokens, endsWithSpace,
                out var path, out var argTail, out var isExact, out var endsSpaceAfter, out var _))
            return false;

        resolution = new Resolution(
            path,
            argTail,
            true,
            isExact,
            endsSpaceAfter,
            !string.IsNullOrWhiteSpace(argTail));
        return true;
    }

    static void ParseBody(string body, out List<string> tokens, out bool endsWithSpace)
    {
        endsWithSpace = body.EndsWith(' ');
        tokens = body.TrimEnd().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
    }
}
