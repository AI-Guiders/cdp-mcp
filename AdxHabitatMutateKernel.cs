#nullable enable

namespace CdpMcp;

/// <summary>
/// ADX-HX-001: full-file rewrite only as draft bootstrap when path was absent;
/// existing paths prefer anchor/replace/replace_range via harness buffer.
/// </summary>
internal static class AdxHabitatMutateKernel
{
    public static bool IsDeltaOp(string? editOp) =>
        editOp?.Trim().ToLowerInvariant() is "anchor" or "replace" or "replace_range";

    public static bool IsFullRewriteOp(string? editOp) =>
        editOp?.Trim().ToLowerInvariant() is "set_text" or "create";

    /// <summary>
    /// Guideline green: create / first write / delta edit on existing path.
    /// </summary>
    public static bool GuidelineOk(bool isCreate, bool pathExistedBefore, string? editOp)
    {
        if (isCreate)
            return true;
        if (!pathExistedBefore)
            return true;
        return IsDeltaOp(editOp);
    }

    public static object CheckCard(bool isCreate, bool pathExistedBefore, string? editOp)
    {
        var ok = GuidelineOk(isCreate, pathExistedBefore, editOp);
        return new
        {
            id = "ADX-HX-001",
            ok,
            is_create = isCreate,
            path_existed_before = pathExistedBefore,
            edit_op = editOp,
            pulse = ok ? "habitat_mutate ok" : "habitat_mutate FAIL set_text_on_existing"
        };
    }
}
