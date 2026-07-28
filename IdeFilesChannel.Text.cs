#nullable enable
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Cdp.Core;
using CdpMcp.Cockpit.DataAcquisition;

namespace CdpMcp;

/// <summary>Lynx-like text projection for documents (pandoc / pdftotext). Complementary to CDP-ADR-0017 raster preview.</summary>
internal static partial class IdeFilesChannel
{
    public const int TextDefaultMaxChars = 12_000;
    public const int TextHardMaxChars = 80_000;

    static readonly HashSet<string> PandocExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".docx", ".doc", ".odt", ".rtf", ".epub", ".fb2", ".html", ".htm", ".xhtml",
        ".tex", ".org", ".rst", ".twiki", ".mediawiki"
    };

    static readonly HashSet<string> PdfExts = new(StringComparer.OrdinalIgnoreCase) { ".pdf" };

    static readonly HashSet<string> PlainExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".csv", ".tsv", ".json", ".xml", ".yaml", ".yml",
        ".log", ".cs", ".ts", ".js", ".py", ".toml", ".ini", ".cfg"
    };

    internal static bool IsTextProjectable(string path)
    {
        var ext = Path.GetExtension(path);
        return PandocExts.Contains(ext) || PdfExts.Contains(ext);
    }

    static object TextProject(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var target = Opt(args, "path") ?? Opt(args, "name");
        if (string.IsNullOrWhiteSpace(target))
            return Err("path_required", "text path=");

        var cwd = ResolveCwd(session, args, out var where);
        string full;
        try
        {
            full = Path.IsPathRooted(target)
                ? Path.GetFullPath(target)
                : Path.GetFullPath(Path.Combine(cwd, target));
        }
        catch (Exception ex)
        {
            return Err("bad_path", ex.Message);
        }

        if (!File.Exists(full))
            return Err("not_found", full);

        var maxChars = TextDefaultMaxChars;
        if (args.TryGetValue("max_chars", out var mcEl))
        {
            if (mcEl.ValueKind == JsonValueKind.Number && mcEl.TryGetInt32(out var n))
                maxChars = n;
            else if (mcEl.ValueKind == JsonValueKind.String
                     && int.TryParse(mcEl.GetString(), out var ns))
                maxChars = ns;
        }

        maxChars = Math.Clamp(maxChars, 500, TextHardMaxChars);

        var ext = Path.GetExtension(full);
        string engine;
        string body;
        try
        {
            if (PdfExts.Contains(ext))
            {
                engine = "pdftotext";
                body = RunPdfToText(full);
            }
            else if (PandocExts.Contains(ext))
            {
                engine = "pandoc";
                body = RunPandoc(full);
            }
            else if (PlainExts.Contains(ext) || LooksLikeUtf8Text(full))
            {
                engine = "utf8";
                body = File.ReadAllText(full);
            }
            else
            {
                return Err(
                    "unsupported",
                    $"{ext} — try op=open as=buffer for text-like, or install pandoc/pdftotext for docs");
            }
        }
        catch (Exception ex)
        {
            return Err("text_failed", ex.Message);
        }

        body ??= "";
        var truncated = body.Length > maxChars;
        if (truncated)
            body = body[..maxChars];

        return new
        {
            ok = true,
            schema = SchemaVersion,
            go = "files_desk",
            tool = ToolName,
            op = "text",
            where,
            path = full,
            kind = ClassifyDocKind(ext),
            engine,
            pulse = truncated
                ? $"files · text · {Path.GetFileName(full)} · truncated {maxChars}"
                : $"files · text · {Path.GetFileName(full)} · {body.Length}c",
            max_chars = maxChars,
            truncated,
            chars = body.Length,
            text = body,
            next = new object[]
            {
                new { go = "files_desk", label = "More chars", why = $"op=text path={full} max_chars={Math.Min(maxChars * 2, TextHardMaxChars)}" },
                new { go = "files_desk", label = "Open as buffer", why = $"op=open path={full} as=buffer" },
                new { go = "files_desk", label = "List cwd", why = "op=list" }
            },
            hint = "Lynx-like dump — not raster preview (ADR-0017). OCR via webcam op=ocr when asked."
        };
    }

    static string ClassifyDocKind(string ext) =>
        PdfExts.Contains(ext) ? "pdf"
        : PandocExts.Contains(ext) ? "office_or_ebook"
        : "text";

    static string RunPdfToText(string full)
    {
        var bin = ToolchainPathProbe.Resolve("pdftotext")
                   ?? throw new InvalidOperationException("pdftotext not on PATH (poppler/MiKTeX)");
        return RunCapture(bin, ["-layout", "-enc", "UTF-8", full, "-"]);
    }

    static string RunPandoc(string full)
    {
        var bin = ToolchainPathProbe.Resolve("pandoc")
                   ?? throw new InvalidOperationException("pandoc not on PATH");
        return RunCapture(bin, ["-t", "plain", full]);
    }

    static string RunCapture(string fileName, IEnumerable<string> arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var a in arguments)
            psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)
                      ?? throw new InvalidOperationException($"failed to start {fileName}");
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        if (!p.WaitForExit(60_000))
        {
            try { p.Kill(entireProcessTree: true); } catch { /* ignore */ }
            throw new TimeoutException($"{Path.GetFileName(fileName)} timed out");
        }

        if (p.ExitCode != 0)
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(stderr)
                    ? $"{Path.GetFileName(fileName)} exit {p.ExitCode}"
                    : stderr.Trim());

        return stdout;
    }

    static bool LooksLikeUtf8Text(string full)
    {
        try
        {
            var fi = new FileInfo(full);
            if (fi.Length > 2_000_000)
                return false;
            var sampleLen = (int)Math.Min(fi.Length, 4096);
            if (sampleLen == 0)
                return true;
            var buf = new byte[sampleLen];
            using var fs = File.OpenRead(full);
            var read = fs.Read(buf, 0, sampleLen);
            var nul = 0;
            for (var i = 0; i < read; i++)
            {
                if (buf[i] == 0)
                    nul++;
            }

            return nul == 0;
        }
        catch
        {
            return false;
        }
    }
}
