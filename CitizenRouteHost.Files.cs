#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Citizen @intent files — sync IdeFilesChannel.Handle; place files_desk organ.</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake files JSON; live uses <see cref="IdeFilesChannel.Handle"/>.</summary>
    internal static Func<IReadOnlyDictionary<string, JsonElement>, string>? FilesHandleOverride { get; set; }

    static Applied RunFiles(CitizenIntentRouter.Route route)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "scene" : route.Op!;
        var args = BuildFilesArgs(route, op);

        try
        {
            string json;
            if (FilesHandleOverride is { } ov)
            {
                json = ov(args);
            }
            else
            {
                var store = IdeLanguageTools.TryGetDocumentStore();
                if (store is null)
                {
                    return new Applied(
                        route.Raw,
                        route.Verb.ToString(),
                        Ok: false,
                        Action: "files",
                        Go: "files_desk",
                        Path: route.Path,
                        Reason: "doc_store_unbound");
                }

                var session = SessionResolver?.Invoke();
                if (session is null)
                {
                    return new Applied(
                        route.Raw,
                        route.Verb.ToString(),
                        Ok: false,
                        Action: "files",
                        Go: "files_desk",
                        Path: route.Path,
                        Reason: "no_session");
                }

                var result = IdeFilesChannel.Handle(store, session, args);
                json = result is string s
                    ? s
                    : JsonSerializer.Serialize(result);
            }

            var ok = TryReadFilesOk(json);
            var pulse = TryReadFilesPulse(json, op);
            // Parity with take: listing/text body → Applied.Ship → @event peer + Face tip.
            // Pulse alone ("files · … · 37") is SoftInstrument chip — human Radio saw no names (lived 2026-08-09).
            var ship = ok ? TryReadFilesShip(json) : null;
            if (ship is { Length: > 0 }
                && pulse is not null
                && pulse.IndexOf("ship=", StringComparison.Ordinal) < 0)
                pulse = TruncPulse(pulse + " ship=" + ship.Length);
            var seat = IdeDeskSeats.PlaceOrgan("files_desk");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "files",
                Seat: seat,
                Go: "files_desk",
                Path: route.Path,
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "files_failed"),
                Ship: ship);
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "files",
                Go: "files_desk",
                Path: route.Path,
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildFilesArgs(CitizenIntentRouter.Route route, string op)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement(op)
        };

        var raw = route.Raw;
        PutIfPresent(args, "path",
            route.Path
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "path")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "to")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "name")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "file"));

        PutIfPresent(args, "where",
            CitizenIntentRouter.ExtractKeyedValue(raw, "where"));

        PutIfPresent(args, "query",
            CitizenIntentRouter.ExtractKeyedValue(raw, "query")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "q"));

        var depthRaw = CitizenIntentRouter.ExtractKeyedValue(raw, "depth");
        if (depthRaw is { Length: > 0 } && int.TryParse(depthRaw, out var depth))
            args["depth"] = JsonSerializer.SerializeToElement(depth);

        return args;
    }

    static bool TryReadFilesOk(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("ok", out var okEl))
                return okEl.ValueKind != JsonValueKind.False;
            if (root.TryGetProperty("error", out var err)
                && err.ValueKind == JsonValueKind.String
                && err.GetString() is { Length: > 0 })
                return false;
            return root.TryGetProperty("pulse", out _)
                || root.TryGetProperty("entries", out _)
                || root.TryGetProperty("cwd", out _)
                || root.TryGetProperty("schema", out _);
        }
        catch
        {
            return false;
        }
    }

    static string? TryReadFilesPulse(string json, string op)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("pulse", out var p) && p.ValueKind == JsonValueKind.String
                && p.GetString() is { Length: > 0 } pulse)
                return TruncPulse(pulse);

            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String
                && err.GetString() is { Length: > 0 } e)
                return TruncPulse($"files {op} fail {e}");

            if (root.TryGetProperty("total", out var t) && t.ValueKind == JsonValueKind.Number)
                return TruncPulse($"files {op} ok total={t.GetInt32()}");

            return TruncPulse($"files {op} ok");
        }
        catch
        {
            return TruncPulse("files " + op);
        }
    }

    /// <summary>
    /// Files board → agent/human body (Completions peer + Face). Prefer entries listing;
    /// text op falls back to body/chat_markdown like take.
    /// </summary>
    internal const int FilesShipMaxChars = 16_000;

    static string? TryReadFilesShip(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("entries", out var entries)
                && entries.ValueKind == JsonValueKind.Array
                && entries.GetArrayLength() > 0)
            {
                var sb = new System.Text.StringBuilder();
                if (root.TryGetProperty("cwd", out var cwdEl)
                    && cwdEl.ValueKind == JsonValueKind.String
                    && cwdEl.GetString() is { Length: > 0 } cwd)
                    sb.Append("cwd | ").Append(cwd).Append('\n');

                var n = 0;
                foreach (var row in entries.EnumerateArray())
                {
                    if (n >= 80)
                        break;
                    var kind = row.TryGetProperty("kind", out var k) && k.ValueKind == JsonValueKind.String
                        ? k.GetString()
                        : null;
                    var name = row.TryGetProperty("name", out var nm) && nm.ValueKind == JsonValueKind.String
                        ? nm.GetString()
                        : null;
                    var path = row.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String
                        ? p.GetString()
                        : null;
                    if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(path))
                        continue;
                    var label = !string.IsNullOrWhiteSpace(name) ? name! : path!;
                    var glyph = string.Equals(kind, "dir", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(kind, "directory", StringComparison.OrdinalIgnoreCase)
                        ? "dir "
                        : "file";
                    sb.Append(glyph).Append(' ').Append(label).Append('\n');
                    n++;
                }

                if (root.TryGetProperty("truncated", out var tr)
                    && tr.ValueKind == JsonValueKind.True)
                    sb.Append("(truncated)\n");
                else if (root.TryGetProperty("total", out var totalEl)
                         && totalEl.ValueKind == JsonValueKind.Number
                         && totalEl.GetInt32() > n)
                    sb.Append("(+").Append(totalEl.GetInt32() - n).Append(" more)\n");

                var listing = sb.ToString().TrimEnd();
                if (listing.Length > 0)
                    return TruncFilesShip(listing);
            }

            string? ship = null;
            if (root.TryGetProperty("chat_markdown", out var md) && md.ValueKind == JsonValueKind.String)
                ship = md.GetString();
            if (string.IsNullOrWhiteSpace(ship)
                && root.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.String)
                ship = body.GetString();
            if (string.IsNullOrWhiteSpace(ship)
                && root.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                ship = text.GetString();
            if (string.IsNullOrWhiteSpace(ship))
                return null;
            return TruncFilesShip(ship.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd());
        }
        catch
        {
            return null;
        }
    }

    static string TruncFilesShip(string ship)
    {
        if (ship.Length <= FilesShipMaxChars)
            return ship;
        return ship[..FilesShipMaxChars] + "\n…[ship truncated chars=" + ship.Length + "]";
    }
}
