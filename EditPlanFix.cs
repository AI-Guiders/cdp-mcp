using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// edit_plan <c>fix:</c> lane — diagnostic id → Roslyn code action (document), kj-1758.
/// </summary>
internal static partial class EditPlanFix
{
    static readonly Regex ActionLine = ActionLineRegex();

    public static async Task<(bool Ok, string? Error, IReadOnlyList<DiagHit> Hits)> FindHitsAsync(
        SessionContext session,
        IReadOnlyDictionary<string, ICdpBackendModule> byDomain,
        string filePath,
        IReadOnlyList<string> fixIds,
        CancellationToken cancellationToken)
    {
        if (fixIds.Count == 0)
            return (true, null, []);

        var sol = session.SolutionOrProjectPath;
        if (string.IsNullOrWhiteSpace(sol))
            return (false, "fix requires cdp_open with .sln/.csproj (solution_or_project_path).", []);

        if (!byDomain.TryGetValue(CdpDomains.Roslyn, out var roslyn))
            return (false, "roslyn backend not mounted.", []);

        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["solution_or_project_path"] = JsonSerializer.SerializeToElement(sol),
            ["file_path"] = JsonSerializer.SerializeToElement(filePath),
            ["scope"] = JsonSerializer.SerializeToElement("project")
        };

        var raw = await roslyn.CallAsync("roslyn_get_diagnostics", args).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryParseDiagItems(raw, out var items, out var parseErr))
            return (false, parseErr, []);

        var want = new HashSet<string>(fixIds, StringComparer.OrdinalIgnoreCase);
        var hits = items
            .Where(i => want.Contains(i.Id))
            .Where(i => string.Equals(Normalize(i.File), Normalize(filePath), StringComparison.OrdinalIgnoreCase)
                        || i.File.EndsWith(Path.GetFileName(filePath), StringComparison.OrdinalIgnoreCase))
            .GroupBy(i => i.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderBy(x => x.Line).ThenBy(x => x.Column).First())
            .ToArray();

        var missing = fixIds.Where(id => hits.All(h => !string.Equals(h.Id, id, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (missing.Length > 0)
            return (false, $"diag_not_present:{string.Join(",", missing)}", hits);

        return (true, null, hits);
    }

    public static async Task<(bool Ok, string? Error, object? Detail)> ApplyHitsAsync(
        SessionContext session,
        IReadOnlyDictionary<string, ICdpBackendModule> byDomain,
        string filePath,
        IReadOnlyList<DiagHit> hits,
        CancellationToken cancellationToken)
    {
        var sol = session.SolutionOrProjectPath
            ?? throw new InvalidOperationException("solution_or_project_path required.");
        if (!byDomain.TryGetValue(CdpDomains.Roslyn, out var roslyn))
            return (false, "roslyn backend not mounted.", null);

        var applied = new List<object>();
        foreach (var hit in hits)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var listArgs = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["solution_or_project_path"] = JsonSerializer.SerializeToElement(sol),
                ["file_path"] = JsonSerializer.SerializeToElement(filePath),
                ["line"] = JsonSerializer.SerializeToElement(Math.Max(1, hit.Line)),
                ["column"] = JsonSerializer.SerializeToElement(Math.Max(1, hit.Column)),
                ["end_line"] = JsonSerializer.SerializeToElement(Math.Max(hit.Line, hit.EndLine)),
                ["end_column"] = JsonSerializer.SerializeToElement(Math.Max(hit.Column, hit.EndColumn))
            };
            var listRaw = await roslyn.CallAsync("roslyn_get_code_actions", listArgs).ConfigureAwait(false);
            var actions = ParseActionList(listRaw);
            if (actions.Count == 0)
                return (false, $"no_code_actions:{hit.Id}@{hit.Line}:{hit.Column}", new { hit, list = listRaw });

            var pick = PickAction(actions, hit.Id);
            if (pick is null)
            {
                return (false, $"no_matching_action:{hit.Id}", new
                {
                    hit,
                    actions = actions.Select(a => new { a.Index, a.Title }).ToArray(),
                    hint = "Pass a more specific action title later; v0 picks by id/title heuristics."
                });
            }

            var applyArgs = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["solution_or_project_path"] = JsonSerializer.SerializeToElement(sol),
                ["file_path"] = JsonSerializer.SerializeToElement(filePath),
                ["line"] = JsonSerializer.SerializeToElement(Math.Max(1, hit.Line)),
                ["column"] = JsonSerializer.SerializeToElement(Math.Max(1, hit.Column)),
                ["end_line"] = JsonSerializer.SerializeToElement(Math.Max(hit.Line, hit.EndLine)),
                ["end_column"] = JsonSerializer.SerializeToElement(Math.Max(hit.Column, hit.EndColumn)),
                ["action_index"] = JsonSerializer.SerializeToElement(pick.Index),
                ["fix_all_scope"] = JsonSerializer.SerializeToElement("document")
            };
            var applyRaw = await roslyn.CallAsync("roslyn_apply_code_action", applyArgs).ConfigureAwait(false);
            if (applyRaw.StartsWith("Error:", StringComparison.OrdinalIgnoreCase))
                return (false, applyRaw, new { hit, action = pick });

            applied.Add(new
            {
                id = hit.Id,
                line = hit.Line,
                column = hit.Column,
                action_index = pick.Index,
                action_title = pick.Title,
                fix_all_scope = "document",
                result_summary = Truncate(applyRaw, 400)
            });
        }

        return (true, null, applied);
    }

    public static string BuildFixYaml(string path, IEnumerable<string> ids, string? message = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("- path: " + YamlQuote(path));
        if (!string.IsNullOrWhiteSpace(message))
            sb.AppendLine("  message: " + YamlQuote(message));
        sb.AppendLine("  fix:");
        foreach (var id in ids.Distinct(StringComparer.OrdinalIgnoreCase))
            sb.AppendLine("    - " + id);
        return sb.ToString();
    }

    public static async Task<string?> SuggestFixYamlAsync(
        SessionContext session,
        IReadOnlyDictionary<string, ICdpBackendModule> byDomain,
        string filePath,
        CancellationToken cancellationToken)
    {
        var sol = session.SolutionOrProjectPath;
        if (string.IsNullOrWhiteSpace(sol) || !byDomain.TryGetValue(CdpDomains.Roslyn, out var roslyn))
            return null;

        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["solution_or_project_path"] = JsonSerializer.SerializeToElement(sol),
            ["file_path"] = JsonSerializer.SerializeToElement(filePath),
            ["scope"] = JsonSerializer.SerializeToElement("project")
        };
        var raw = await roslyn.CallAsync("roslyn_get_diagnostics", args).ConfigureAwait(false);
        if (!TryParseDiagItems(raw, out var items, out _))
            return null;

        var ids = items
            .Where(i => string.Equals(Normalize(i.File), Normalize(filePath), StringComparison.OrdinalIgnoreCase)
                        || i.File.EndsWith(Path.GetFileName(filePath), StringComparison.OrdinalIgnoreCase))
            .Select(i => i.Id)
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(24)
            .ToArray();
        if (ids.Length == 0)
            return null;

        var rel = session.ProjectRoot is { Length: > 0 } root
            && filePath.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? filePath[(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length + 1)..]
            : Path.GetFileName(filePath);

        return BuildFixYaml(rel.Replace('\\', '/'), ids, "fix document diagnostics");
    }

}
