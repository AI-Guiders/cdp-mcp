#nullable enable
using System.Net.Http;
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Agent vision organ — attach PNG/JPEG/WebP (etc.) as MCP <c>ImageContent</c> via <see cref="ToolMediaOutbox"/>.
/// Meta <c>cdp_see</c> / go=<c>see</c>|<c>see_desk</c>. Not Lynx; not Cursor host Read.
/// World dig: figures from papers / UI refs / Glass evidence PNGs.
/// </summary>
internal static class IdeSeeChannel
{
    public const string Schema = "see/v0";
    public const string ToolName = "cdp_see";
    public const string GoName = "see_desk";

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    static readonly HashSet<string> ImageExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".tif", ".tiff"
    };

    static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(45)
    };

    public static string HandleJson(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args) =>
        JsonSerializer.Serialize(Handle(session, args), Pretty);

    public static object Handle(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args)
    {
        args ??= new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        var op = Opt(args, "op") ?? "see";
        if (op is "scene" or "pulse" or "which")
        {
            return new
            {
                ok = true,
                schema = Schema,
                go = GoName,
                tool = ToolName,
                op,
                pulse = "see · path=|url= → ImageContent (agent vision)",
                hint = "cdp_see path=…png | url=https://…/fig.webp — attaches ImageContent via ToolMediaOutbox (max 2, ≤2.5MB)."
            };
        }

        var path = Opt(args, "path") ?? Opt(args, "file") ?? Opt(args, "file_path") ?? Opt(args, "image");
        var url = Opt(args, "url") ?? Opt(args, "href") ?? Opt(args, "src");
        if (string.IsNullOrWhiteSpace(path) && string.IsNullOrWhiteSpace(url))
        {
            return Err("path_or_url_required",
                "cdp_see path=D:\\…\\shot.png | url=https://…/figure.webp");
        }

        try
        {
            byte[] bytes;
            string mime;
            string source;
            string? localPath = null;

            if (!string.IsNullOrWhiteSpace(url))
            {
                var u = url.Trim();
                if (!Uri.TryCreate(u, UriKind.Absolute, out var uri)
                    || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps
                        && uri.Scheme != Uri.UriSchemeFile))
                {
                    return Err("bad_url", "url= must be http(s) or file://");
                }

                if (uri.Scheme == Uri.UriSchemeFile)
                {
                    localPath = uri.LocalPath;
                    (bytes, mime) = ReadFile(localPath);
                    source = "file_url";
                }
                else
                {
                    using var resp = Http.GetAsync(uri).GetAwaiter().GetResult();
                    if (!resp.IsSuccessStatusCode)
                    {
                        return Err("http_failed",
                            $"GET {(int)resp.StatusCode} {resp.ReasonPhrase}");
                    }

                    bytes = resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                    mime = GuessMime(
                        resp.Content.Headers.ContentType?.MediaType,
                        uri.AbsolutePath);
                    source = "http";
                    // Optional cache under project evidence for dogfood / dig=
                    localPath = TryCacheDownload(session, bytes, mime, uri);
                }
            }
            else
            {
                localPath = ResolvePath(session, path!);
                (bytes, mime) = ReadFile(localPath);
                source = "path";
            }

            if (bytes.Length == 0)
                return Err("empty", "image bytes empty");
            if (bytes.Length > ToolMediaOutbox.MaxBytesPerImage)
            {
                return Err("too_large",
                    $"image {bytes.Length} bytes > ToolMediaOutbox.MaxBytesPerImage ({ToolMediaOutbox.MaxBytesPerImage})");
            }

            var attached = ToolMediaOutbox.TryAdd(bytes, mime);
            return new
            {
                ok = true,
                schema = Schema,
                go = GoName,
                tool = ToolName,
                op = "see",
                pulse = attached
                    ? $"see · ImageContent · {mime} · {bytes.Length}B"
                    : $"see · outbox full/reject · {mime} · {bytes.Length}B",
                source,
                path = localPath,
                url = string.IsNullOrWhiteSpace(url) ? null : url.Trim(),
                bytes = bytes.Length,
                mime,
                attached_image = attached,
                note = attached
                    ? "ImageContent attached for agent vision (ToolMediaOutbox)"
                    : "outbox full (max 2) or reject — shrink / see fewer images this turn",
                hint = "World dig: after cdp_browser links → cdp_see url=|path= before inventing UI chrome."
            };
        }
        catch (Exception ex)
        {
            return Err("see_failed", ex.Message);
        }
    }

    static (byte[] Bytes, string Mime) ReadFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("image not found", path);
        var ext = Path.GetExtension(path);
        if (!ImageExt.Contains(ext) && GuessMime(null, path) == "application/octet-stream")
            throw new InvalidOperationException($"not an image extension: {ext}");
        var bytes = File.ReadAllBytes(path);
        return (bytes, GuessMime(null, path));
    }

    static string ResolvePath(SessionContext session, string raw)
    {
        var p = raw.Trim().Trim('"');
        if (Path.IsPathRooted(p))
            return Path.GetFullPath(p);
        var root = session.ProjectRoot;
        if (string.IsNullOrWhiteSpace(root))
            root = Directory.GetCurrentDirectory();
        return Path.GetFullPath(Path.Combine(root, p));
    }

    static string? TryCacheDownload(SessionContext session, byte[] bytes, string mime, Uri uri)
    {
        try
        {
            var root = session.ProjectRoot;
            if (string.IsNullOrWhiteSpace(root))
                return null;
            var dir = Path.Combine(root, ".cdp", "evidence", "see-cache");
            Directory.CreateDirectory(dir);
            var ext = mime switch
            {
                "image/jpeg" => ".jpg",
                "image/webp" => ".webp",
                "image/gif" => ".gif",
                "image/bmp" => ".bmp",
                _ => ".png"
            };
            var name = "see-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-"
                       + Math.Abs(uri.GetHashCode()).ToString("x") + ext;
            var path = Path.Combine(dir, name);
            File.WriteAllBytes(path, bytes);
            return path;
        }
        catch
        {
            return null;
        }
    }

    static string GuessMime(string? header, string pathOrUrl)
    {
        if (!string.IsNullOrWhiteSpace(header)
            && header.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return header.Split(';')[0].Trim();

        var ext = Path.GetExtension(pathOrUrl.Contains('?', StringComparison.Ordinal)
            ? pathOrUrl[..pathOrUrl.IndexOf('?', StringComparison.Ordinal)]
            : pathOrUrl);
        return ext.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".tif" or ".tiff" => "image/tiff",
            ".png" => "image/png",
            _ => "image/png"
        };
    }

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el) || el.ValueKind != JsonValueKind.String)
            return null;
        var s = el.GetString();
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    static object Err(string error, string hint) => new
    {
        ok = false,
        schema = Schema,
        go = GoName,
        tool = ToolName,
        error,
        hint
    };
}
