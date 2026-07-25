using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;
using CdpMcp.Backends;

namespace CdpMcp.IntentWorkspace;

/// <summary>Background IdeReport jobs keyed by stage id (ToDo carrier).</summary>
internal sealed class IdeReportJobRunner(
    IntentWorkspaceStore store,
    IReadOnlyDictionary<string, ICdpBackendModule> byDomain)
{
    private readonly SemaphoreSlim _gate = new(2, 2);

    public void Enqueue(Guid stageId, string jobJson)
    {
        _ = Task.Run(async () =>
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                await RunJobAsync(stageId, jobJson).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                try { store.StageFailJob(stageId, ex.Message); }
                catch { /* best effort */ }
            }
            finally
            {
                _gate.Release();
            }
        });
    }

    private async Task RunJobAsync(Guid stageId, string jobJson)
    {
        using var doc = JsonDocument.Parse(jobJson);
        var root = doc.RootElement;
        var kind = root.TryGetProperty("kind", out var k) ? k.GetString() : null;
        if (string.IsNullOrWhiteSpace(kind))
            throw new ArgumentException("job.kind is required.");

        var filePath = ReqString(root, "file_path");
        int? line = root.TryGetProperty("line", out var ln) && ln.TryGetInt32(out var li) ? li : null;
        int? column = root.TryGetProperty("column", out var col) && col.TryGetInt32(out var ci) ? ci : null;

        if (string.Equals(kind, "correspondence", StringComparison.OrdinalIgnoreCase))
        {
            var solutionOpt = root.TryGetProperty("solution_or_project_path", out var solEl)
                ? solEl.GetString()
                : null;
            var anchor = new CodeAnchor(filePath, line, column, solutionOpt);
            var hint = WorkspaceCorrespondence.FindWorkspaceRoot(filePath, solutionOpt is { Length: > 0 }
                ? Path.GetDirectoryName(solutionOpt)
                : null);
            var lootCorr = IdeReport.Correspondence(anchor, hint).ToJson();
            store.StageCompleteJob(stageId, lootCorr);
            return;
        }

        if (!byDomain.TryGetValue(CdpDomains.Roslyn, out var roslyn) || !roslyn.IsEnabled)
            throw new InvalidOperationException("Roslyn backend not mounted.");

        var solution = ReqString(root, "solution_or_project_path");
        var roslynAnchor = new CodeAnchor(filePath, line, column, solution);

        string loot;
        if (string.Equals(kind, "semantic_map", StringComparison.OrdinalIgnoreCase))
        {
            var mode = root.TryGetProperty("mode", out var m) && m.GetString() is { Length: > 0 } ms
                ? ms
                : "related";
            var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["solution_or_project_path"] = JsonSerializer.SerializeToElement(solution),
                ["file_path"] = JsonSerializer.SerializeToElement(filePath),
                ["mode"] = JsonSerializer.SerializeToElement(mode)
            };
            if (line is { } l) args["line"] = JsonSerializer.SerializeToElement(l);
            if (column is { } c) args["column"] = JsonSerializer.SerializeToElement(c);
            var raw = await roslyn.CallAsync("roslyn_get_workspace_navigation_context", args).ConfigureAwait(false);
            loot = IdeReportBuilder.FromSemanticMapRelated(roslynAnchor, raw, mode).ToJson();
        }
        else if (string.Equals(kind, "find_usages", StringComparison.OrdinalIgnoreCase))
        {
            if (line is null or < 1 || column is null or < 1)
                throw new ArgumentException("find_usages job requires line and column.");
            var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["solution_or_project_path"] = JsonSerializer.SerializeToElement(solution),
                ["file_path"] = JsonSerializer.SerializeToElement(filePath),
                ["line"] = JsonSerializer.SerializeToElement(line.Value),
                ["column"] = JsonSerializer.SerializeToElement(column.Value)
            };
            var raw = await roslyn.CallAsync("roslyn_find_usages", args).ConfigureAwait(false);
            loot = IdeReportBuilder.FromFindUsages(roslynAnchor, raw).ToJson();
        }
        else
        {
            throw new ArgumentException($"Unknown job.kind: {kind}");
        }

        store.StageCompleteJob(stageId, loot);
    }

    private static string ReqString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el) || el.GetString() is not { Length: > 0 } s)
            throw new ArgumentException($"job.{name} is required.");
        return s;
    }
}
