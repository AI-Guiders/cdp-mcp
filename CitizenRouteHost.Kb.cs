#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Citizen @intent kb|memory_* — sync CallAsync on memory facets (agent-notes / findings / failures / task).</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake backend JSON; live uses <see cref="ByDomainResolver"/>.</summary>
    internal static Func<string, string, IReadOnlyDictionary<string, JsonElement>, Task<string>>? KbCallOverride { get; set; }

    static Applied RunKb(CitizenIntentRouter.Route route)
    {
        var tool = string.IsNullOrWhiteSpace(route.Op) ? "list_pack" : route.Op!;
        var facet = string.IsNullOrWhiteSpace(route.Server) ? CdpDomains.MemoryWorld : route.Server!;
        var args = BuildKbArgs(route.Raw, tool, facet);

        try
        {
            string json;
            if (KbCallOverride is { } ov)
            {
                json = ov(facet, tool, args).ConfigureAwait(false).GetAwaiter().GetResult();
            }
            else
            {
                var byDomain = ByDomainResolver?.Invoke()
                    ?? new Dictionary<string, ICdpBackendModule>(StringComparer.Ordinal);
                if (!byDomain.TryGetValue(facet, out var backend) || !backend.IsEnabled)
                {
                    return new Applied(
                        route.Raw,
                        route.Verb.ToString(),
                        Ok: false,
                        Action: "kb",
                        Go: "kb",
                        Reason: "kb_facet_disabled:" + facet);
                }

                IReadOnlyDictionary<string, JsonElement> callArgs = args;
                var session = SessionResolver?.Invoke();
                if (session is not null && MemorySessionDefaults.IsMemoryDomain(facet))
                    callArgs = MemorySessionDefaults.WithWorkspace(args, session);

                if (NeedsKbWorkspace(tool) && !HasKbWorkspace(callArgs))
                {
                    return new Applied(
                        route.Raw,
                        route.Verb.ToString(),
                        Ok: false,
                        Action: "kb",
                        Go: "kb",
                        Pulse: TruncPulse("kb " + facet + " " + tool + " need cdp_open"),
                        Reason: "kb_workspace_required · cdp_open");
                }

                if (tool is "route_context" && !HasKbStringArg(callArgs, "query"))
                {
                    return new Applied(
                        route.Raw,
                        route.Verb.ToString(),
                        Ok: false,
                        Action: "kb",
                        Go: "kb",
                        Pulse: TruncPulse("kb " + facet + " " + tool + " need query="),
                        Reason: "query is required");
                }

                if (tool is "read_card" && !HasKbStringArg(callArgs, "relative_path"))
                {
                    return new Applied(
                        route.Raw,
                        route.Verb.ToString(),
                        Ok: false,
                        Action: "kb",
                        Go: "kb",
                        Pulse: TruncPulse("kb " + facet + " " + tool + " need relative_path="),
                        Reason: "relative_path is required");
                }

                json = backend.CallAsync(tool, callArgs)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
            }

            var ok = TryReadKbOk(json, tool);
            var pulse = TryReadKbPulse(json, facet, tool);
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "kb",
                Go: "kb",
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "kb_failed"));
        }
        catch (Exception ex)
        {
            var tip = TipKbArgException(ex, out var reason);
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "kb",
                Go: "kb",
                Pulse: TruncPulse("kb " + facet + " " + tool + " " + tip),
                Reason: reason);
        }
    }

    static bool NeedsKbWorkspace(string tool) =>
        tool is "route_next" or "route_context" or "ensure_store" or "tasks" or "task_upsert"
            or "read_card" or "write_card" or "upsert_section" or "analytics_upsert"
            or "search_agent_notes" or "upsert_agent_notes_section"
            or "findings" or "finding_record" or "finding_check"
            or "failures" or "failure_record";

    static bool HasKbWorkspace(IReadOnlyDictionary<string, JsonElement> args) =>
        args.TryGetValue("workspace_path", out var el)
        && el.ValueKind == JsonValueKind.String
        && el.GetString() is { Length: > 0 };

    static bool HasKbStringArg(IReadOnlyDictionary<string, JsonElement> args, string name) =>
        args.TryGetValue(name, out var el)
        && el.ValueKind == JsonValueKind.String
        && el.GetString() is { Length: > 0 };

    /// <summary>Router-level kb preflight refuses — surface need tip so FM does not SoftFL-invent after pulse=null.</summary>
    static string? TipKbRouteNotOk(CitizenIntentRouter.Route route)
    {
        var facet = string.IsNullOrWhiteSpace(route.Server) ? "kb" : route.Server!;
        var tool = string.IsNullOrWhiteSpace(route.Op) ? "?" : route.Op!;

        if (route.Reason is "kb_tool_unknown")
        {
            // man lives on task/finding/failure — wrong facet must not SoftFL-invent "missing".
            if (tool is "man")
                return TruncPulse("kb " + facet + " man unknown · try facet=task|finding|failure");
            return TruncPulse("kb " + facet + " " + tool + " unknown");
        }

        if (route.Verb is not CitizenIntentRouter.Verb.Kb)
            return null;

        return route.Reason switch
        {
            "kb_process_id_required" => TruncPulse("kb " + facet + " " + tool + " need process_id="),
            "kb_procedure_id_required" => TruncPulse("kb " + facet + " " + tool + " need procedure_id="),
            "kb_file_path_required" => TruncPulse("kb " + facet + " " + tool + " need file_path="),
            _ => null
        };
    }

    /// <summary>Honest fail tip — do not SoftFL-invent <c>need cdp_open</c> for every ArgumentException.</summary>
    static string TipKbArgException(Exception ex, out string reason)
    {
        reason = ex.GetType().Name + ": " + ex.Message;
        if (ex is not ArgumentException)
            return "failed";

        if (ex.Message.Contains("workspace_path", StringComparison.OrdinalIgnoreCase))
        {
            reason = "kb_workspace_required · cdp_open";
            return "need cdp_open";
        }

        if (ex.Message.Contains("query", StringComparison.OrdinalIgnoreCase))
            return "need query=";

        if (ex.Message.Contains("relative_path", StringComparison.OrdinalIgnoreCase))
            return "need relative_path=";

        if (ex.Message.Contains("analytics_id", StringComparison.OrdinalIgnoreCase))
            return "need analytics_id=";

        if (ex.Message.Contains("section_id", StringComparison.OrdinalIgnoreCase))
            return "need section_id=";

        // finding_check / finding_record — "path is required" (not relative_path / workspace_path).
        if (ex.Message.Contains("path is required", StringComparison.OrdinalIgnoreCase))
            return "need path=";

        // failure_record — "tool is required".
        if (ex.Message.Contains("tool is required", StringComparison.OrdinalIgnoreCase))
            return "need tool=";

        return "failed";
    }


    /// <summary>
    /// AN <c>read_knowledge_file</c> / facet <c>man</c> return raw text (not lifecycle JSON).
    /// Missing → empty string → fail; non-empty body → ok.
    /// </summary>
    static bool TryReadKbOk(string json, string tool)
    {
        if (TryReadLifecycleOk(json))
            return true;

        if (tool is not ("read_knowledge_file" or "list_knowledge_files" or "man"))
            return false;

        if (string.IsNullOrWhiteSpace(json))
            return false;

        var trim = json.AsSpan().TrimStart();
        if (trim.StartsWith('{'))
            return false; // structured outline/error already failed lifecycle

        return true;
    }

    static Dictionary<string, JsonElement> BuildKbArgs(string raw, string tool, string facet)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var key in KbArgKeys)
        {
            var val = ExtractMcpKeyed(raw, key);
            if (val is { Length: > 0 })
                args[key] = JsonSerializer.SerializeToElement(val);
        }

        if (!args.ContainsKey("definition_id")
            && ExtractMcpKeyed(raw, "id") is { Length: > 0 } id
            && tool is "get_definition")
        {
            args["definition_id"] = JsonSerializer.SerializeToElement(id);
        }

        if (tool is "get_definition" && !args.ContainsKey("definition_id"))
            args["definition_id"] = JsonSerializer.SerializeToElement("debug-radius");

        if (IsNotesFacet(facet)
            && (tool is "get_definition" or "list_pack" or "get_process" or "get_procedure" or "radius_gate_check")
            && !args.ContainsKey("pack_id") && !args.ContainsKey("pack_path"))
        {
            args["pack_id"] = JsonSerializer.SerializeToElement(
                string.Equals(facet, CdpDomains.MemorySkill, StringComparison.Ordinal)
                    ? "agent-operations-cdp"
                    : "epistemic-scene");
        }

        if (!args.ContainsKey("query") && args.TryGetValue("q", out var qEl))
            args["query"] = qEl;

        return args;
    }

    static bool IsNotesFacet(string facet) =>
        // Project roots rarely share world pack ids — do not silent-inject epistemic-scene.
        facet is CdpDomains.MemoryWorld or CdpDomains.MemorySkill;

    static readonly string[] KbArgKeys =
    [
        "pack_id", "pack_path", "definition_id", "process_id", "procedure_id",
        "file_path", "subdir", "path", "claim", "delta_radius",
        "radius_before", "radius_after", "workspace_path", "query", "q",
        "task_id", "relative_path", "section_id", "content", "status",
        "limit", "title", "summary", "tool", "error_or_miss"
    ];

    static string? TryReadKbPulse(string json, string facet, string tool)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var bits = new List<string> { "kb", facet, tool };
            if (root.TryGetProperty("ok", out var okEl))
                bits.Add(okEl.ValueKind == JsonValueKind.True ? "ok" : "fail");
            if (root.TryGetProperty("definition_id", out var d) && d.ValueKind == JsonValueKind.String
                && d.GetString() is { Length: > 0 } def)
                bits.Add(def);
            if (root.TryGetProperty("pack_id", out var p) && p.ValueKind == JsonValueKind.String
                && p.GetString() is { Length: > 0 } pack)
                bits.Add(pack);
            if (root.TryGetProperty("count", out var c) && c.TryGetInt32(out var n))
                bits.Add(n + " hit(s)");
            if (root.TryGetProperty("pulse", out var pulseEl) && pulseEl.ValueKind == JsonValueKind.String
                && pulseEl.GetString() is { Length: > 0 } pulse)
                bits.Add(TruncPulse(pulse) ?? pulse);
            if (root.TryGetProperty("llm_cue", out var cue) && cue.ValueKind == JsonValueKind.String
                && cue.GetString() is { Length: > 0 } cueText)
                bits.Add(TruncPulse(cueText) ?? cueText);
            if (root.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String
                && title.GetString() is { Length: > 0 } t)
                bits.Add(TruncPulse(t) ?? t);
            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String
                && err.GetString() is { Length: > 0 } e)
                bits.Add(TruncPulse(e) ?? e);
            AppendKbSearchHits(root, bits);
            AppendKbNextHits(root, bits);
            AppendKbTaskHits(root, bits);
            AppendKbEntryHits(root, bits);
            AppendKbHealthBits(root, bits);
            AppendKbFileListHits(root, bits);
            AppendKbTagHits(root, bits);
            AppendKbHotContextBits(root, bits);
            AppendKbRouteContextBits(root, bits);
            AppendKbStoreMetaBits(root, bits);
            AppendKbReadCardBits(root, bits);
            AppendKbValidateBits(root, bits);
            AppendKbNormalizeBits(root, bits);
            return TruncPulse(string.Join(' ', bits));
        }
        catch
        {
            // Raw text from read_knowledge_file / facet man — short preview, not silent parse fail.
            if ((tool is "read_knowledge_file" or "man") && !string.IsNullOrWhiteSpace(json))
            {
                var one = json.Replace('\r', ' ').Replace('\n', ' ').Trim();
                if (one.Length > 72)
                    one = one[..72] + "…";
                return TruncPulse("kb " + facet + " " + tool + " body " + one);
            }

            return TruncPulse("kb " + facet + " " + tool);
        }
    }

    /// <summary>AN search_agent_notes JSON — surface top hits so FM does not SoftFL-invent after thin pulse.</summary>
    static void AppendKbSearchHits(JsonElement root, List<string> bits)
    {
        if (root.TryGetProperty("query", out var qEl) && qEl.ValueKind == JsonValueKind.String
            && qEl.GetString() is { Length: > 0 } q)
            bits.Add("q=" + (q.Length > 32 ? q[..32] + "…" : q));
        if (root.TryGetProperty("total_matches", out var tm) && tm.TryGetInt32(out var total))
            bits.Add(total + " match(es)");
        else if (root.TryGetProperty("returned_matches", out var rm) && rm.TryGetInt32(out var ret))
            bits.Add(ret + " returned");
        if (!root.TryGetProperty("matches", out var matches) || matches.ValueKind != JsonValueKind.Array)
            return;

        var hitN = 0;
        foreach (var m in matches.EnumerateArray())
        {
            if (hitN >= 2)
                break;
            string? hit = null;
            if (m.TryGetProperty("text", out var hitText) && hitText.ValueKind == JsonValueKind.String)
                hit = hitText.GetString();
            else if (m.TryGetProperty("title", out var hitTitle) && hitTitle.ValueKind == JsonValueKind.String)
                hit = hitTitle.GetString();
            else if (m.TryGetProperty("path", out var hitPath) && hitPath.ValueKind == JsonValueKind.String)
                hit = hitPath.GetString();
            if (hit is not { Length: > 0 })
                continue;
            var one = hit.Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (one.Length > 40)
                one = one[..40] + "…";
            bits.Add("#" + (hitN + 1) + " " + one);
            hitN++;
        }
    }

    /// <summary>TaskKnowledge route_next JSON — surface top toBe/title so FM does not SoftFL-invent after thin count pulse.</summary>
    static void AppendKbNextHits(JsonElement root, List<string> bits)
    {
        if (!root.TryGetProperty("next", out var next) || next.ValueKind != JsonValueKind.Array)
            return;

        var hitN = 0;
        foreach (var m in next.EnumerateArray())
        {
            if (hitN >= 2)
                break;
            string? hit = null;
            if (m.TryGetProperty("toBe", out var toBe) && toBe.ValueKind == JsonValueKind.String)
                hit = toBe.GetString();
            else if (m.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String)
                hit = title.GetString();
            else if (m.TryGetProperty("taskId", out var id) && id.ValueKind == JsonValueKind.String)
                hit = id.GetString();
            if (hit is not { Length: > 0 })
                continue;
            var one = hit.Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (one.Length > 40)
                one = one[..40] + "…";
            bits.Add("#" + (hitN + 1) + " " + one);
            hitN++;
        }
    }

    /// <summary>TaskKnowledge tasks JSON — surface top toBe/title so FM does not SoftFL-invent after thin count pulse.</summary>
    static void AppendKbTaskHits(JsonElement root, List<string> bits)
    {
        if (!root.TryGetProperty("tasks", out var tasks) || tasks.ValueKind != JsonValueKind.Array)
            return;

        var hitN = 0;
        foreach (var m in tasks.EnumerateArray())
        {
            if (hitN >= 2)
                break;
            string? hit = null;
            if (m.TryGetProperty("toBe", out var toBe) && toBe.ValueKind == JsonValueKind.String)
                hit = toBe.GetString();
            else if (m.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String)
                hit = title.GetString();
            else if (m.TryGetProperty("taskId", out var id) && id.ValueKind == JsonValueKind.String)
                hit = id.GetString();
            if (hit is not { Length: > 0 })
                continue;
            var one = hit.Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (one.Length > 40)
                one = one[..40] + "…";
            bits.Add("#" + (hitN + 1) + " " + one);
            hitN++;
        }
    }

    /// <summary>Findings/failures list JSON — surface top entry summary/path/error so FM does not SoftFL-invent after thin count pulse.</summary>
    static void AppendKbEntryHits(JsonElement root, List<string> bits)
    {
        if (!root.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
            return;

        var hitN = 0;
        foreach (var m in entries.EnumerateArray())
        {
            if (hitN >= 2)
                break;
            string? hit = null;
            if (m.TryGetProperty("summary", out var summary) && summary.ValueKind == JsonValueKind.String)
                hit = summary.GetString();
            else if (m.TryGetProperty("errorOrMiss", out var err) && err.ValueKind == JsonValueKind.String)
                hit = err.GetString();
            else if (m.TryGetProperty("why", out var why) && why.ValueKind == JsonValueKind.String)
                hit = why.GetString();
            else if (m.TryGetProperty("path", out var path) && path.ValueKind == JsonValueKind.String)
                hit = path.GetString();
            else if (m.TryGetProperty("tool", out var tool) && tool.ValueKind == JsonValueKind.String)
                hit = tool.GetString();
            else if (m.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                hit = id.GetString();
            if (hit is not { Length: > 0 })
                continue;
            var one = hit.Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (one.Length > 40)
                one = one[..40] + "…";
            bits.Add("#" + (hitN + 1) + " " + one);
            hitN++;
        }
    }

    /// <summary>AN memory_health JSON — surface health_level/hot chars so FM does not SoftFL-invent after bare tool pulse.</summary>
    static void AppendKbHealthBits(JsonElement root, List<string> bits)
    {
        if (root.TryGetProperty("health_level", out var level) && level.ValueKind == JsonValueKind.String
            && level.GetString() is { Length: > 0 } hl)
            bits.Add(hl);
        if (root.TryGetProperty("hot_context", out var hot) && hot.ValueKind == JsonValueKind.Object
            && hot.TryGetProperty("chars", out var chars) && chars.TryGetInt32(out var hotChars))
            bits.Add("hot=" + hotChars);
        if (root.TryGetProperty("recommend_compaction", out var rc) && rc.ValueKind == JsonValueKind.True)
            bits.Add("compact?");
        if (root.TryGetProperty("warnings", out var warns) && warns.ValueKind == JsonValueKind.Array)
        {
            foreach (var w in warns.EnumerateArray())
            {
                if (w.ValueKind != JsonValueKind.String || w.GetString() is not { Length: > 0 } warn)
                    continue;
                var one = warn.Length > 32 ? warn[..32] + "…" : warn;
                bits.Add(one);
                break;
            }
        }
    }

    /// <summary>AN list_knowledge_files JSON — surface total + top paths so FM does not SoftFL-invent after bare tool pulse.</summary>
    static void AppendKbFileListHits(JsonElement root, List<string> bits)
    {
        if (root.TryGetProperty("total", out var totalEl) && totalEl.TryGetInt32(out var total))
            bits.Add(total + " file(s)");
        if (!root.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
            return;

        var hitN = 0;
        foreach (var m in files.EnumerateArray())
        {
            if (hitN >= 2)
                break;
            if (m.TryGetProperty("path", out var path) && path.ValueKind == JsonValueKind.String
                && path.GetString() is { Length: > 0 } p)
            {
                var one = p.Replace('\\', '/');
                if (one.Length > 40)
                    one = "…" + one[^39..];
                bits.Add("#" + (hitN + 1) + " " + one);
                hitN++;
            }
        }
    }

    /// <summary>AN knowledge_tags inventory/lookup JSON — surface totals + top tags so FM does not SoftFL-invent after bare tool pulse.</summary>
    static void AppendKbTagHits(JsonElement root, List<string> bits)
    {
        if (root.TryGetProperty("total_tags", out var tt) && tt.TryGetInt32(out var tagTotal))
            bits.Add(tagTotal + " tag(s)");
        if (root.TryGetProperty("tagged_files", out var tf) && tf.TryGetInt32(out var tagged))
            bits.Add("files=" + tagged);

        if (root.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array)
        {
            var hitN = 0;
            foreach (var m in tags.EnumerateArray())
            {
                if (hitN >= 2)
                    break;
                if (m.TryGetProperty("tag", out var tagEl) && tagEl.ValueKind == JsonValueKind.String
                    && tagEl.GetString() is { Length: > 0 } tag)
                {
                    var one = tag.Replace('\r', ' ').Replace('\n', ' ').Trim();
                    if (one.Length > 40)
                        one = one[..40] + "…";
                    bits.Add("#" + (hitN + 1) + " " + one);
                    hitN++;
                }
            }

            return;
        }

        if (!root.TryGetProperty("hits", out var hits) || hits.ValueKind != JsonValueKind.Array)
            return;

        var pathN = 0;
        foreach (var m in hits.EnumerateArray())
        {
            if (pathN >= 2)
                break;
            string? hit = null;
            if (m.TryGetProperty("path", out var path) && path.ValueKind == JsonValueKind.String)
                hit = path.GetString();
            else if (m.TryGetProperty("file", out var file) && file.ValueKind == JsonValueKind.String)
                hit = file.GetString();
            if (hit is not { Length: > 0 })
                continue;
            var one = hit.Replace('\\', '/');
            if (one.Length > 40)
                one = "…" + one[^39..];
            bits.Add("#" + (pathN + 1) + " " + one);
            pathN++;
        }
    }

    /// <summary>AN read_hot_context JSON — surface scope + loaded section ids + content chars (not body dump) so FM does not SoftFL-invent after bare tool pulse.</summary>
    static void AppendKbHotContextBits(JsonElement root, List<string> bits)
    {
        if (root.TryGetProperty("active_scope", out var scope) && scope.ValueKind == JsonValueKind.String
            && scope.GetString() is { Length: > 0 } s)
        {
            var one = s.Length > 32 ? s[..32] + "…" : s;
            bits.Add("scope=" + one);
        }

        if (root.TryGetProperty("loaded_sections", out var loaded) && loaded.ValueKind == JsonValueKind.Array)
        {
            bits.Add(loaded.GetArrayLength() + " section(s)");
            var hitN = 0;
            foreach (var m in loaded.EnumerateArray())
            {
                if (hitN >= 2)
                    break;
                if (m.ValueKind != JsonValueKind.String || m.GetString() is not { Length: > 0 } id)
                    continue;
                var one = id.Length > 40 ? id[..40] + "…" : id;
                bits.Add("#" + (hitN + 1) + " " + one);
                hitN++;
            }
        }

        // Only when this is hot-context (not normalize_sections preview which also has content=).
        if (root.TryGetProperty("active_scope", out _) || root.TryGetProperty("loaded_sections", out _))
        {
            if (root.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String
                && content.GetString() is { } body)
                bits.Add("chars=" + body.Length);
        }
    }

    /// <summary>AN route_context JSON — surface selected hits so FM does not SoftFL-invent after thin pulse.</summary>
    static void AppendKbRouteContextBits(JsonElement root, List<string> bits)
    {
        // Discriminator vs read_hot_context (content) / search (matches).
        if (!root.TryGetProperty("selected_count", out var sc) && !root.TryGetProperty("selected", out _))
            return;

        if (sc.ValueKind != JsonValueKind.Undefined && sc.TryGetInt32(out var n))
            bits.Add(n + " selected");
        else if (root.TryGetProperty("selected", out var selArr) && selArr.ValueKind == JsonValueKind.Array)
            bits.Add(selArr.GetArrayLength() + " selected");

        if (root.TryGetProperty("resolved_scope", out var scope) && scope.ValueKind == JsonValueKind.String
            && scope.GetString() is { Length: > 0 } s)
        {
            var one = s.Length > 32 ? s[..32] + "…" : s;
            bits.Add("scope=" + one);
        }

        if (root.TryGetProperty("selected", out var selected) && selected.ValueKind == JsonValueKind.Array)
        {
            var hitN = 0;
            foreach (var m in selected.EnumerateArray())
            {
                if (hitN >= 2)
                    break;
                if (m.ValueKind != JsonValueKind.Object
                    || !m.TryGetProperty("id", out var idEl)
                    || idEl.ValueKind != JsonValueKind.String
                    || idEl.GetString() is not { Length: > 0 } id)
                    continue;
                var one = id.Length > 40 ? id[..40] + "…" : id;
                bits.Add("#" + (hitN + 1) + " " + one);
                hitN++;
            }
        }

        if (root.TryGetProperty("assembled_context", out var assembled) && assembled.ValueKind == JsonValueKind.String
            && assembled.GetString() is { } body)
            bits.Add("chars=" + body.Length);
    }

    /// <summary>TaskKnowledge ensure_store / cards meta — surface storeDir so FM does not SoftFL-invent after bare ok pulse.</summary>
    static void AppendKbStoreMetaBits(JsonElement root, List<string> bits)
    {
        if (!root.TryGetProperty("meta", out var meta) || meta.ValueKind != JsonValueKind.Object)
            return;
        if (meta.TryGetProperty("storeDir", out var dir) && dir.ValueKind == JsonValueKind.String
            && dir.GetString() is { Length: > 0 } storeDir)
        {
            var one = storeDir.Replace('\\', '/');
            if (one.Length > 48)
                one = "…" + one[^47..];
            bits.Add("store=" + one);
        }

        if (meta.TryGetProperty("resolvedScope", out var scope) && scope.ValueKind == JsonValueKind.String
            && scope.GetString() is { Length: > 0 } s)
        {
            var one = s.Length > 24 ? s[..24] + "…" : s;
            bits.Add("scope=" + one);
        }
    }

    /// <summary>TaskKnowledge read_card JSON — surface path + content chars (not body dump).</summary>
    static void AppendKbReadCardBits(JsonElement root, List<string> bits)
    {
        if (!root.TryGetProperty("relativePath", out var path) || path.ValueKind != JsonValueKind.String
            || path.GetString() is not { Length: > 0 } rel)
            return;

        var one = rel.Length > 40 ? rel[..40] + "…" : rel;
        bits.Add("path=" + one);
        if (root.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String
            && content.GetString() is { } body)
            bits.Add("chars=" + body.Length);
    }

    /// <summary>AN validate_sections report — surface section/dup counts so FM does not SoftFL-invent after bare ok.</summary>
    static void AppendKbValidateBits(JsonElement root, List<string> bits)
    {
        if (!root.TryGetProperty("section_ids", out var ids) || ids.ValueKind != JsonValueKind.Array)
            return;

        bits.Add(ids.GetArrayLength() + " section(s)");
        if (root.TryGetProperty("duplicates", out var dups) && dups.ValueKind == JsonValueKind.Array
            && dups.GetArrayLength() > 0)
            bits.Add("dup=" + dups.GetArrayLength());
        if (root.TryGetProperty("problems", out var probs) && probs.ValueKind == JsonValueKind.Array
            && probs.GetArrayLength() > 0)
            bits.Add("problems=" + probs.GetArrayLength());

        var n = 0;
        foreach (var el in ids.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.String || el.GetString() is not { Length: > 0 } id)
                continue;
            var one = id.Length > 28 ? id[..28] + "…" : id;
            bits.Add("#" + (++n) + " " + one);
            if (n >= 2)
                break;
        }
    }

    /// <summary>AN normalize_sections preview — surface changed= + content chars so FM does not SoftFL-invent after leaked chars-only pulse.</summary>
    static void AppendKbNormalizeBits(JsonElement root, List<string> bits)
    {
        if (!root.TryGetProperty("changed", out var ch)
            || (ch.ValueKind != JsonValueKind.True && ch.ValueKind != JsonValueKind.False))
            return;

        bits.Add(ch.GetBoolean() ? "changed" : "unchanged");
        if (root.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String
            && content.GetString() is { } body)
            bits.Add("chars=" + body.Length);
    }

}
