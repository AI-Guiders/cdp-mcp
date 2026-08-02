using System.Text;

namespace CdpMcp;

internal sealed partial class DocBuffer
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

}
