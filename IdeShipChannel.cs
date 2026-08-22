#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=ship_git</c> / Meta <c>cdp_ship</c> — one-call logical commit + push (+ optional deploy).
/// Pipeline: session scm_root → optional secret preflight → git_plan draft → slices → apply push.
/// <c>go=ship</c> stays buffer <c>take</c>; <c>go=ship_desk</c> stays Build-SA fuse.
/// </summary>
internal static class IdeShipChannel
{
    public const string SchemaVersion = "ship/v1";
    public const string ToolName = "cdp_ship";
    public const string LastKey = "ship.last";

    /// <summary>Tests: inject git JSON; live uses <paramref name="byDomain"/> git backend.</summary>
    internal static Func<string, IReadOnlyDictionary<string, JsonElement>, string>? GitCallOverride { get; set; }

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    public static string HandleJson(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args,
        IReadOnlyDictionary<string, ICdpBackendModule>? byDomain = null) =>
        JsonSerializer.Serialize(Handle(session, args, byDomain), Pretty);

    public static object Handle(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args = null,
        IReadOnlyDictionary<string, ICdpBackendModule>? byDomain = null)
    {
        args ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var op = (Opt(args, "op") ?? "run").Trim().ToLowerInvariant();

        return op switch
        {
            "last" => LastCard(),
            "pulse" or "status" => Pulse(session, args, byDomain),
            "run" or "ship" or "commit" => Run(session, args, byDomain),
            _ => new
            {
                ok = false,
                schema = SchemaVersion,
                role = "ship",
                go = "ship_git",
                tool = ToolName,
                error = "unknown_op",
                hint = "op=run|pulse|last. message= required unless slices=[]. push defaults true; deploy defaults false."
            }
        };
    }

    static object Pulse(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args,
        IReadOnlyDictionary<string, ICdpBackendModule>? byDomain)
    {
        var scm = ResolveScm(session);
        if (scm is null)
        {
            return new
            {
                ok = false,
                schema = SchemaVersion,
                role = "ship",
                go = "ship_git",
                tool = ToolName,
                detail = "pulse",
                error = "no_scm",
                hint = "cdp_open first — ship needs session scm_root."
            };
        }

        var dirty = IdeReviewChannel.ListDirtyFiles(scm);
        var secrets = dirty.Count(d => d.Risk.Equals("secret", StringComparison.OrdinalIgnoreCase));
        return new
        {
            ok = true,
            schema = SchemaVersion,
            role = "ship",
            go = "ship_git",
            tool = ToolName,
            detail = "pulse",
            pulse = secrets > 0
                ? $"ship blocked×{secrets} secret-risk"
                : dirty.Count > 0
                    ? $"ship ready×{dirty.Count} dirty"
                    : "ship clean",
            scm_root = scm,
            dirty_count = dirty.Count,
            secret_hits = secrets,
            hint = dirty.Count > 0
                ? "cdp_ship message=\"…\" — or go=build_desk fuse first."
                : "Working tree clean."
        };
    }

    static object Run(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args,
        IReadOnlyDictionary<string, ICdpBackendModule>? byDomain)
    {
        var wired = GitSessionDefaults.WithWorkspace(args, session);
        var scm = ResolveScm(session);
        if (scm is null && !GitSessionDefaults.HasSlices(wired))
        {
            return Fail("no_scm", "cdp_open first — or pass workspace_path= / slices[].");
        }


        var dryRun = BoolOr(args, "dry_run", false) || BoolOr(args, "peek", false);
        var push = BoolOr(args, "push", true);
        var deploy = BoolOr(args, "deploy", false);
        var force = BoolOr(args, "force", false);
        var skipSecrets = BoolOr(args, "skip_secrets", false) || BoolOr(args, "skip_preflight", false);

        if (!skipSecrets && scm is { Length: > 0 })
        {
            var secretHits = IdeReviewChannel.ListDirtyFiles(scm)
                .Count(d => d.Risk.Equals("secret", StringComparison.OrdinalIgnoreCase));
            if (secretHits > 0 && !force)
            {
                return Fail(
                    "secret_risk",
                    $"{secretHits} secret-risk path(s) — git_preflight / exclude, or force=true.",
                    secret_hits: secretHits);
            }
        }

        JsonElement slicesEl;
        if (GitSessionDefaults.HasSlices(wired))
        {
            slicesEl = wired["slices"];
        }
        else
        {
            var message = Opt(args, "message") ?? Opt(args, "msg");
            if (string.IsNullOrWhiteSpace(message))
                return Fail("message_required", "message= commit text, or pass slices=[{root,paths,message}].");

            if (!TryAutoSlices(wired, message!, byDomain, out slicesEl, out var autoErr, out var draft))
            {
                return new
                {
                    ok = false,
                    schema = SchemaVersion,
                    role = "ship",
                    go = "ship_git",
                    tool = ToolName,
                    error = autoErr ?? "auto_slice_failed",
                    hint = "git_plan draft failed — check scm_root and dirty tree.",
                    draft
                };
            }

            if (slicesEl.ValueKind != JsonValueKind.Array || slicesEl.GetArrayLength() == 0)
            {
                return new
                {
                    ok = true,
                    schema = SchemaVersion,
                    role = "ship",
                    go = "ship_git",
                    tool = ToolName,
                    op = "run",
                    verdict = "clean",
                    scm_root = scm,
                    draft,
                    hint = "Nothing to commit — working tree clean."
                };
            }
        }

        var planArgs = new Dictionary<string, JsonElement>(wired, StringComparer.Ordinal)
        {
            ["slices"] = slicesEl
        };

        if (dryRun)
        {
            planArgs["op"] = JsonSerializer.SerializeToElement("validate");
            var validateJson = GitCall("git_plan", planArgs, byDomain);
            var validateOk = TryReadOk(validateJson);
            SaveLast(scm, dryRun: true, validateJson, null, push);
            return new
            {
                ok = validateOk,
                schema = SchemaVersion,
                role = "ship",
                go = "ship_git",
                tool = ToolName,
                op = "dry_run",
                push,
                validate = ParseJsonOrString(validateJson),
                hint = validateOk ? "Slices valid — retry without dry_run to apply+push." : "Fix slices then retry."
            };
        }

        planArgs["op"] = JsonSerializer.SerializeToElement("apply");
        planArgs["push"] = JsonSerializer.SerializeToElement(push);
        if (BoolOr(args, "skip_validate", false))
            planArgs["skip_validate"] = JsonSerializer.SerializeToElement(true);

        var applyJson = GitCall("git_plan", planArgs, byDomain);
        var applyOk = TryReadOk(applyJson);
        object? deployResult = null;
        if (applyOk && deploy)
        {
            var deployArgs = new Dictionary<string, JsonElement>(args, StringComparer.Ordinal);
            if (!deployArgs.ContainsKey("mode"))
                deployArgs["mode"] = JsonSerializer.SerializeToElement(Opt(args, "deploy_mode") ?? "hard");
            deployResult = ParseJsonOrString(IdeDeploy.Run(session, deployArgs));
        }

        SaveLast(scm, dryRun: false, applyJson, deployResult, push);
        if (applyOk)
            IdeDomainStampPending.Mark("ship");

        return new
        {
            ok = applyOk,
            schema = SchemaVersion,
            role = "ship",
            go = "ship_git",
            tool = ToolName,
            op = "run",
            push,
            deploy,
            scm_root = scm,
            apply = ParseJsonOrString(applyJson),
            deploy_result = deployResult,
            hint = applyOk
                ? deploy ? "Shipped + deploy invoked." : "Shipped (commit" + (push ? "+push" : "") + "). deploy=false by default."
                : "apply failed — see apply.errors or run op=pulse / go=build_desk."
        };
    }

    static bool TryAutoSlices(
        IReadOnlyDictionary<string, JsonElement> wired,
        string message,
        IReadOnlyDictionary<string, ICdpBackendModule>? byDomain,
        out JsonElement slicesEl,
        out string? error,
        out object? draft)
    {
        slicesEl = default;
        error = null;
        draft = null;

        var draftArgs = new Dictionary<string, JsonElement>(wired, StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement("draft")
        };
        var draftJson = GitCall("git_plan", draftArgs, byDomain);
        if (draftJson.StartsWith("git_", StringComparison.Ordinal))
        {
            error = draftJson;
            return false;
        }

        draft = ParseJsonOrString(draftJson);
        try
        {
            using var doc = JsonDocument.Parse(draftJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("ok", out var okEl) || okEl.ValueKind != JsonValueKind.True)
            {
                error = "draft_not_ok";
                return false;
            }

            var slices = new List<object>();
            if (!root.TryGetProperty("roots", out var roots) || roots.ValueKind != JsonValueKind.Array)
            {
                error = "draft_no_roots";
                return false;
            }

            foreach (var r in roots.EnumerateArray())
            {
                if (!r.TryGetProperty("ok", out var rok) || rok.ValueKind != JsonValueKind.True)
                    continue;
                if (r.TryGetProperty("dirty", out var dirtyEl) && dirtyEl.ValueKind == JsonValueKind.False)
                    continue;
                if (!r.TryGetProperty("path", out var pathEl) || pathEl.GetString() is not { Length: > 0 } rootPath)
                    continue;
                if (!r.TryGetProperty("paths", out var pathsEl) || pathsEl.ValueKind != JsonValueKind.Array)
                    continue;

                var paths = new List<string>();
                foreach (var p in pathsEl.EnumerateArray())
                {
                    if (p.ValueKind == JsonValueKind.String && p.GetString() is { Length: > 0 } ps)
                        paths.Add(ps);
                }

                if (paths.Count == 0)
                    continue;

                var sliceMessage = message;
                if (roots.GetArrayLength() > 1)
                    sliceMessage = $"{message} ({Path.GetFileName(rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))})";

                slices.Add(new { root = rootPath, paths, message = sliceMessage });
            }

            slicesEl = JsonSerializer.SerializeToElement(slices);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name;
            return false;
        }
    }

    static string GitCall(
        string tool,
        IReadOnlyDictionary<string, JsonElement> args,
        IReadOnlyDictionary<string, ICdpBackendModule>? byDomain)
    {
        if (GitCallOverride is { } ov)
            return ov(tool, args);

        if (byDomain is null || !byDomain.TryGetValue(CdpDomains.Git, out var backend) || !backend.IsEnabled)
            return "git_disabled";

        try
        {
            return backend.CallAsync(tool, args).ConfigureAwait(false).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { ok = false, error = ex.GetType().Name, detail = ex.Message });
        }
    }

    static object LastCard()
    {
        var raw = IdeSettingsStore.GetOrNull(LastKey);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new
            {
                ok = false,
                schema = SchemaVersion,
                role = "ship",
                go = "ship_git",
                tool = ToolName,
                error = "no_last",
                hint = "No prior cdp_ship in this session."
            };
        }

        return new
        {
            ok = true,
            schema = SchemaVersion,
            role = "ship",
            go = "ship_git",
            tool = ToolName,
            op = "last",
            last = ParseJsonOrString(raw)
        };
    }

    static void SaveLast(string? scm, bool dryRun, string applyJson, object? deploy, bool push)
    {
        var payload = JsonSerializer.Serialize(new
        {
            utc = DateTime.UtcNow,
            scm_root = scm,
            dry_run = dryRun,
            push,
            apply = ParseJsonOrString(applyJson),
            deploy
        }, Pretty);
        IdeSettingsStore.Set(LastKey, payload);
    }

    static object Fail(string error, string hint, int? secret_hits = null) => new
    {
        ok = false,
        schema = SchemaVersion,
        role = "ship",
        go = "ship_git",
        tool = ToolName,
        error,
        hint,
        secret_hits
    };

    static string? ResolveScm(SessionContext session)
    {
        var scm = session.ScmRoot;
        if (string.IsNullOrWhiteSpace(scm) && !string.IsNullOrWhiteSpace(session.ProjectRoot))
            scm = GitSessionDefaults.TryResolveScmRoot(session.ProjectRoot);
        return string.IsNullOrWhiteSpace(scm) ? null : scm;
    }

    static bool TryReadOk(string json)
    {
        if (json.StartsWith("git_", StringComparison.Ordinal))
            return false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("ok", out var ok))
                return ok.ValueKind == JsonValueKind.True;
        }
        catch
        {
            /* fall through */
        }

        return json.TrimStart().Length > 0;
    }

    static object? ParseJsonOrString(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<object>(json);
        }
        catch
        {
            return json;
        }
    }

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el) || el.ValueKind != JsonValueKind.String)
            return null;
        var s = el.GetString();
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    static bool BoolOr(IReadOnlyDictionary<string, JsonElement> args, string key, bool defaultValue)
    {
        if (!args.TryGetValue(key, out var el))
            return defaultValue;
        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(el.GetString(), out var b) => b,
            _ => defaultValue
        };
    }
}
