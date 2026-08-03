#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent replace_all — buffer comfort bulk replace without Cursor MCP.</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteReplaceAll(string raw)
    {
        var path = ExtractKeyedValue(raw, "path") ?? ExtractPath(raw);
        var query = ExtractKeyedValue(raw, "query")
            ?? ExtractKeyedValue(raw, "old")
            ?? ExtractKeyedValue(raw, "old_string")
            ?? ExtractKeyedValue(raw, "pattern");
        var text = ExtractKeyedValue(raw, "text")
            ?? ExtractKeyedValue(raw, "new")
            ?? ExtractKeyedValue(raw, "new_string")
            ?? ExtractKeyedValue(raw, "body")
            ?? "";

        if (string.IsNullOrWhiteSpace(path))
            return new Route(Verb.ReplaceAll, raw, Ok: false, Reason: "replace_all_path_required", Go: "buffer");
        if (string.IsNullOrEmpty(query))
            return new Route(Verb.ReplaceAll, raw, Ok: false, Path: path.Trim(), Reason: "replace_all_query_required", Go: "buffer");

        return new Route(
            Verb.ReplaceAll,
            raw,
            Ok: true,
            Op: "replace_all",
            Path: path.Trim(),
            OldString: query,
            NewString: text,
            Go: "buffer");
    }
}
