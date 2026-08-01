using System.Text;

namespace CdpMcp;

internal sealed class DocBuffer
{
    public required string DocId { get; init; }
    public required string Path { get; init; }
    public required string Text { get; set; }
    public int Version { get; set; }
    public bool Dirty { get; set; }
    public required string Language { get; set; }
    public DateTime DiskMtimeUtc { get; set; }
    public string? LastDiagnosticsJson { get; set; }
    public DateTime? LastDiagnosedUtc { get; set; }
    /// <summary>Buffer version when <see cref="LastDiagnosticsJson"/> was computed.</summary>
    public int? LastDiagnosedVersion { get; set; }
    public string? LastDiagnosedScope { get; set; }

        public object ToMeta()
    {
        var changed = ProbeMaterialDiskChanged(out var diskNow, out var reason);
        return new
        {
            doc_id = DocId,
            path = Path,
            language = Language,
            version = Version,
            dirty = Dirty,
            line_count = CountLines(Text),
            char_count = Text.Length,
            disk_mtime_utc = DiskMtimeUtc,
            disk_now_utc = diskNow,
            disk_changed = changed,
            disk_changed_reason = reason,
            last_diagnosed_utc = LastDiagnosedUtc
        };
    }

    /// <summary>
    /// VS-style "File Modified Outside the Environment": disk mtime (or presence)
    /// differs from last buffer sync.
    /// </summary>
    public bool ProbeDiskChanged(out DateTime? diskNow, out string? reason)
    {
        diskNow = null;
        reason = null;
        try
        {
            if (!File.Exists(Path))
            {
                reason = "missing_on_disk";
                return true;
            }

            diskNow = File.GetLastWriteTimeUtc(Path);
            if (diskNow.Value != DiskMtimeUtc)
            {
                reason = "mtime";
                return true;
            }

            return false;
        }
        catch (Exception)
        {
            reason = "probe_failed";
            return true;
        }
    }

    /// <summary>
    /// SA/desk material drift: missing/content differ.
    /// Mtime-only with identical text stamps disk mtime (no false WARN after git checkout).
    /// </summary>
    public bool ProbeMaterialDiskChanged(out DateTime? diskNow, out string? reason)
    {
        if (!ProbeDiskChanged(out diskNow, out reason))
            return false;
        if (reason is "missing_on_disk" or "probe_failed")
            return true;

        try
        {
            if (!File.Exists(Path))
            {
                reason = "missing_on_disk";
                return true;
            }

            var diskText = File.ReadAllText(Path);
            if (string.Equals(Text, diskText, StringComparison.Ordinal))
            {
                AcknowledgeDisk();
                diskNow = DiskMtimeUtc;
                reason = null;
                return false;
            }

            reason = "content";
            return true;
        }
        catch (Exception)
        {
            reason = "probe_failed";
            return true;
        }
    }

    /// <summary>Silence drift without taking disk (Don't Reload).</summary>
    public void AcknowledgeDisk()
    {
        if (File.Exists(Path))
            DiskMtimeUtc = File.GetLastWriteTimeUtc(Path);
    }

    /// <summary>Glance: memory vs on-disk text (not a full unified dump).</summary>
    public object PeekDisk(int pad = 2, int maxHunkLines = 24)
    {
        pad = Math.Clamp(pad, 0, 8);
        maxHunkLines = Math.Clamp(maxHunkLines, 4, 80);
        var changed = ProbeDiskChanged(out var diskNow, out var reason);
        if (!File.Exists(Path))
        {
            return new
            {
                schema = "doc_disk_peek/v0",
                path = Path,
                doc_id = DocId,
                disk_changed = changed,
                disk_changed_reason = reason ?? "missing_on_disk",
                content_same = false,
                missing_on_disk = true,
                dirty = Dirty,
                disk_mtime_utc = DiskMtimeUtc,
                disk_now_utc = diskNow
            };
        }

        var diskText = File.ReadAllText(Path);
        var contentSame = string.Equals(Text, diskText, StringComparison.Ordinal);
        if (contentSame)
        {
            if (changed)
                AcknowledgeDisk();
            return new
            {
                schema = "doc_disk_peek/v0",
                path = Path,
                doc_id = DocId,
                disk_changed = false,
                disk_changed_reason = (string?)null,
                content_same = true,
                missing_on_disk = false,
                dirty = Dirty,
                pulse = changed ? "mtime drifted, content same — stamped" : "in sync",
                disk_mtime_utc = DiskMtimeUtc,
                disk_now_utc = DiskMtimeUtc,
                mem_lines = CountLines(Text),
                disk_lines = CountLines(diskText)
            };
        }

        var mem = SplitLines(Text);
        var disk = SplitLines(diskText);
        var first = 0;
        var lim = Math.Min(mem.Count, disk.Count);
        while (first < lim && mem[first] == disk[first])
            first++;

        var start = Math.Max(0, first - pad);
        var end = Math.Min(Math.Max(mem.Count, disk.Count), first + maxHunkLines - pad);
        var rows = new List<object>();
        for (var i = start; i < end; i++)
        {
            var m = i < mem.Count ? mem[i] : null;
            var d = i < disk.Count ? disk[i] : null;
            var mark = m == d ? " " : m is null ? "+" : d is null ? "-" : "!";
            rows.Add(new
            {
                line = i + 1,
                mark,
                mem = TrimPeek(m),
                disk = TrimPeek(d)
            });
            if (rows.Count >= maxHunkLines)
                break;
        }

        return new
        {
            schema = "doc_disk_peek/v0",
            path = Path,
            doc_id = DocId,
            disk_changed = changed,
            disk_changed_reason = reason,
            content_same = false,
            missing_on_disk = false,
            dirty = Dirty,
            pulse = Dirty
                ? "DIRTY+DISK — memory ≠ disk (reload loses buffer edits)"
                : "DISK CHANGED — memory ≠ disk",
            first_diff_line = first + 1,
            mem_lines = mem.Count,
            disk_lines = disk.Count,
            disk_mtime_utc = DiskMtimeUtc,
            disk_now_utc = diskNow,
            sample = rows,
            hint = "go=reload (take disk) | go=keep_disk (keep memory). Dirty+drift: reload drops buffer edits."
        };
    }

    static string? TrimPeek(string? s)
    {
        if (s is null)
            return null;
        return s.Length <= 160 ? s : s[..157] + "...";
    }

    public object ToReadResult(int? startLine, int? endLine)
    {
        if (startLine is null && endLine is null)
        {
            return new
            {
                schema = "doc_read/v0",
                meta = ToMeta(),
                text = Text
            };
        }

        var lines = SplitLines(Text);
        var from = Math.Max(1, startLine ?? 1);
        var to = Math.Min(lines.Count, endLine ?? lines.Count);
        if (from > to)
            throw new ArgumentException("start_line > end_line.");
        var slice = new StringBuilder();
        for (var i = from; i <= to; i++)
        {
            if (i > from) slice.Append('\n');
            slice.Append(lines[i - 1]);
        }

        return new
        {
            schema = "doc_read/v0",
            meta = ToMeta(),
            start_line = from,
            end_line = to,
            text = slice.ToString()
        };
    }

    static int CountLines(string text)
    {
        if (text.Length == 0) return 1;
        var n = 1;
        foreach (var ch in text)
        {
            if (ch == '\n') n++;
        }

        return n;
    }

    static List<string> SplitLines(string text)
    {
        var list = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n') continue;
            var len = i - start;
            if (len > 0 && text[i - 1] == '\r') len--;
            list.Add(text.Substring(start, len));
            start = i + 1;
        }

        list.Add(text[start..].TrimEnd('\r'));
        return list;
    }
}
