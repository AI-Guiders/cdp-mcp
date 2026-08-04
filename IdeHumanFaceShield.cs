#nullable enable
using System.Text.Json;
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

/// <summary>
/// Human-face shield for #CIDE ships — cheap agent-text Done is illegal when viewer is human.
/// Parity with FeatureDone half-a / PathMutateGate: refuse until dig evidence (PNG path on disk) or force=.
/// shot=true bool alone is seeming — operator 2026-08-04 «Выстрела нет».
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
            $"task_done refused — {RefuseId}: #CIDE ship needs evidence=path.png on disk " +
            "(shot=true bool alone is illegal). Glass Done = human eyes on PNG, not agent claim. force=true escape.");
    }

    internal static bool HasShotEvidence(IReadOnlyDictionary<string, JsonElement>? args)
    {
        if (args is null)
            return false;

        // shot=true / screenshot=true alone = seeming (operator «Выстрела нет» 2026-08-04).
        foreach (var key in new[] { "evidence", "shot_path", "screenshot_path", "png" })
        {
            if (TryPngPath(args, key))
                return true;
        }

        if (args.TryGetValue("go_args", out var ga) && ga.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in ga.EnumerateObject())
            {
                if ((p.NameEquals("evidence") || p.NameEquals("shot_path") || p.NameEquals("png")
                     || p.NameEquals("screenshot_path"))
                    && p.Value.ValueKind == JsonValueKind.String
                    && IsPngEvidencePath(p.Value.GetString()))
                    return true;
            }
        }

        return false;
    }

    static bool TryPngPath(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el) || el.ValueKind != JsonValueKind.String)
            return false;
        return IsPngEvidencePath(el.GetString());
    }

    /// <summary>Path must name a .png and exist on disk — bool shot= is not evidence.</summary>
    internal static bool IsPngEvidencePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;
        var s = path.Trim();
        if (!s.Contains(".png", StringComparison.OrdinalIgnoreCase))
            return false;
        try
        {
            return File.Exists(s);
        }
        catch
        {
            return false;
        }
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
