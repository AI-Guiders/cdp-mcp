#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

internal static partial class IdeRefactorPlanChannel
{
    static object? BuildBudget(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args,
        DebtSnap debt)
    {
        var policy = QualityGates.LoadEffective(session.ProjectRoot);
        var path = Opt(args, "path") ?? Opt(args, "file_path") ?? debt.Items.FirstOrDefault()?.Path;
        if (path is null or { Length: 0 })
            return new { ok = false, error = "path_required", pulse = "refactor_plan · budget · no path" };

        var full = ResolvePath(session, path);
        EnsureOpen(store, full);
        var beforeFile = QuietLineCount(full);
        var methodHit = debt.Items.FirstOrDefault(h =>
            h.Path.Equals(full, StringComparison.OrdinalIgnoreCase) && h.Metric == "method_lines")
            ?? ParseFindings(QualityGates.EvaluatePath(store, session.ProjectRoot, full))
                .FirstOrDefault(h => h.Metric == "method_lines");

        var afterFile = OptInt(args, "after_lines") ?? OptInt(args, "after_file_lines");
        var afterMethod = OptInt(args, "after_method_lines");
        var extractLines = OptInt(args, "extract_lines");

        if (afterFile is null && extractLines is int ex && ex > 0)
            afterFile = Math.Max(0, beforeFile - ex);

        var fileWhatIf = BuildFileWhatIf(afterFile, beforeFile, policy.FileLinesWarn, policy.FileLinesFail);
        var methodWhatIf = BuildMethodWhatIf(afterMethod, methodHit, policy.MethodLinesWarn, policy.MethodLinesFail);
        return FormatBudgetPayload(session, policy, full, beforeFile, methodHit, afterFile, fileWhatIf, methodWhatIf);
    }

    static object? BuildFileWhatIf(int? afterFile, int beforeFile, int fileWarn, int fileFail) =>
        afterFile is int af
            ? new
            {
                after = af,
                delta = af - beforeFile,
                vs_warn = af - fileWarn,
                vs_fail = af - fileFail,
                clears_warn = beforeFile >= fileWarn && af < fileWarn,
                clears_fail = beforeFile >= fileFail && af < fileFail,
                verdict = af >= fileFail ? "still_fail" : af >= fileWarn ? "still_warn" : "under_warn"
            }
            : null;

    static object? BuildMethodWhatIf(int? afterMethod, Hotspot? methodHit, int methodWarn, int methodFail) =>
        afterMethod is int am && methodHit is not null
            ? new
            {
                symbol = methodHit.Symbol,
                before = methodHit.Value,
                after = am,
                delta = am - methodHit.Value,
                vs_warn = am - methodWarn,
                vs_fail = am - methodFail,
                clears_warn = methodHit.Value >= methodWarn && am < methodWarn,
                clears_fail = methodHit.Value >= methodFail && am < methodFail,
                verdict = am >= methodFail ? "still_fail" : am >= methodWarn ? "still_warn" : "under_warn"
            }
            : null;

    static object FormatBudgetPayload(
        SessionContext session,
        QualityGates.QualityPolicy policy,
        string full,
        int beforeFile,
        Hotspot? methodHit,
        int? afterFile,
        object? fileWhatIf,
        object? methodWhatIf)
    {
        var pulse = fileWhatIf is null && methodWhatIf is null
            ? $"refactor_plan · budget · before file={beforeFile} · pass after_lines="
            : $"refactor_plan · budget · file {beforeFile}→{afterFile?.ToString() ?? "?"}";

        return new
        {
            ok = true,
            pulse,
            path = full,
            rel = Rel(session.ProjectRoot, full),
            policy = new
            {
                file_lines_warn = policy.FileLinesWarn,
                file_lines_fail = policy.FileLinesFail,
                method_lines_warn = policy.MethodLinesWarn,
                method_lines_fail = policy.MethodLinesFail,
                source = policy.Source
            },
            before = new
            {
                file_lines = beforeFile,
                method_lines = methodHit?.Value,
                method_symbol = methodHit?.Symbol
            },
            after_file = fileWhatIf,
            after_method = methodWhatIf,
            hint = "after_lines= or extract_lines= for file budget; after_method_lines= for worst-method what-if."
        };
    }
}
