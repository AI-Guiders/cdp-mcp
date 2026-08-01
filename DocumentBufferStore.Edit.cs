namespace CdpMcp;

/// <summary>In-buffer Apply* + Flush (≤ADX soft-warn peel).</summary>
internal sealed partial class DocumentBufferStore
{
    public void ApplySetText(DocBuffer buf, string text)
    {
        buf.Text = text ?? "";
        buf.Version++;
        buf.Dirty = true;
    }

    public void ApplyReplace(DocBuffer buf, string oldString, string newString)
    {
        if (string.IsNullOrEmpty(oldString))
            throw new ArgumentException("old_string is required for replace.");

        if (!TryFindUniqueSpan(buf.Text, oldString, out var idx, out var matchedLen))
        {
            // Distinguish not-found vs ambiguous for agent recovery.
            var exact = buf.Text.IndexOf(oldString, StringComparison.Ordinal);
            if (exact < 0
                && buf.Text.IndexOf(NormalizeNewlines(oldString, "\n"), StringComparison.Ordinal) < 0
                && buf.Text.IndexOf(NormalizeNewlines(oldString, "\r\n"), StringComparison.Ordinal) < 0)
                throw new ArgumentException("old_string not found in buffer.");
            throw new ArgumentException("old_string is not unique in buffer; narrow the span.");
        }

        var insertion = AdaptNewlinesToBuffer(newString ?? "", buf.Text);
        buf.Text = string.Concat(buf.Text.AsSpan(0, idx), insertion, buf.Text.AsSpan(idx + matchedLen));
        buf.Version++;
        buf.Dirty = true;
    }

    /// <summary>
    /// Exact Ordinal match first; if miss, retry with LF/CRLF variants of <paramref name="needle"/>
    /// so agents can pass \\n while the buffer still has Windows CRLF.
    /// </summary>
    static bool TryFindUniqueSpan(string haystack, string needle, out int index, out int matchedLength)
    {
        index = -1;
        matchedLength = 0;
        string[] candidates =
        [
            needle,
            NormalizeNewlines(needle, "\n"),
            NormalizeNewlines(needle, "\r\n")
        ];

        foreach (var candidate in candidates.Distinct(StringComparer.Ordinal))
        {
            if (candidate.Length == 0)
                continue;
            var first = haystack.IndexOf(candidate, StringComparison.Ordinal);
            if (first < 0)
                continue;
            var second = haystack.IndexOf(candidate, first + candidate.Length, StringComparison.Ordinal);
            if (second >= 0)
                return false;
            index = first;
            matchedLength = candidate.Length;
            return true;
        }

        return false;
    }

    static string NormalizeNewlines(string text, string eol) =>
        (text ?? "").Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Replace("\n", eol, StringComparison.Ordinal);

    static string AdaptNewlinesToBuffer(string text, string bufferSample)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('\n') < 0 && text.IndexOf('\r') < 0)
            return text;
        var eol = bufferSample.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        return NormalizeNewlines(text, eol);
    }

    /// <summary>1-based line/column; end exclusive on end position (like LSP range).</summary>
    public void ApplyReplaceRange(
        DocBuffer buf,
        int startLine,
        int startColumn,
        int endLine,
        int endColumn,
        string text)
    {
        if (startLine < 1 || startColumn < 1 || endLine < 1 || endColumn < 1)
            throw new ArgumentException("line/column are 1-based and must be >= 1.");
        var start = OffsetOf(buf.Text, startLine, startColumn);
        var end = OffsetOf(buf.Text, endLine, endColumn);
        if (end < start)
            throw new ArgumentException("end position is before start.");
        buf.Text = string.Concat(buf.Text.AsSpan(0, start), text ?? "", buf.Text.AsSpan(end));
        buf.Version++;
        buf.Dirty = true;
    }

    public void Flush(DocBuffer buf, bool allowShrink = false) =>
        _gate.Run(buf.Path, () => FlushUnlocked(buf, allowShrink));

    /// <summary>
    /// Flush without taking the gate — caller must already hold <see cref="MutateAsync"/> for this path.
    /// Policy (no magic size thresholds): shrinking an existing file on disk requires explicit
    /// <paramref name="allowShrink"/> — full rewrites that get shorter are intent, not heuristics.
    /// </summary>
    internal void FlushUnlocked(DocBuffer buf, bool allowShrink = false)
    {
        if (File.Exists(buf.Path) && !allowShrink)
        {
            var diskLen = new FileInfo(buf.Path).Length;
            var bodyLen = buf.Text.Length;
            if (bodyLen < diskLen)
            {
                throw new InvalidOperationException(
                    $"Refusing shrink flush of '{buf.Path}' ({diskLen} → {bodyLen} bytes). " +
                    "Pass allow_shrink=true when the shorter body is intentional " +
                    "(or use create overwrite=true / delete outside the buffer).");
            }
        }

        AtomicTextFile.WriteUtf8(buf.Path, buf.Text);
        buf.Dirty = false;
        buf.DiskMtimeUtc = File.GetLastWriteTimeUtc(buf.Path);
        DocumentDiskSyncLatch.Publish(buf.Path, DocumentDiskSyncLatch.OriginAgent);
    }
}
