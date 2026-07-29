#nullable enable
using System.Text;
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Dual-axis share shelf: <c>with=self</c> put, <c>from=self</c> take body into agent tool result.
/// Fast path — no Task Manager / desk spray.
/// </summary>
internal static partial class IdeShare
{
    /// <summary>Dispatch buffer-plane share: from= → take; body+with=self → put; else buffer share.</summary>
    public static string DispatchShare(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var from = Opt(args, "from");
        if (!string.IsNullOrWhiteSpace(from))
            return ShareFrom(session.ProjectRoot, args);

        var with = NormalizeWith(Opt(args, "with") ?? Opt(args, "to") ?? WithOperator);
        var body = Opt(args, "body") ?? Opt(args, "text") ?? Opt(args, "content") ?? Opt(args, "notes");
        if (with == WithSelf && !string.IsNullOrWhiteSpace(body))
            return SharePut(session.ProjectRoot, with, body!, args);

        return ShareBuffer(store, session, args);
    }

    /// <summary>Write body onto share shelf (default <c>with=self</c>).</summary>
    public static string SharePut(
        string? projectRoot,
        string withRaw,
        string body,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var with = NormalizeWith(withRaw);
        if (with is not (WithSelf or WithOperator))
        {
            return JsonSerializer.Serialize(new
            {
                schema = SchemaVersion,
                ok = false,
                op = "share",
                error = "unsupported_with",
                with,
                hint = "v1: with=operator|self. from=self to pull shelf body."
            }, Pretty);
        }

        var what = Opt(args, "what") ?? "note";
        var title = Opt(args, "title") ?? Opt(args, "name") ?? what;
        var ask = with == WithSelf ? "none" : NormalizeAsk(Opt(args, "ask"));
        var dir = ResolveShareInbox(projectRoot, Opt(args, "dir") ?? Opt(args, "inbox"), with);
        Directory.CreateDirectory(dir);

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var shareId = Guid.NewGuid().ToString("N")[..12];
        var fileName = $"{Slug(what)}-{stamp}-{Slug(title)}.md";
        var path = Path.Combine(dir, fileName);
        File.WriteAllText(path, body, Encoding.UTF8);

        var latest = Path.Combine(dir, "LATEST.md");
        File.Copy(path, latest, overwrite: true);
        var latestJson = Path.Combine(dir, "LATEST.json");
        var status = with == WithSelf ? "shelved" : ask == "confirm" ? "awaiting_confirm" : "shared";
        var meta = new
        {
            schema = SchemaVersion,
            share_id = shareId,
            with,
            what,
            ask,
            status,
            path,
            title,
            lines = CountLines(body),
            chars = body.Length,
            shared_utc = DateTime.UtcNow
        };
        File.WriteAllText(latestJson, JsonSerializer.Serialize(meta, Pretty), Encoding.UTF8);

        var chat = with == WithSelf
            ? $"Shelved (self): {path}"
            : ask == "confirm"
                ? $"Shared (awaiting confirm): {path}"
                : $"Shared: {path}";

        return JsonSerializer.Serialize(new
        {
            schema = SchemaVersion,
            ok = true,
            op = "share",
            with,
            what,
            ask,
            status,
            share_id = shareId,
            path,
            latest,
            latest_json = latestJson,
            inbox = dir,
            title,
            lines = CountLines(body),
            chars = body.Length,
            chat,
            next = with == WithSelf
                ? new object[]
                {
                    new { go = "share", label = "Share from self", why = "from=self — pull body into agent" },
                    new { go = "share", label = "Shelve again", why = "with=self body=…" }
                }
                : new object[]
                {
                    new { go = "share", label = "Share again", why = "with=operator" }
                },
            hint = with == WithSelf
                ? "share with=self — agent shelf (.cdp/share-self). Relay chat=; later share from=self to pull body."
                : "share with=operator — inbox file; relay chat= only."
        }, Pretty);
    }

    /// <summary>Pull latest shelf body into tool result (agent continuity).</summary>
    public static string ShareFrom(string? projectRoot, IReadOnlyDictionary<string, JsonElement> args)
    {
        var from = NormalizeFrom(Opt(args, "from"));
        if (from is not (WithSelf or WithOperator))
        {
            return JsonSerializer.Serialize(new
            {
                schema = SchemaVersion,
                ok = false,
                op = "share",
                error = "unsupported_from",
                from,
                hint = "from=self|latest (agent shelf) or from=operator (human inbox LATEST)."
            }, Pretty);
        }

        var depth = (Opt(args, "depth") ?? "full").Trim().ToLowerInvariant();
        var dir = ResolveShareInbox(projectRoot, Opt(args, "dir") ?? Opt(args, "inbox"), from);
        var latestMd = Path.Combine(dir, "LATEST.md");
        var latestJson = Path.Combine(dir, "LATEST.json");

        if (!File.Exists(latestMd) && !File.Exists(latestJson))
        {
            return JsonSerializer.Serialize(new
            {
                schema = SchemaVersion,
                ok = false,
                op = "share",
                error = "empty_shelf",
                from,
                inbox = dir,
                hint = "No LATEST on shelf — share with=self body=… first."
            }, Pretty);
        }

        string? body = null;
        if (File.Exists(latestMd))
            body = File.ReadAllText(latestMd);

        object? meta = null;
        if (File.Exists(latestJson))
        {
            try
            {
                meta = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(latestJson));
            }
            catch
            {
                /* best-effort */
            }
        }

        var includeBody = depth is not ("pulse" or "meta" or "slim");
        return JsonSerializer.Serialize(new
        {
            schema = SchemaVersion,
            ok = true,
            op = "share",
            with = from,
            from,
            what = "latest",
            status = "taken",
            inbox = dir,
            latest = File.Exists(latestMd) ? latestMd : null,
            latest_json = File.Exists(latestJson) ? latestJson : null,
            lines = body is null ? 0 : CountLines(body),
            chars = body?.Length ?? 0,
            meta,
            body = includeBody ? body : null,
            chat = includeBody
                ? $"Took from {from}: {(body?.Length ?? 0)} chars"
                : $"Shelf pulse from {from}: {latestMd}",
            hint = includeBody
                ? "share from=self — body returned in tool result (continuity shelf). depth=pulse for meta only."
                : "depth=pulse — meta only; omit depth or depth=full for body."
        }, Pretty);
    }
}
