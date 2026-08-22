using System.Text.Json;
using TerminalMcp.Core;

namespace CdpMcpBridge;

/// <summary>Bridge-local read of durable job SSOT when CdpService is down during deploy (ADR-0203).</summary>
internal static class CdpBridgeDurableAccess
{
    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    internal static bool HasInFlightDeploy() =>
        DurableJobStore.TryGetInFlightKind("deploy") is not null;

    internal static string? InFlightDeployJobId() =>
        DurableJobStore.TryGetInFlightKind("deploy");

    internal static string ReadLifecycleLast(IReadOnlyDictionary<string, JsonElement> args) =>
        AnnotateLocal(DurableJobStore.Last(
            Opt(args, "job_id"),
            Opt(args, "kind"),
            Pretty));

    internal static string ReadLifecycleScene() =>
        AnnotateLocal(DurableJobStore.Scene(Pretty));

    internal static bool TryReadJob(string jobId, out DurableJobRecord record) =>
        DurableJobStore.TryReadRecordPublic(jobId, out record!);

    internal static bool IsRunningState(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("state", out var state))
                return false;
            var s = state.GetString();
            return s is "queued" or "running";
        }
        catch
        {
            return false;
        }
    }

    internal static bool TryParseJobOk(string json, out bool ok)
    {
        ok = false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("ok", out var okEl))
            {
                ok = okEl.ValueKind == JsonValueKind.True;
                return true;
            }

            if (root.TryGetProperty("exit_code", out var code) && code.TryGetInt32(out var n))
            {
                ok = n == 0;
                return true;
            }
        }
        catch
        {
            /* ignore */
        }

        return false;
    }

    internal static string? TryParseJobId(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("job_id", out var id)
                && id.ValueKind == JsonValueKind.String)
                return id.GetString();
        }
        catch
        {
            /* ignore */
        }

        return null;
    }

    static string AnnotateLocal(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var writer = new MemoryStream();
            using (var w = new Utf8JsonWriter(writer, new JsonWriterOptions { Indented = true }))
            {
                w.WriteStartObject();
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    prop.WriteTo(w);
                }

                w.WriteBoolean("bridge_local", true);
                w.WriteString("bridge_hint",
                    "Read from durable job store while CdpService is restarting — no agent poll procedure needed.");
                w.WriteEndObject();
            }

            return System.Text.Encoding.UTF8.GetString(writer.ToArray());
        }
        catch
        {
            return json;
        }
    }

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key) =>
        args.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;
}
