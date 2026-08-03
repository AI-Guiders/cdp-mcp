#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent project|sln — cdp_project_* / cdp_sln_* without Cursor MCP.</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteProject(string raw)
    {
        var family = DetectProjectFamily(raw);
        var work = NormalizeProjectCompound(raw, family);
        var op = ExtractKeyedValue(work, "op") ?? ExtractKeyedValue(work, "cmd");
        if (string.IsNullOrWhiteSpace(op))
        {
            if (work.StartsWith("project ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("projects ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("sln ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("solution ", StringComparison.OrdinalIgnoreCase))
            {
                var sp = work.IndexOf(' ');
                var rest = sp < 0 ? "" : work[(sp + 1)..].Trim();
                var headSp = rest.IndexOf(' ');
                var head = headSp < 0 ? rest : rest[..headSp];
                if (head.Length > 0 && !head.Contains('=', StringComparison.Ordinal))
                    op = head;
            }
        }

        op = string.IsNullOrWhiteSpace(op)
            ? (work.Equals("projects", StringComparison.OrdinalIgnoreCase)
                ? "list"
                : (family == "sln" ? "list" : "scene"))
            : op.Trim().ToLowerInvariant();
        op = NormalizeProjectOp(op, family);

        if (!IsProjectOp(op, family))
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "project_op_unknown");

        var root = ExtractKeyedValue(work, "root")
            ?? ExtractKeyedValue(work, "path");
        var outputDir = ExtractKeyedValue(work, "output_dir")
            ?? ExtractKeyedValue(work, "dir")
            ?? ExtractKeyedValue(work, "out");
        var name = ExtractKeyedValue(work, "name");
        var template = ExtractKeyedValue(work, "template")
            ?? ExtractKeyedValue(work, "tmpl");
        var project = ExtractKeyedValue(work, "project")
            ?? ExtractKeyedValue(work, "csproj");
        var solution = ExtractKeyedValue(work, "solution")
            ?? ExtractKeyedValue(work, "sln");

        if (op is "create")
        {
            if (string.IsNullOrWhiteSpace(outputDir))
                outputDir = root ?? TryPositionalAfterOp(work, "create");
            if (string.IsNullOrWhiteSpace(outputDir))
                return new Route(Verb.Unknown, raw, Ok: false, Reason: "project_output_dir_required");
        }

        if (op is "add" or "remove")
        {
            if (string.IsNullOrWhiteSpace(project))
                project = root ?? TryPositionalAfterOp(work, op);
            if (string.IsNullOrWhiteSpace(project))
                return new Route(Verb.Unknown, raw, Ok: false, Reason: "project_path_required");
        }

        var path = op switch
        {
            "create" => outputDir,
            "add" or "remove" => project,
            _ => root
        };

        return new Route(
            Verb.Project,
            raw,
            Ok: true,
            Op: op,
            Path: path,
            Tool: name,
            Scene: solution,
            Detail: template,
            Organ: family,
            Go: "project");
    }

    static string DetectProjectFamily(string raw)
    {
        if (raw.Equals("sln", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("sln ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("solution", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("solution ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("sln_list", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("sln_list ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("sln_create", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("sln_create ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("sln_projects", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("sln_projects ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("sln_add", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("sln_add ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("sln_remove", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("sln_remove ", StringComparison.OrdinalIgnoreCase))
            return "sln";
        return "project";
    }

    static string NormalizeProjectCompound(string raw, string family)
    {
        foreach (var (prefix, op) in ProjectCompounds)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return family == "sln" ? "sln " + op : "project " + op;
            if (!raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;

            var rest = raw[prefix.Length..];
            if (ExtractKeyedValue(raw, "op") is { Length: > 0 })
                return (family == "sln" ? "sln" : "project") + rest;
            return (family == "sln" ? "sln " : "project ") + op + rest;
        }

        return raw;
    }

    static readonly (string Prefix, string Op)[] ProjectCompounds =
    [
        ("project_scene", "scene"),
        ("project_list", "list"),
        ("project_create", "create"),
        ("project_close", "close"),
        ("project_add_to_sln", "add"),
        ("project_add", "add"),
        ("sln_list", "list"),
        ("sln_create", "create"),
        ("sln_projects", "projects"),
        ("sln_add", "add"),
        ("sln_remove", "remove")
    ];

    static string NormalizeProjectOp(string op, string family) =>
        op switch
        {
            "map" or "templates" or "new_list" => "scene",
            "ls" or "status" => family == "sln" ? "list" : "list",
            "new" or "scaffold" => "create",
            "add_to_sln" or "addtosln" => "add",
            "rm" or "delete" => "remove",
            "proj" or "csprojs" => family == "sln" ? "projects" : op,
            _ => op
        };

    static bool IsProjectOp(string? op, string family) =>
        family == "sln"
            ? op is "list" or "create" or "projects" or "add" or "remove"
            : op is "scene" or "list" or "create" or "close" or "add";
}
