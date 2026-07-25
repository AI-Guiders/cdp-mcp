#nullable enable
using System.Text;
using System.Text.Json;
using Cdp.Core;
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

/// <summary>
/// Share with… — outward delivery to operator without loading body into agent context.
/// Sibling of <c>put</c> (into IDE) and inverse of <c>take</c> (into agent).
/// Plan + ask=confirm reuses <see cref="IdePlanPromote"/>; promote remains an alias.
/// </summary>
internal static class IdeShare
{
    public const string SchemaVersion = "share/v0";

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    /// <summary>Buffer/span → operator inbox file; agent gets thin <c>chat</c> only.</summary>
    public static string ShareBuffer(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var with = NormalizeWith(Opt(args, "with") ?? Opt(args, "to") ?? "operator");
        if (with is not "operator")
        {
            return JsonSerializer.Serialize(new
            {
                schema = SchemaVersion,
                ok = false,
                op = "share",
                error = "unsupported_with",
                with,
                hint = "v0: with=operator only. take= into agent context when you need the body."
            }, Pretty);
        }

        var ask = NormalizeAsk(Opt(args, "ask"));
        var span = EditorComfort.ResolveTakeSpan(store, session, args);
        var buf = span.Buf;
        var body = span.Body;
        EditorComfort.RememberFile(buf.Path);

        var dir = ResolveShareInbox(session.ProjectRoot, Opt(args, "dir") ?? Opt(args, "inbox"));
        Directory.CreateDirectory(dir);

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var baseName = Path.GetFileNameWithoutExtension(buf.Path);
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = "share";
        var ext = Path.GetExtension(buf.Path);
        if (string.IsNullOrEmpty(ext))
            ext = ".md";
        var shareId = Guid.NewGuid().ToString("N")[..12];
        var fileName = $"share-{stamp}-{Slug(baseName)}{ext}";
        var path = Path.Combine(dir, fileName);
        File.WriteAllText(path, body, Encoding.UTF8);

        var latest = Path.Combine(dir, "LATEST" + ext);
        File.Copy(path, latest, overwrite: true);
        var latestJson = Path.Combine(dir, "LATEST.json");
        var status = ask == "confirm" ? "awaiting_confirm" : "shared";
        var meta = new
        {
            schema = SchemaVersion,
            share_id = shareId,
            with = "operator",
            what = "buffer",
            ask,
            status,
            path,
            source = buf.Path,
            from = span.From,
            lines = CountLines(body),
            chars = body.Length,
            shared_utc = DateTime.UtcNow
        };
        File.WriteAllText(latestJson, JsonSerializer.Serialize(meta, Pretty), Encoding.UTF8);

        var chat = ask == "confirm"
            ? $"Shared (awaiting confirm): {path}"
            : $"Shared: {path}";

        return JsonSerializer.Serialize(new
        {
            schema = SchemaVersion,
            ok = true,
            op = "share",
            with = "operator",
            what = "buffer",
            ask,
            status,
            share_id = shareId,
            path,
            latest,
            latest_json = latestJson,
            inbox = dir,
            source = buf.Path,
            from = span.From,
            lines = CountLines(body),
            chars = body.Length,
            chat,
            next = ask == "confirm"
                ? new object[]
                {
                    new { go = "confirm", label = "Confirm", why = "operator approved share" },
                    new { go = "reject", label = "Reject", why = "operator declined" }
                }
                : new object[]
                {
                    new { go = "share", label = "Share again", why = "new revision to operator" },
                    new { go = "take", label = "Take into agent", why = "rare — load body into context" }
                },
            hint =
                "Share with operator: file on disk; relay chat= only. " +
                "Not take — take pulls body into agent context (token-expensive). " +
                (ask == "confirm" ? "ask=confirm → cmd=confirm|reject after human reads." : "")
        }, Pretty);
    }

    /// <summary>Wrap plan promote as share with=operator what=plan.</summary>
    public static object SharePlan(
        IntentWorkspaceStore store,
        IntentWorkspaceState state,
        string? projectRoot,
        string? notes,
        string? dirOverride,
        string? ask)
    {
        var askNorm = NormalizeAsk(ask);
        if (askNorm is "none")
            askNorm = "confirm";
        var promoted = IdePlanPromote.Promote(store, state, projectRoot, notes, dirOverride);
        return new
        {
            schema = SchemaVersion,
            ok = true,
            op = "share",
            with = "operator",
            what = "plan",
            ask = askNorm,
            alias_of = "promote",
            result = promoted,
            chat = ExtractChat(promoted),
            hint =
                "share with=operator what=plan ask=confirm (alias: promote). " +
                "Human reads path; agent relays chat= only — do not paste plan body."
        };
    }

    public static string ResolveShareInbox(string? projectRoot, string? dirOverride)
    {
        if (!string.IsNullOrWhiteSpace(dirOverride))
            return Path.GetFullPath(dirOverride);
        if (!string.IsNullOrWhiteSpace(projectRoot))
            return Path.GetFullPath(Path.Combine(projectRoot, ".cdp", "share"));
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "cdp-mcp",
            "share");
    }

    static string? ExtractChat(object promoted)
    {
        try
        {
            var json = JsonSerializer.Serialize(promoted);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("chat", out var c) ? c.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    static string NormalizeWith(string raw)
    {
        var s = raw.Trim().ToLowerInvariant();
        if (s is "operator" or "human" or "user" or "me" or "host")
            return "operator";
        return s;
    }

    static string NormalizeAsk(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "none";
        var s = raw.Trim().ToLowerInvariant();
        return s is "confirm" or "approve" or "yes" or "ask" ? "confirm"
            : s is "none" or "no" or "off" ? "none"
            : s;
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
        return s.Length == 0 ? "share" : s.Length <= 32 ? s : s[..32];
    }

    static int CountLines(string text)
    {
        if (text.Length == 0) return 1;
        var n = 1;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n') n++;
        }

        return n;
    }

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el) || el.ValueKind != JsonValueKind.String)
            return null;
        var s = el.GetString();
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}
