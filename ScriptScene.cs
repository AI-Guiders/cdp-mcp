#nullable enable
using System.Collections.Concurrent;
using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;
using CdpMcp.Backends;

namespace CdpMcp;

/// <summary>
/// Script habitat: put → edit in buffer (diagnostics) → check/run → report.
/// Same comfort as put for files — not throwaway external CSX.
/// </summary>
internal static class ScriptScene
{
    public const string Schema = "script_scene/v0";
    public const string ToolName = "cdp_script_scene";

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
    static readonly ConcurrentDictionary<string, LastRun> LastByRoot = new(StringComparer.OrdinalIgnoreCase);

    public static bool IsScriptTool(string name) =>
        string.Equals(name, ToolName, StringComparison.OrdinalIgnoreCase);

    public static async Task<string> DispatchAsync(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, ICdpBackendModule> byDomain,
        IReadOnlyDictionary<string, JsonElement> args,
        Func<string, IReadOnlyDictionary<string, JsonElement>, CancellationToken, Task<string>> metaDispatch,
        CancellationToken ct = default)
    {
        var op = (OptString(args, "op") ?? OptString(args, "feature") ?? "scene").Trim().ToLowerInvariant();
        return op switch
        {
            "" or "scene" or "map" or "status" => SceneMap(session),
            "put" or "new" or "create" => await PutAsync(store, session, byDomain, args, ct).ConfigureAwait(false),
            "open" => await OpenAsync(store, session, byDomain, args, ct).ConfigureAwait(false),
            "check" or "compile" => await CheckAsync(store, session, byDomain, args, ct).ConfigureAwait(false),
            "run" or "dry_run" or "dryrun" => await RunAsync(store, session, args, metaDispatch, ct).ConfigureAwait(false),
            "last" or "report" => Last(session),
            "help" => Help(args),
            _ => Err("unknown_op", op, "op=scene|put|open|check|run|last|help")
        };
    }

    static string SceneMap(SessionContext session)
    {
        var root = ScriptsRoot(session);
        var hasProject = session.ProjectRoot is { Length: > 0 };
        var scripts = ListScripts(root);
        LastByRoot.TryGetValue(SessionKey(session), out var last);

        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            scene = "script",
            pulse = !hasProject
                ? "no project — cdp_open first"
                : scripts.Length == 0
                    ? "scripts dir ready — put a .csx"
                    : $"{scripts.Length} script(s)",
            scripts_root = root,
            scripts,
            last = last is null ? null : new { last.Path, last.Mode, last.Ok, last.AtUtc },
            kinds = new object[]
            {
                new { id = "csx", title = "CSX (ScriptGlobals)", status = "live" },
                new { id = "yaml", title = "YAML plans", status = "planned" }
            },
            next = hasProject
                ? Next(
                    ("script_put", "Put script", "name= + text= → .cdp/scripts/*.csx"),
                    ("script_check", "Check", "allowlist compile + anchors"),
                    ("script_run", "Run", "path= or open buffer"),
                    ("script_last", "Last report", "previous check/run"))
                : Next(("project_scene", "Open project", "cdp_open first")),
            hint =
                "Habitat: put → edit buffer (diagnostics) → check → run → report. " +
                "Not put-and-pray. Scripts under .cdp/scripts/. YAML kind later."
        }, Pretty);
    }

    static async Task<string> PutAsync(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, ICdpBackendModule> byDomain,
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken ct)
    {
        if (session.ProjectRoot is not { Length: > 0 })
            return Err("no_project", "put", "cdp_open first");

        var name = Path.GetFileName((OptString(args, "name") ?? OptString(args, "file") ?? "script").Trim());
        if (!name.EndsWith(".csx", StringComparison.OrdinalIgnoreCase))
            name += ".csx";

        var root = ScriptsRoot(session)!;
        Directory.CreateDirectory(root);
        var full = Path.Combine(root, name);
        var overwrite = BoolOr(args, "overwrite", File.Exists(full));
        if (File.Exists(full) && !overwrite)
        {
            return JsonSerializer.Serialize(new
            {
                schema = Schema,
                ok = false,
                op = "put",
                error = "file_exists",
                path = full,
                hint = "overwrite=true or pick another name="
            }, Pretty);
        }

        var text = OptString(args, "text") ?? OptString(args, "body") ?? OptString(args, "code")
            ?? """
            // CDP script — edit in buffer, then go=script_check / script_run
            await Help.Of("Symbol");
            """;

        var buf = store.Create(full, text.Replace("\r\n", "\n"), overwrite: true);
        var wire = RelWire(session, full);
        var diags = await TryBufferDiagnosticsAsync(store, session, byDomain, full, ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            op = "put",
            path = full,
            anchor = wire,
            meta = buf.ToMeta(),
            diagnostics = diags,
            land = new
            {
                anchor = wire,
                doc_id = buf.DocId,
                start_line = 1,
                end_line = Math.Min(12, LineCount(buf.Text)),
                text = string.Join("\n", buf.Text.Replace("\r\n", "\n").Split('\n').Take(12))
            },
            next = Next(
                ("edit_draft", "Edit in IDE", "diagnostics on buffer — not pray"),
                ("script_check", "Check CSX", "allowlist compile"),
                ("script_run", "Run", "after green check"),
                ("diagnostics", "Buffer diagnostics", "syntax on open buffer")),
            hint = "Draft in .cdp/scripts. Refine with buffer edit/diagnostics, then check/run."
        }, Pretty);
    }

    static async Task<string> OpenAsync(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, ICdpBackendModule> byDomain,
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken ct)
    {
        if (!TryResolveScriptPath(store, session, args, out var full, out var err))
            return Err(err, "open", "path= / name= under .cdp/scripts or open buffer");

        var buf = store.Open(full, refresh: BoolOr(args, "refresh", false));
        var diags = await TryBufferDiagnosticsAsync(store, session, byDomain, full, ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            op = "open",
            path = full,
            anchor = RelWire(session, full),
            meta = buf.ToMeta(),
            diagnostics = diags,
            next = Next(
                ("edit_draft", "Edit", "buffer ready"),
                ("script_check", "Check", "compile"),
                ("script_run", "Run", "execute")),
            hint = "Opened in buffer — Instant Save on edit; diagnostics in-result."
        }, Pretty);
    }

    static async Task<string> CheckAsync(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, ICdpBackendModule> byDomain,
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken ct)
    {
        if (!TryResolveScriptPath(store, session, args, out var full, out var err))
            return Err(err, "check", null);

        FlushIfOpen(store, full);
        var code = await File.ReadAllTextAsync(full, ct).ConfigureAwait(false);
        var report = await ScriptHost.CheckAsync(code, ct).ConfigureAwait(false);
        Remember(session, full, "check", report.Ok);

        var rel = Rel(session, full);
        var anchors = (report.DiagnosticItems ?? []).Select(d =>
        {
            var wire = d.Anchor is { Length: > 0 }
                ? d.Anchor.Replace(CsxDiagnosticProjection.ScriptFileToken, rel, StringComparison.Ordinal)
                : d.Line is int L ? $"[F:{rel}; L:{L}]" : RelWire(session, full);
            return new
            {
                anchor = wire,
                d.Line,
                d.Column,
                severity = d.Severity,
                id = d.Id,
                message = d.Message,
                hint = d.Hint
            };
        }).ToArray();

        var bufferDiags = await TryBufferDiagnosticsAsync(store, session, byDomain, full, ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = report.Ok,
            op = "check",
            path = full,
            anchor = RelWire(session, full),
            diagnostics = anchors,
            count = anchors.Length,
            buffer_diagnostics = bufferDiags,
            next = report.Ok
                ? Next(
                    ("script_run", "Run", "check green"),
                    ("edit_draft", "Tweak", "still in buffer"))
                : Next(
                    ("peek", "Peek error", "wire= from diagnostics[].anchor"),
                    ("edit_draft", "Fix in IDE", "then check again"),
                    ("script_check", "Re-check", "after edit")),
            hint = "CSX allowlist compile. Fix via buffer — not another put unless rewrite."
        }, Pretty);
    }

    static async Task<string> RunAsync(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args,
        Func<string, IReadOnlyDictionary<string, JsonElement>, CancellationToken, Task<string>> metaDispatch,
        CancellationToken ct)
    {
        if (!TryResolveScriptPath(store, session, args, out var full, out var err))
            return Err(err, "run", null);

        FlushIfOpen(store, full);
        var mode = (OptString(args, "mode") ?? "run").Trim();
        if (string.Equals(OptString(args, "op"), "dry_run", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "dryrun", StringComparison.OrdinalIgnoreCase))
            mode = "dry_run";

        var mapped = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["path"] = JsonSerializer.SerializeToElement(full),
            ["mode"] = JsonSerializer.SerializeToElement(mode)
        };
        if (session.ProjectRoot is { Length: > 0 } pr)
            mapped["workspace_path"] = JsonSerializer.SerializeToElement(pr);

        string raw;
        try
        {
            raw = await metaDispatch("cdp_csx_run", mapped, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Remember(session, full, mode, ok: false);
            return JsonSerializer.Serialize(new
            {
                schema = Schema,
                ok = false,
                op = "run",
                path = full,
                error = "run_failed",
                message = ex.Message
            }, Pretty);
        }

        var ok = true;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("ok", out var okEl))
                ok = okEl.ValueKind != JsonValueKind.False;
        }
        catch
        {
            // keep ok true if unparseable — raw still returned
        }

        Remember(session, full, mode, ok);
        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok,
            op = "run",
            mode,
            path = full,
            anchor = RelWire(session, full),
            report = JsonSerializer.Deserialize<JsonElement>(raw),
            next = Next(
                ("script_last", "Report", "stored last"),
                ("script_check", "Re-check", "if failed"),
                ("edit_draft", "Edit", "iterate in buffer"),
                ("script_run", "Rerun", "same path")),
            hint = "Ran in session. Iterate in buffer — not put-and-pray."
        }, Pretty);
    }

    static string Last(SessionContext session)
    {
        if (!LastByRoot.TryGetValue(SessionKey(session), out var last))
            return Err("no_last_run", "last", "check or run first");

        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            op = "last",
            path = last.Path,
            mode = last.Mode,
            succeeded = last.Ok,
            at_utc = last.AtUtc,
            next = Next(
                ("script_run", "Rerun", "same script"),
                ("script_open", "Open", "back to buffer")),
            hint = "Last check/run pulse for this project session."
        }, Pretty);
    }

    static string Help(IReadOnlyDictionary<string, JsonElement> args)
    {
        var path = OptString(args, "path") ?? OptString(args, "of");
        return string.IsNullOrWhiteSpace(path)
            ? CsxHelpCatalog.Toc(48)
            : CsxHelpCatalog.Of(path!, 40);
    }

    static async Task<object?> TryBufferDiagnosticsAsync(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, ICdpBackendModule> byDomain,
        string full,
        CancellationToken ct)
    {
        try
        {
            var diagArgs = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["op"] = JsonSerializer.SerializeToElement("diagnostics"),
                ["path"] = JsonSerializer.SerializeToElement(full),
                ["force"] = JsonSerializer.SerializeToElement(true)
            };
            var raw = await DocumentEditPlane.DispatchAsync(
                    "cdp_buffer", store, session, byDomain, diagArgs, ct)
                .ConfigureAwait(false);
            return JsonSerializer.Deserialize<JsonElement>(raw);
        }
        catch
        {
            return null;
        }
    }

    static void FlushIfOpen(DocumentBufferStore store, string full)
    {
        var buf = store.All.FirstOrDefault(b =>
            string.Equals(b.Path, full, StringComparison.OrdinalIgnoreCase));
        if (buf is { Dirty: true })
            store.Flush(buf, allowShrink: true);
    }

    static bool TryResolveScriptPath(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args,
        out string full,
        out string error)
    {
        full = "";
        error = "path_required";
        var pathArg = OptString(args, "path") ?? OptString(args, "file");
        var name = OptString(args, "name");

        if (pathArg is { Length: > 0 })
        {
            full = Path.IsPathRooted(pathArg)
                ? Path.GetFullPath(pathArg)
                : Path.GetFullPath(Path.Combine(
                    session.ProjectRoot ?? ScriptsRoot(session) ?? ".", pathArg));
            if (!File.Exists(full) && ScriptsRoot(session) is { } sr)
            {
                var alt = Path.Combine(sr, Path.GetFileName(pathArg));
                if (File.Exists(alt)) full = alt;
            }
        }
        else if (name is { Length: > 0 } && ScriptsRoot(session) is { } root)
        {
            var fn = Path.GetFileName(name);
            if (!fn.EndsWith(".csx", StringComparison.OrdinalIgnoreCase)) fn += ".csx";
            full = Path.Combine(root, fn);
        }
        else if (store.All.FirstOrDefault(b =>
                     b.Path.EndsWith(".csx", StringComparison.OrdinalIgnoreCase)) is { } csx)
        {
            full = csx.Path;
        }

        if (full.Length == 0 || !File.Exists(full))
        {
            error = full.Length == 0 ? "path_required" : "not_found";
            return false;
        }

        return true;
    }

    static string? ScriptsRoot(SessionContext session) =>
        session.ProjectRoot is { Length: > 0 } pr ? Path.Combine(pr, ".cdp", "scripts") : null;

    static object[] ListScripts(string? root)
    {
        if (root is null || !Directory.Exists(root))
            return [];
        return Directory.EnumerateFiles(root, "*.csx")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Take(40)
            .Select(p => (object)new
            {
                name = Path.GetFileName(p),
                path = p,
                mtime_utc = File.GetLastWriteTimeUtc(p)
            })
            .ToArray();
    }

    static void Remember(SessionContext session, string path, string mode, bool ok) =>
        LastByRoot[SessionKey(session)] = new LastRun(path, mode, ok, DateTime.UtcNow);

    static string SessionKey(SessionContext session) =>
        session.ProjectRoot ?? session.ScmRoot ?? "_";

    static string Rel(SessionContext session, string abs)
    {
        var root = session.ProjectRoot ?? session.ScmRoot;
        if (root is null) return abs.Replace('\\', '/');
        try
        {
            var r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var a = Path.GetFullPath(abs);
            if (a.StartsWith(r, StringComparison.OrdinalIgnoreCase))
                return a[r.Length..].TrimStart('\\', '/').Replace('\\', '/');
        }
        catch { /* keep abs */ }

        return abs.Replace('\\', '/');
    }

    static string RelWire(SessionContext session, string abs) => $"[F:{Rel(session, abs)}]";

    static int LineCount(string text) =>
        text.Replace("\r\n", "\n").Split('\n').Length;

    static object[] Next(params (string go, string label, string why)[] items) =>
        items.Select(i => (object)new { go = i.go, label = i.label, why = i.why }).ToArray();

    static string Err(string error, string? op, string? hint) =>
        JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = false,
            op,
            error,
            hint
        }, Pretty);

    static string? OptString(IReadOnlyDictionary<string, JsonElement> args, string key) =>
        args.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    static bool BoolOr(IReadOnlyDictionary<string, JsonElement> args, string key, bool fallback)
    {
        if (!args.TryGetValue(key, out var el)) return fallback;
        if (el.ValueKind is JsonValueKind.True) return true;
        if (el.ValueKind is JsonValueKind.False) return false;
        if (el.ValueKind == JsonValueKind.String && bool.TryParse(el.GetString(), out var b)) return b;
        return fallback;
    }

    sealed record LastRun(string Path, string Mode, bool Ok, DateTime AtUtc);
}
