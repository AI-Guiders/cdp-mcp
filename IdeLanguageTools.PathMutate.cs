using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cdp.Core;
using Cdp.Lsp;
using Cdp.ScriptableIde;
using TypescriptLang;
using Tool = ModelContextProtocol.Protocol.Tool;

namespace CdpMcp;
internal static partial class IdeLanguageTools
{
    /// <summary>Citizen replace host-execute — open + ApplyReplace + Flush (PathMutateGate).</summary>
    public static bool TryReplaceInDocument(string path, string? projectRoot, string oldString, string newString, out string? fullPath, out string? docId, out string? error)
    {
        fullPath = null;
        docId = null;
        error = null;
        if (_docStore is null)
        {
            error = "doc_store_unbound";
            return false;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            error = "path_empty";
            return false;
        }

        if (string.IsNullOrEmpty(oldString))
        {
            error = "old_empty";
            return false;
        }

        try
        {
            var resolved = ResolveOpenPath(path.Trim(), projectRoot);
            var buf = _docStore.Open(resolved);
            _docStore.ApplyReplace(buf, oldString, newString ?? "");
            _docStore.Flush(buf, allowShrink: true);
            fullPath = buf.Path;
            docId = buf.DocId;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name + ": " + ex.Message;
            return false;
        }
    }

    /// <summary>Citizen create/write host-execute — PathMutateGate Create (not Cursor Write).</summary>
    public static bool TryCreateDocument(string path, string? projectRoot, string? body, bool overwrite, out string? fullPath, out string? docId, out string? error)
    {
        fullPath = null;
        docId = null;
        error = null;
        if (_docStore is null)
        {
            error = "doc_store_unbound";
            return false;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            error = "path_empty";
            return false;
        }

        try
        {
            var resolved = ResolveOpenPath(path.Trim(), projectRoot);
            var buf = _docStore.Create(resolved, body ?? "", overwrite);
            fullPath = buf.Path;
            docId = buf.DocId;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name + ": " + ex.Message;
            return false;
        }
    }

    /// <summary>Citizen append host-execute — open + suffix + Flush (PathMutateGate; not Cursor Write).</summary>
    public static bool TryAppendDocument(string path, string? projectRoot, string? body, out string? fullPath, out string? docId, out string? error)
    {
        fullPath = null;
        docId = null;
        error = null;
        if (_docStore is null)
        {
            error = "doc_store_unbound";
            return false;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            error = "path_empty";
            return false;
        }

        if (string.IsNullOrEmpty(body))
        {
            error = "append_body_empty";
            return false;
        }

        try
        {
            var resolved = ResolveOpenPath(path.Trim(), projectRoot);
            var buf = _docStore.Open(resolved);
            _docStore.ApplySetText(buf, buf.Text + body);
            _docStore.Flush(buf, allowShrink: true);
            fullPath = buf.Path;
            docId = buf.DocId;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name + ": " + ex.Message;
            return false;
        }
    }

    /// <summary>Citizen delete/rm host-execute — PathMutateGate Delete (not Cursor Write).</summary>
    public static bool TryDeleteDocument(string path, string? projectRoot, bool force, out string? fullPath, out string? error)
    {
        fullPath = null;
        error = null;
        if (_docStore is null)
        {
            error = "doc_store_unbound";
            return false;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            error = "path_empty";
            return false;
        }

        try
        {
            var resolved = ResolveOpenPath(path.Trim(), projectRoot);
            _docStore.Delete(resolved, force);
            fullPath = Path.GetFullPath(resolved);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name + ": " + ex.Message;
            return false;
        }
    }
}