#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent webcam|webcam_desk — sense plane without Cursor MCP (go=webcam_desk place-only).</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteWebcam(string raw)
    {
        var work = NormalizeWebcamCompound(raw);
        var op = ExtractKeyedValue(work, "op") ?? ExtractKeyedValue(work, "cmd");
        if (string.IsNullOrWhiteSpace(op))
        {
            if (work.StartsWith("webcam ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("webcam_desk ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("cdp_webcam ", StringComparison.OrdinalIgnoreCase))
            {
                var sp = work.IndexOf(' ');
                var rest = sp < 0 ? "" : work[(sp + 1)..].Trim();
                var headSp = rest.IndexOf(' ');
                var head = headSp < 0 ? rest : rest[..headSp];
                if (head.Length > 0 && !head.Contains('=', StringComparison.Ordinal))
                    op = head;
            }
        }

        op = string.IsNullOrWhiteSpace(op) ? "scene" : op.Trim().ToLowerInvariant();
        op = NormalizeWebcamOp(op);

        if (!IsWebcamOp(op))
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "webcam_op_unknown");

        return new Route(
            Verb.Webcam,
            raw,
            Ok: true,
            Op: op,
            Path: ExtractKeyedValue(work, "path")
                ?? ExtractKeyedValue(work, "hwnd")
                ?? ExtractKeyedValue(work, "process")
                ?? ExtractKeyedValue(work, "title"),
            Go: "webcam_desk");
    }

    static string NormalizeWebcamCompound(string raw)
    {
        foreach (var (prefix, op) in WebcamCompounds)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return "webcam " + op;
            if (!raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;

            var rest = raw[prefix.Length..];
            if (ExtractKeyedValue(raw, "op") is { Length: > 0 })
                return "webcam" + rest;
            return "webcam " + op + rest;
        }

        return raw;
    }

    static readonly (string Prefix, string Op)[] WebcamCompounds =
    [
        ("webcam_scene", "scene"),
        ("webcam_frame", "frame"),
        ("webcam_burst", "burst"),
        ("webcam_av", "av"),
        ("webcam_screen", "screen"),
        ("webcam_window_list", "window_list"),
        ("webcam_window", "window"),
        ("webcam_audio", "audio"),
        ("webcam_transcribe", "transcribe"),
        ("webcam_ocr", "ocr"),
        ("webcam_analyze", "analyze"),
        ("webcam_desk_scene", "scene"),
        ("cdp_webcam_scene", "scene"),
        ("cdp_webcam_frame", "frame"),
        ("cdp_webcam_window_list", "window_list"),
        ("cdp_webcam_window", "window")
    ];

    static string NormalizeWebcamOp(string op) =>
        op switch
        {
            "status" or "caps" or "desk" or "pulse" => "scene",
            "snap" or "capture" or "photo" => "frame",
            "webcam_burst" or "capture_burst" => "burst",
            "av_burst" or "capture_av" or "capture_av_burst" => "av",
            "screen_burst" or "capture_screen_burst" => "screen",
            "window_snap" or "capture_window" => "window",
            "windows" or "list_windows" => "window_list",
            "record_audio" or "capture_audio" => "audio",
            "transcribe_audio" or "transcribe_audio_whisper" or "whisper" => "transcribe",
            "ocr_batch" or "ocr_image_batch" => "ocr",
            "analyze_burst" or "analyze_burst_sequence" => "analyze",
            _ => op
        };

    static bool IsWebcamOp(string? op) =>
        op is "scene" or "frame" or "burst" or "av" or "screen"
            or "window" or "window_list" or "audio" or "transcribe" or "ocr" or "analyze";
}
