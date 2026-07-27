#nullable enable
using System.Text;
using System.Text.Json;

namespace CdpMcp;

internal static partial class IdePlanPromote
{
    static void WriteStatus(string path, PlanStatus status)
    {
        var payload = new
        {
            schema = status.Schema,
            plan_id = status.PlanId,
            status = status.Status,
            path = status.Path,
            feature = status.Feature,
            feature_id = status.FeatureId,
            task_id = status.TaskId,
            task = status.Task,
            promoted_utc = status.PromotedUtc,
            resolved_utc = status.ResolvedUtc,
            notes = status.Notes
        };
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }),
            Encoding.UTF8);
    }

    static PlanStatus? ReadStatus(string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var r = doc.RootElement;
            return new PlanStatus(
                r.TryGetProperty("schema", out var sch) ? sch.GetString() ?? SchemaVersion : SchemaVersion,
                r.GetProperty("plan_id").GetString() ?? "",
                r.GetProperty("status").GetString() ?? Awaiting,
                r.GetProperty("path").GetString() ?? "",
                r.TryGetProperty("feature", out var f) ? f.GetString() : null,
                r.TryGetProperty("feature_id", out var fid) && Guid.TryParse(fid.GetString(), out var fg) ? fg : null,
                r.TryGetProperty("task_id", out var tid) && Guid.TryParse(tid.GetString(), out var tg) ? tg : null,
                r.TryGetProperty("task", out var t) ? t.GetString() : null,
                r.TryGetProperty("promoted_utc", out var pu) && DateTime.TryParse(pu.GetString(), out var pdt)
                    ? pdt.ToUniversalTime()
                    : DateTime.UtcNow,
                r.TryGetProperty("resolved_utc", out var ru) && DateTime.TryParse(ru.GetString(), out var rdt)
                    ? rdt.ToUniversalTime()
                    : null,
                r.TryGetProperty("notes", out var n) ? n.GetString() : null);
        }
        catch
        {
            return null;
        }
    }

    static string Slug(string title)
    {
        var sb = new StringBuilder(title.Length);
        foreach (var ch in title.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(ch))
                sb.Append(ch);
            else if (ch is ' ' or '-' or '_' && sb.Length > 0 && sb[^1] != '-')
                sb.Append('-');
        }

        var s = sb.ToString().Trim('-');
        return s.Length == 0 ? "plan" : s.Length <= 32 ? s : s[..32];
    }

}
