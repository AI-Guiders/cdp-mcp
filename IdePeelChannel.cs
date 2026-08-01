#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Thin Meta over live Roslyn <c>roslyn_move_members_to_partial_file</c> — FileLines peel act.
/// Always ListTools (unlike domain shortlist act-only filter). Alias go=peel.
/// </summary>
internal static class IdePeelChannel
{
    public const string Schema = "peel/v0";
    public const string ToolName = "cdp_peel";
    public const string GoName = "peel";
    public const string Underlying = "roslyn_move_members_to_partial_file";

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    public static async Task<string> HandleAsync(
        SessionContext session,
        IReadOnlyDictionary<string, ICdpBackendModule> byDomain,
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken cancellationToken)
    {
        var result = await HandleObjectAsync(session, byDomain, args, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Serialize(result, Pretty);
    }

    public static async Task<object> HandleObjectAsync(
        SessionContext session,
        IReadOnlyDictionary<string, ICdpBackendModule> byDomain,
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken cancellationToken)
    {
        if (!byDomain.TryGetValue(CdpDomains.Roslyn, out var roslyn) || !roslyn.IsEnabled)
        {
            return new
            {
                ok = false,
                schema = Schema,
                go = GoName,
                tool = ToolName,
                error = "roslyn_unavailable",
                pulse = "peel · roslyn off",
                hint = "Enable [dev.roslyn] or cdp_health — peel needs in-proc Roslyn."
            };
        }

        var sol = Opt(args, "solution_or_project_path") ?? session.SolutionOrProjectPath;
        if (string.IsNullOrWhiteSpace(sol))
        {
            return new
            {
                ok = false,
                schema = Schema,
                go = GoName,
                tool = ToolName,
                error = "solution_required",
                pulse = "peel · need cdp_open",
                hint = "cdp_open a .sln/.csproj first (session injects solution_or_project_path)."
            };
        }

        var filePath = Opt(args, "file_path") ?? Opt(args, "path");
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return new
            {
                ok = false,
                schema = Schema,
                go = GoName,
                tool = ToolName,
                error = "path_required",
                pulse = "peel · need path",
                hint = "path= or file_path= source .cs (type locus)."
            };
        }

        var fullFile = ResolvePath(session, filePath);
        var output = Opt(args, "output_file_path") ?? Opt(args, "out") ?? Opt(args, "output");
        if (string.IsNullOrWhiteSpace(output))
        {
            return new
            {
                ok = false,
                schema = Schema,
                go = GoName,
                tool = ToolName,
                error = "output_required",
                pulse = "peel · need out",
                hint = "out= or output_file_path= new TypeName.Topic.cs (must not exist on apply)."
            };
        }

        var fullOut = ResolvePath(session, output);
        if (!TryReadMembers(args, out var members, out var membersErr))
        {
            return new
            {
                ok = false,
                schema = Schema,
                go = GoName,
                tool = ToolName,
                error = membersErr,
                pulse = "peel · need members",
                hint = "members= or member_names=[MethodA, FieldB] — overloads of one name move together."
            };
        }

        var line = OptInt(args, "line") ?? 1;
        var column = OptInt(args, "column") ?? 1;
        var apply = OptBool(args, "apply") ?? false;
        var addDependent = OptBool(args, "add_dependent_upon") ?? true;

        var roslynArgs = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["solution_or_project_path"] = JsonSerializer.SerializeToElement(sol),
            ["file_path"] = JsonSerializer.SerializeToElement(fullFile),
            ["line"] = JsonSerializer.SerializeToElement(line),
            ["column"] = JsonSerializer.SerializeToElement(column),
            ["member_names"] = JsonSerializer.SerializeToElement(members),
            ["output_file_path"] = JsonSerializer.SerializeToElement(fullOut),
            ["apply"] = JsonSerializer.SerializeToElement(apply),
            ["add_dependent_upon"] = JsonSerializer.SerializeToElement(addDependent)
        };

        cancellationToken.ThrowIfCancellationRequested();
        var raw = await roslyn.CallAsync(Underlying, roslynArgs).ConfigureAwait(false);

        object? underlying = null;
        try
        {
            underlying = JsonSerializer.Deserialize<JsonElement>(raw);
        }
        catch
        {
            underlying = raw;
        }

        var pulse = apply
            ? $"peel · apply · {Path.GetFileName(fullFile)} → {Path.GetFileName(fullOut)}"
            : $"peel · preview · {Path.GetFileName(fullFile)} → {Path.GetFileName(fullOut)}";

        return new
        {
            ok = true,
            schema = Schema,
            go = GoName,
            tool = ToolName,
            underlying = Underlying,
            apply,
            pulse,
            file_path = fullFile,
            output_file_path = fullOut,
            member_names = members,
            result = underlying,
            hint = apply
                ? "Applied via Roslyn TryApplyChanges. Reload buffers if open."
                : "Preview only — re-call apply=true to write. Prefer go=refactor partials for seam path."
        };
    }

    static bool TryReadMembers(
        IReadOnlyDictionary<string, JsonElement> args,
        out string[] members,
        out string error)
    {
        members = [];
        error = "members_required";

        if (args.TryGetValue("member_names", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            members = arr.EnumerateArray()
                .Select(e => e.GetString()?.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Cast<string>()
                .ToArray();
        }
        else if (args.TryGetValue("members", out var m))
        {
            if (m.ValueKind == JsonValueKind.Array)
            {
                members = m.EnumerateArray()
                    .Select(e => e.GetString()?.Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Cast<string>()
                    .ToArray();
            }
            else if (m.ValueKind == JsonValueKind.String && m.GetString() is { Length: > 0 } csv)
            {
                members = csv.Split([',', ';', ' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
        }

        if (members.Length == 0)
            return false;

        error = "";
        return true;
    }

    static string ResolvePath(SessionContext session, string path)
    {
        var t = path.Trim();
        if (Path.IsPathRooted(t))
            return Path.GetFullPath(t);
        var root = session.ProjectRoot;
        return string.IsNullOrWhiteSpace(root)
            ? Path.GetFullPath(t)
            : Path.GetFullPath(Path.Combine(root, t));
    }

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key) =>
        args.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()?.Trim()
            : null;

    static int? OptInt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return null;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n))
            return n;
        if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out var p))
            return p;
        return null;
    }

    static bool? OptBool(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return null;
        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(el.GetString(), out var b) => b,
            _ => null
        };
    }
}
