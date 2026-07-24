namespace CdpMcp;

/// <summary>
/// IDE session clipboard — Android-style frames (not OS).
/// Copy/cut push a frame (MRU front). Paste by frame=; preserve=true keeps it (default).
/// </summary>
internal static class SessionClipboard
{
    public const int MaxFrames = 12;
    public const int PreviewShort = 120;
    public const int PreviewLong = 1_200;

    static readonly object Gate = new();
    static readonly List<Frame> Frames = [];
    static int Seq;

    public sealed record Frame(string Id, string Text, string? From, string Kind, DateTimeOffset AtUtc);

    public static bool Any()
    {
        lock (Gate)
            return Frames.Count > 0;
    }

    public static int Count
    {
        get { lock (Gate) return Frames.Count; }
    }

    public static Frame Push(string text, string? from, string kind)
    {
        lock (Gate)
        {
            var id = $"c{++Seq}";
            var frame = new Frame(id, text, from, kind, DateTimeOffset.UtcNow);
            Frames.Insert(0, frame);
            while (Frames.Count > MaxFrames)
                Frames.RemoveAt(Frames.Count - 1);
            return frame;
        }
    }

    public static Frame? Current()
    {
        lock (Gate)
            return Frames.Count > 0 ? Frames[0] : null;
    }

    public static Frame? Find(string? frameArg)
    {
        lock (Gate)
        {
            if (Frames.Count == 0)
                return null;
            if (string.IsNullOrWhiteSpace(frameArg))
                return Frames[0];

            var raw = frameArg.Trim();
            if (raw.Equals("current", StringComparison.OrdinalIgnoreCase)
                || raw is "0" or "^")
                return Frames[0];

            // c3 / C3
            if (raw.StartsWith('c') || raw.StartsWith('C'))
            {
                var hit = Frames.FirstOrDefault(f =>
                    f.Id.Equals(raw, StringComparison.OrdinalIgnoreCase));
                if (hit is not null)
                    return hit;
            }

            // numeric index into MRU (0 = current)
            if (int.TryParse(raw, out var idx) && idx >= 0 && idx < Frames.Count)
                return Frames[idx];

            return Frames.FirstOrDefault(f =>
                f.Id.Equals(raw, StringComparison.OrdinalIgnoreCase));
        }
    }

    public static bool Drop(string? frameArg, out string? droppedId)
    {
        droppedId = null;
        lock (Gate)
        {
            if (string.IsNullOrWhiteSpace(frameArg)
                || frameArg.Equals("all", StringComparison.OrdinalIgnoreCase)
                || frameArg.Equals("*", StringComparison.OrdinalIgnoreCase))
            {
                Frames.Clear();
                droppedId = "all";
                return true;
            }

            var hit = FindUnlocked(frameArg);
            if (hit is null)
                return false;
            Frames.RemoveAll(f => f.Id == hit.Id);
            droppedId = hit.Id;
            return true;
        }
    }

    public static void ClearAll()
    {
        lock (Gate)
            Frames.Clear();
    }

    public static object SceneCard(int previewMax = PreviewLong)
    {
        lock (Gate)
        {
            var frames = Frames.Select((f, i) => new
            {
                id = f.Id,
                current = i == 0,
                index = i,
                kind = f.Kind,
                chars = f.Text.Length,
                from = f.From,
                at = f.AtUtc.ToString("o"),
                preview = Preview(f.Text, i == 0 ? previewMax : PreviewShort)
            }).ToArray();

            return new
            {
                empty = Frames.Count == 0,
                count = Frames.Count,
                current = Frames.Count > 0 ? Frames[0].Id : null,
                frames
            };
        }
    }

    public static (int Count, string? CurrentId, int Chars, string? From, string Preview)? LocusPulse()
    {
        lock (Gate)
        {
            if (Frames.Count == 0)
                return null;
            var c = Frames[0];
            return (Frames.Count, c.Id, c.Text.Length, c.From, Preview(c.Text, 160));
        }
    }

    public static object Summary()
    {
        lock (Gate)
        {
            if (Frames.Count == 0)
                return new { empty = true, count = 0, frames = Array.Empty<object>() };
            var c = Frames[0];
            return new
            {
                empty = false,
                count = Frames.Count,
                current = c.Id,
                chars = c.Text.Length,
                from = c.From,
                preview = Preview(c.Text, PreviewShort),
                frames = Frames.Select(f => new { id = f.Id, chars = f.Text.Length, kind = f.Kind }).ToArray()
            };
        }
    }

    static Frame? FindUnlocked(string? frameArg)
    {
        if (Frames.Count == 0)
            return null;
        if (string.IsNullOrWhiteSpace(frameArg))
            return Frames[0];

        var raw = frameArg.Trim();
        if (raw.Equals("current", StringComparison.OrdinalIgnoreCase) || raw is "0" or "^")
            return Frames[0];

        if (raw.StartsWith('c') || raw.StartsWith('C'))
        {
            var hit = Frames.FirstOrDefault(f =>
                f.Id.Equals(raw, StringComparison.OrdinalIgnoreCase));
            if (hit is not null)
                return hit;
        }

        if (int.TryParse(raw, out var idx) && idx >= 0 && idx < Frames.Count)
            return Frames[idx];

        return Frames.FirstOrDefault(f =>
            f.Id.Equals(raw, StringComparison.OrdinalIgnoreCase));
    }

    public static string Preview(string text, int max)
    {
        var one = text.Replace("\r\n", "\n").Replace('\r', '\n');
        if (one.Length <= max)
            return one;
        return one[..max] + "\n…";
    }
}
