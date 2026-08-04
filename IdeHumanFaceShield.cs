#nullable enable
using System.Text.Json;
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

/// <summary>
/// Human-face shield for #CIDE ships — cheap agent-text Done is illegal when viewer is human.
/// Parity with FeatureDone half-a / PathMutateGate: refuse until dig evidence (PNG) or force=.
/// </summary>
internal static class IdeHumanFaceShield
{
    internal const string RefuseId = "human_face_cide_shot";

    internal static void RefuseCideDoneWithoutShot(
        IntentWorkspaceStore store,
        Guid stageId,
        IReadOnlyDictionary<string, JsonElement>? args)
    {
        if (ForceArg(args))
            return;
        if (HasShotEvidence(args))
            return;

        var peek = store.TryGetStageTitleProduct(stageId);
        if (peek is null)
            return;
        var (_, product) = peek.Value;
        if (!string.Equals(product, "CIDE", StringComparison.OrdinalIgnoreCase))
            return;

        throw new ArgumentException(
            $"task_done refused — {RefuseId}: #CIDE ship needs human screenshot evidence " +
            "(evidence=path.png | shot=true). Glass Done = human eyes, not agent text dump. force=true escape.");
    }

    internal static bool HasShotEvidence(IReadOnlyDictionary<string, JsonElement>? args)
    {
        if (args is null)
            return false;

        if (Boolish(args, "shot") || Boolish(args, "screenshot") || Boolish(args, "human_shot"))
            return true;

        foreach (var key in new[] { "evidence", "shot_path", "screenshot_path", "png" })
        {
            if (!args.TryGetValue(key, out var el))
                continue;
            if (el.ValueKind != JsonValueKind.String)
                continue;
            var s = el.GetString()?.Trim() ?? "";
            if (s.Length > 0 && s.Contains(".png", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        if (args.TryGetValue("go_args", out var ga) && ga.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in ga.EnumerateObject())
            {
                if (p.NameEquals("shot") || p.NameEquals("screenshot") || p.NameEquals("human_shot"))
                {
                    if (p.Value.ValueKind == JsonValueKind.True)
                        return true;
                    if (p.Value.ValueKind == JsonValueKind.String
                        && bool.TryParse(p.Value.GetString(), out var b) && b)
                        return true;
                }

                if ((p.NameEquals("evidence") || p.NameEquals("shot_path") || p.NameEquals("png"))
                    && p.Value.ValueKind == JsonValueKind.String
                    && (p.Value.GetString() ?? "").Contains(".png", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    static bool Boolish(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return false;
        if (el.ValueKind == JsonValueKind.True)
            return true;
        return el.ValueKind == JsonValueKind.String
               && bool.TryParse(el.GetString(), out var b)
               && b;
    }

    static bool ForceArg(IReadOnlyDictionary<string, JsonElement>? args)
    {
        if (args is null)
            return false;
        if (Boolish(args, "force"))
            return true;
        if (args.TryGetValue("go_args", out var ga) && ga.ValueKind == JsonValueKind.Object
            && ga.TryGetProperty("force", out var f))
        {
            if (f.ValueKind == JsonValueKind.True)
                return true;
            if (f.ValueKind == JsonValueKind.String && bool.TryParse(f.GetString(), out var b) && b)
                return true;
        }

        return false;
    }
}
