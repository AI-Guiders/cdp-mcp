#nullable enable
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;
internal static partial class IdeFdrThresholdPolicy
{
    public static object Apply(int lookback = 500, bool dryRun = false)
    {
        var raises = SuggestFromTape(lookback).Where(c => c.Action is "raise" && c.SuggestedS > c.CurrentS).ToArray();
        if (dryRun)
        {
            return new
            {
                schema = Schema,
                ok = true,
                op = "apply",
                dry_run = true,
                would_write = raises.Select(SlimRaise).ToArray(),
                path = OverlayPath,
                hint = "dry_run=true — no write. Re-run without dry_run to arm overlay."
            };
        }

        var map = raises.ToDictionary(c => c.Tool, c => c.SuggestedS, StringComparer.OrdinalIgnoreCase);
        WriteOverlay(map, lookback, raises);
        return new
        {
            schema = Schema,
            ok = true,
            op = "apply",
            dry_run = false,
            written = raises.Select(SlimRaise).ToArray(),
            count = map.Count,
            path = OverlayPath,
            hint = map.Count == 0 ? "No raise candidates on tape — overlay cleared/empty. hang/async stay informational." : "Overlay armed — ResolveThreshold uses these until clear_overlay or per-call override."
        };
    }

    public static object ClearOverlay()
    {
        var path = OverlayPath;
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        /* best-effort */
        }

        lock (Gate)
        {
            s_overlay = null;
            s_overlayPath = null;
            s_overlayMtimeUtc = default;
        }

        return new
        {
            schema = Schema,
            ok = true,
            op = "clear_overlay",
            path,
            hint = "Overlay cleared — StaticThresholdSeconds only (+ per-call override)."
        };
    }

    static void WriteOverlay(Dictionary<string, int> map, int lookback, IReadOnlyList<Candidate> raises)
    {
        var path = OverlayPath;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);
        var doc = new
        {
            schema = Schema,
            at_utc = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            lookback,
            tools = map.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(k => k.Key, k => k.Value, StringComparer.OrdinalIgnoreCase),
            notes = raises.Select(c => new { tool = c.Tool, why = c.Why }).ToArray()
        };
        var json = JsonSerializer.Serialize(doc, JsonOpts);
        File.WriteAllText(path, json);
        lock (Gate)
        {
            s_overlay = new Dictionary<string, int>(map, StringComparer.OrdinalIgnoreCase);
            s_overlayPath = path;
            try
            {
                s_overlayMtimeUtc = File.GetLastWriteTimeUtc(path);
            }
            catch
            {
                s_overlayMtimeUtc = DateTime.UtcNow;
            }
        }
    }

    static void EnsureOverlayLoaded()
    {
        var path = OverlayPath;
        lock (Gate)
        {
            try
            {
                if (!File.Exists(path))
                {
                    s_overlay = null;
                    s_overlayPath = path;
                    s_overlayMtimeUtc = default;
                    return;
                }

                var mtime = File.GetLastWriteTimeUtc(path);
                if (s_overlay is not null && string.Equals(s_overlayPath, path, StringComparison.OrdinalIgnoreCase) && mtime == s_overlayMtimeUtc)
                    return;
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var root = doc.RootElement;
                if (!root.TryGetProperty("tools", out var toolsEl) || toolsEl.ValueKind != JsonValueKind.Object)
                {
                    s_overlay = null;
                    return;
                }

                var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in toolsEl.EnumerateObject())
                {
                    if (p.Value.TryGetInt32(out var sec))
                        map[p.Name] = Math.Clamp(sec, 0, 600);
                }

                s_overlay = map;
                s_overlayPath = path;
                s_overlayMtimeUtc = mtime;
            }
            catch
            {
                s_overlay = null;
            }
        }
    }
}