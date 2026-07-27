#nullable enable
using System.Text;
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Share with… — outward delivery to operator without loading body into agent context.
/// Partials: Plan (promote alias), Util (inbox/norms/slug).
/// Sibling of <c>put</c> (into IDE) and inverse of <c>take</c> (into agent).
/// </summary>
internal static partial class IdeShare
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
}
