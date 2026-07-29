#nullable enable
using System.Text;
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Share with… / share from… — dual-axis continuity shelf.
/// Out: <c>with=operator|self</c> (inbox file + thin chat). In: <c>from=self</c> (body into tool result).
/// Sibling of <c>put</c> (into IDE); <c>from=</c> is the intentional inverse for agent continuity.
/// </summary>
internal static partial class IdeShare
{
    public const string SchemaVersion = "share/v1";

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    /// <summary>Buffer/span → share inbox/shelf; agent gets thin <c>chat</c> only (unless from=).</summary>
    public static string ShareBuffer(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var with = NormalizeWith(Opt(args, "with") ?? Opt(args, "to") ?? WithOperator);
        if (with is not (WithOperator or WithSelf))
        {
            return JsonSerializer.Serialize(new
            {
                schema = SchemaVersion,
                ok = false,
                op = "share",
                error = "unsupported_with",
                with,
                hint = "v1: with=operator|self. from=self to pull shelf body into agent."
            }, Pretty);
        }

        var ask = with == WithSelf ? "none" : NormalizeAsk(Opt(args, "ask"));
        var span = EditorComfort.ResolveTakeSpan(store, session, args);
        var buf = span.Buf;
        var body = span.Body;
        EditorComfort.RememberFile(buf.Path);

        var dir = ResolveShareInbox(session.ProjectRoot, Opt(args, "dir") ?? Opt(args, "inbox"), with);
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
        // Prefer LATEST.md for from= consumers even when source was .txt/.cs
        var latestMd = Path.Combine(dir, "LATEST.md");
        if (!string.Equals(latest, latestMd, StringComparison.OrdinalIgnoreCase))
            File.Copy(path, latestMd, overwrite: true);
        var latestJson = Path.Combine(dir, "LATEST.json");
        var status = with == WithSelf ? "shelved" : ask == "confirm" ? "awaiting_confirm" : "shared";
        var meta = new
        {
            schema = SchemaVersion,
            share_id = shareId,
            with,
            what = "buffer",
            ask,
            status,
            path,
            source = buf.Path,
            from_span = span.From,
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
            what = "buffer",
            ask,
            status,
            share_id = shareId,
            path,
            latest = latestMd,
            latest_json = latestJson,
            inbox = dir,
            source = buf.Path,
            from = span.From,
            lines = CountLines(body),
            chars = body.Length,
            chat,
            next = with == WithSelf
                ? new object[]
                {
                    new { go = "share", label = "Share from self", why = "from=self — pull body" },
                    new { go = "share", label = "Shelve again", why = "with=self" }
                }
                : ask == "confirm"
                    ? new object[]
                    {
                        new { go = "confirm", label = "Confirm", why = "operator approved share" },
                        new { go = "reject", label = "Reject", why = "operator declined" }
                    }
                    : new object[]
                    {
                        new { go = "share", label = "Share again", why = "new revision to operator" },
                        new { go = "share", label = "Take from operator shelf", why = "from=operator — rare" }
                    },
            hint = with == WithSelf
                ? "share with=self: agent shelf (.cdp/share-self). Later share from=self to pull body."
                : "Share with operator: file on disk; relay chat= only. from=self pulls agent shelf."
        }, Pretty);
    }
}
