#nullable enable

namespace CdpMcp;

/// <summary>RouteOne family gate: Doc — peel method_lines off RouteOne.</summary>
internal static partial class CitizenIntentRouter
{
    static Route? TryRouteDoc(string raw)
    {
        if (raw.Equals("replace_all", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("replace_all ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("replace_all path=", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("replaceall", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("replaceall ", StringComparison.OrdinalIgnoreCase))
        {
            return CitizenBufferReplaceAll.Route(raw);
        }

        if (raw.Equals("put", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("put ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("put path=", StringComparison.OrdinalIgnoreCase))
        {
            return CitizenBufferPut.Route(raw);
        }

        if (raw.Equals("scratch", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("scratch ", StringComparison.OrdinalIgnoreCase))
        {
            return CitizenBufferScratch.Route(raw);
        }

        if (raw.Equals("take", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("take ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("take path=", StringComparison.OrdinalIgnoreCase))
        {
            return CitizenBufferTake.Route(raw);
        }

        if (raw.Equals("share", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("share ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("share path=", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("share with=", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("share from=", StringComparison.OrdinalIgnoreCase))
        {
            return CitizenBufferShare.Route(raw);
        }

        if (raw.Equals("scope", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("scope ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("scope from=", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("scope_clear", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("scope_clear", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("sniper", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("sniper ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("peek", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("peek ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("peek wire=", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("peek pad=", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("aim", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("aim ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("aim wire=", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("target", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("target ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("outline", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("outline ", StringComparison.OrdinalIgnoreCase))
        {
            return CitizenBufferSniper.Route(raw);
        }

        if (raw.Equals("reload", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("reload ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("reload path=", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("keep_disk", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("keep_disk ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("keep_disk path=", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("disk_peek", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("disk_peek ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("disk_peek path=", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("diskpeek", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("diskpeek ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("peek_disk", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("peek_disk ", StringComparison.OrdinalIgnoreCase))
        {
            return CitizenBufferDisk.Route(raw);
        }

        if (raw.Equals("read", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("read ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("read path=", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("close", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("close ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("close path=", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("buffers", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("buffers ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("doc_scene", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("doc_scene", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("buffer_scene", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("buffer_scene", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("buffer", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("buffer ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("doc_read", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("doc_close", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("buffer_read", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("buffer_close", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("doc_diagnostics", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("buffer_diagnostics", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("buf_diagnostics", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("doc_open", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("buffer_open", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("buf_diags", StringComparison.OrdinalIgnoreCase))
        {
            return CitizenBufferBuffer.Route(raw);
        }

        if (raw.StartsWith("replace ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("replace path=", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseReplace(raw, out var path, out var oldString, out var newString, out var reason))
                return new Route(Verb.Unknown, raw, Ok: false, Reason: reason);
            return new Route(
                Verb.Replace,
                raw,
                Ok: true,
                Path: path,
                OldString: oldString,
                NewString: newString,
                Go: "buffer");
        }

        if (raw.StartsWith("create ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("create path=", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("write ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("write path=", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseCreate(raw, out var path, out var body, out var overwrite, out var reason))
                return new Route(Verb.Unknown, raw, Ok: false, Reason: reason);
            return new Route(
                Verb.Create,
                raw,
                Ok: true,
                Path: path,
                NewString: body,
                Op: overwrite ? "overwrite" : null,
                Go: "buffer");
        }

        if (raw.StartsWith("append ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("append path=", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseAppend(raw, out var path, out var body, out var reason))
                return new Route(Verb.Unknown, raw, Ok: false, Reason: reason);
            return new Route(
                Verb.Append,
                raw,
                Ok: true,
                Path: path,
                NewString: body,
                Go: "buffer");
        }

        if (raw.StartsWith("delete ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("delete path=", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("rm ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("rm path=", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("remove ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("remove path=", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseDelete(raw, out var path, out var force, out var reason))
                return new Route(Verb.Unknown, raw, Ok: false, Reason: reason);
            return new Route(
                Verb.Delete,
                raw,
                Ok: true,
                Path: path,
                Op: force ? "force" : null,
                Go: "buffer");
        }

        if (raw.Equals("build", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("build ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("build path=", StringComparison.OrdinalIgnoreCase))
        {
            var path = ExtractLifecyclePath(raw, "build");
            return new Route(Verb.Build, raw, Ok: true, Path: path, Go: "build");
        }

        return null;
    }
}
