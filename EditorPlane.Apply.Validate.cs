using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;
using CdpMcp.Cockpit.Surface;

namespace CdpMcp;
internal static partial class EditorPlane
{
    static List<string> ValidateStep(DocumentBufferStore store, SessionContext session, EditStep step, bool resolveAnchors)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(step.Path))
            errors.Add("path required");
        if (string.IsNullOrWhiteSpace(step.EditOp))
            errors.Add("edit_op required");
        var op = (step.EditOp ?? "").Trim().ToLowerInvariant();
        if (op is not ("anchor" or "replace" or "replace_range" or "set_text"))
            errors.Add("edit_op must be anchor|replace|replace_range|set_text");
        string? fullPath = null;
        if (!string.IsNullOrWhiteSpace(step.Path))
        {
            try
            {
                fullPath = ResolveUserPath(session, step.Path);
                var open = store.All.FirstOrDefault(b => string.Equals(b.Path, fullPath, StringComparison.OrdinalIgnoreCase));
                if (open is null && !File.Exists(fullPath))
                    errors.Add($"path_not_found:{fullPath}");
            }
            catch (Exception ex)
            {
                errors.Add($"path_resolve:{ex.Message}");
            }
        }

        switch (op)
        {
            case "anchor":
                if (string.IsNullOrWhiteSpace(step.Anchor) && string.IsNullOrWhiteSpace(step.At))
                    errors.Add("anchor (or at) required");
                if (string.IsNullOrWhiteSpace(step.Text) && string.IsNullOrWhiteSpace(step.NewString))
                    errors.Add("text (or new_string) required");
                if (resolveAnchors && errors.Count == 0 && fullPath is not null && (File.Exists(fullPath) || store.All.Any(b => string.Equals(b.Path, fullPath, StringComparison.OrdinalIgnoreCase))))
                {
                    var wire = step.Anchor ?? step.At!;
                    try
                    {
                        var span = BracketLocate.Parse(wire);
                        var text = store.All.FirstOrDefault(b => string.Equals(b.Path, fullPath, StringComparison.OrdinalIgnoreCase))?.Text ?? File.ReadAllText(fullPath);
                        var family = BracketLocate.ClassifyFamily(span, out var familyError);
                        if (familyError is not null)
                            errors.Add(familyError);
                        else if (family == BracketLocate.AxisFamily.Csharp)
                        {
                            if (!BracketSyntaxResolve.TryResolve(fullPath, text, span, out _, out var detail))
                                errors.Add($"anchor_resolve:{detail}");
                        }
                        else if (family == BracketLocate.AxisFamily.Xml)
                        {
                            if (!BracketXmlResolve.TryResolve(fullPath, text, span, out _, out var detail))
                                errors.Add($"anchor_resolve:{detail}");
                        }
                        else
                            errors.Add("anchor_family_none");
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"anchor_parse:{ex.Message}");
                    }
                }

                break;
            case "replace":
                if (string.IsNullOrWhiteSpace(step.OldString))
                    errors.Add("old_string required");
                break;
            case "replace_range":
                if (step.StartLine is null || step.StartColumn is null || step.EndLine is null || step.EndColumn is null)
                    errors.Add("start_line/start_column/end_line/end_column required");
                break;
            case "set_text":
                // text may be empty intentionally
                break;
        }

        return errors;
    }

    static Dictionary<string, JsonElement> BuildEditArgs(SessionContext session, EditStep step, bool flush, bool diagnose)
    {
        var path = ResolveUserPath(session, step.Path!);
        var dict = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement("edit"),
            ["path"] = JsonSerializer.SerializeToElement(path),
            ["edit_op"] = JsonSerializer.SerializeToElement(step.EditOp!.Trim().ToLowerInvariant()),
            ["flush"] = JsonSerializer.SerializeToElement(flush),
            ["diagnose"] = JsonSerializer.SerializeToElement(diagnose)
        };
        void Put(string key, string? value)
        {
            if (value is not null)
                dict[key] = JsonSerializer.SerializeToElement(value);
        }

        void PutInt(string key, int? value)
        {
            if (value is not null)
                dict[key] = JsonSerializer.SerializeToElement(value.Value);
        }

        Put("anchor", step.Anchor);
        Put("at", step.At);
        Put("text", step.Text);
        Put("old_string", step.OldString);
        Put("new_string", step.NewString);
        PutInt("start_line", step.StartLine);
        PutInt("start_column", step.StartColumn);
        PutInt("end_line", step.EndLine);
        PutInt("end_column", step.EndColumn);
        if (step.AllowShrink is { } ash)
            dict["allow_shrink"] = JsonSerializer.SerializeToElement(ash);
        return dict;
    }
}