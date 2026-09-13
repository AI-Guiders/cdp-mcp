using AIGuiders.Platform.Modeling.Notations.Argument;
using AIGuiders.Platform.Notations;
using AIGuiders.Platform.Notations.Argument.Kv;

namespace Cdp.Deploy;

/// <summary>Console path/kv split — mirrors <c>ConsoleCommandNotation</c> (GUIDERS-ADR-0021).</summary>
static class DeployCommandNotation
{
    public readonly record struct PathWire(IReadOnlyList<string> Tokens, bool EndsWithSpaceAfterTokens);

    public static bool TryParse(string line, out PathWire pathWire, out NormalizedArguments args, out string kvTailRaw)
    {
        pathWire = new PathWire([], false);
        args = NormalizedArguments.FromRaw("");
        kvTailRaw = "";

        if (string.IsNullOrWhiteSpace(line))
            return false;

        var endsWithSpace = line.EndsWith(' ');
        var tokens = line.TrimEnd().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (tokens.Count == 0)
            return false;

        var pathTokens = new List<string>();
        var kvStart = tokens.Count;
        for (var i = 0; i < tokens.Count; i++)
        {
            if (IsKvToken(tokens[i]))
            {
                kvStart = i;
                break;
            }

            pathTokens.Add(tokens[i]);
        }

        if (pathTokens.Count == 0)
            return false;

        pathWire = new PathWire(pathTokens, endsWithSpace && kvStart >= tokens.Count);
        kvTailRaw = kvStart < tokens.Count ? string.Join(' ', tokens.Skip(kvStart)) : "";
        args = KvArgumentNotation.Parse(kvTailRaw);
        return true;
    }

    static bool IsKvToken(string token)
    {
        var eq = token.IndexOf('=');
        return eq > 0 && eq < token.Length - 1;
    }
}
