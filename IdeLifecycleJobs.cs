#nullable enable
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Cdp.Core;
using CdpMcp.Backends;
using TerminalMcp.Core;

namespace CdpMcp;

/// <summary>In-process background jobs for cdp_build / cdp_test / cdp_deploy (MCP must not block).</summary>
internal static class IdeLifecycleJobs
{
    public const string Schema = "lifecycle_job/v0";
    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    sealed class Entry
    {
        public required string Id { get; init; }
        public required string Kind { get; init; }
        public required string IgniteEvent { get; init; }
        public string State { get; set; } = "running";
        public string? ResultJson { get; set; }
        public string? Error { get; set; }
        public DateTimeOffset StartedUtc { get; init; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? FinishedUtc { get; set; }
    }

    static readonly ConcurrentDictionary<string, Entry> Jobs = new(StringComparer.OrdinalIgnoreCase);
    static readonly ConcurrentDictionary<string, string> ActiveByKind = new(StringComparer.OrdinalIgnoreCase);

    public static bool ResolveBackground(IReadOnlyDictionary<string, JsonElement> args)
    {
        if (IsTruthy(args, "dry_run") || IsTruthy(args, "peek"))
            return false;
        if (IsTruthy(args, "wait"))
            return false;
        if (args.TryGetValue("background", out var bgEl))
        {
            return bgEl.ValueKind switch
            {
                JsonValueKind.False => false,
                JsonValueKind.True => true,
                JsonValueKind.String => !IsFalseToken(bgEl.GetString()),
                JsonValueKind.Number => bgEl.TryGetInt32(out var n) && n != 0,
                _ => true
            };
        }

        return true;
    }

    /// <summary>durable=true explicit; deploy+background defaults durable (opt-out durable=false).</summary>
    public static bool ResolveDurable(IReadOnlyDictionary<string, JsonElement> args, string kind)
    {
        if (IsTruthy(args, "dry_run") || IsTruthy(args, "peek") || !ResolveBackground(args))
            return false;
        if (args.TryGetValue("durable", out var el))
        {
            return el.ValueKind switch
            {
                JsonValueKind.False => false,
                JsonValueKind.True => true,
                JsonValueKind.String => !IsFalseToken(el.GetString()),
                JsonValueKind.Number => el.TryGetInt32(out var n) && n != 0,
                _ => true
            };
        }

        return kind.Equals("deploy", StringComparison.OrdinalIgnoreCase);
    }

    public static string StartBuild(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args,
        ICdpBackendModule? buildMod,
        JsonSerializerOptions pretty)
    {
        if (ResolveDurable(args, "build"))
            return StartDurable(session, "build", "build_finished", args, pretty);
        return Start(
            "build",
            "build_finished",
            args,
            pretty,
            ct => IdeSessionLifecycle.BuildAsync(session, args, buildMod, ct));
    }

    public static string StartTest(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args,
        ICdpBackendModule? buildMod,
        JsonSerializerOptions pretty)
    {
        if (ResolveDurable(args, "test"))
            return StartDurable(session, "test", "test_finished", args, pretty);
        return Start(
            "test",
            "test_finished",
            args,
            pretty,
            ct => IdeSessionLifecycle.TestAsync(session, args, buildMod, ct));
    }

    public static string StartDeploy(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args,
        JsonSerializerOptions pretty)
    {
        if (ResolveDurable(args, "deploy"))
            return StartDurable(session, "deploy", "peer_ship", args, pretty);
        return Start(
            "deploy",
            "peer_ship",
            args,
            pretty,
            _ => Task.FromResult(IdeDeploy.Run(session, args)),
            onFinished: (ok, _) =>
                IdeIgniteArmHost.Notify("peer_ship", ok, pulse: "deploy", detail: ok ? "ok" : "fail"));
    }

    public static string Scene()
    {
        var durableJson = DurableJobStore.Scene(Pretty);
        try
        {
            using var doc = JsonDocument.Parse(durableJson);
            if (doc.RootElement.TryGetProperty("jobs", out var jobs) && jobs.GetArrayLength() > 0)
                return durableJson;
        }
        catch
        {
            /* fall through */
        }

        var items = Jobs.Values
            .OrderByDescending(e => e.StartedUtc)
            .Take(24)
            .Select(e => new
            {
                job_id = e.Id,
                kind = e.Kind,
                state = e.State,
                ignite_event = e.IgniteEvent,
                started_utc = e.StartedUtc,
                finished_utc = e.FinishedUtc,
                error = e.Error
            })
            .ToList();
        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            active = ActiveByKind.Select(kv => new { kind = kv.Key, job_id = kv.Value }).ToList(),
            jobs = items,
            hint = "Poll cdp_lifecycle_last kind=build|test|deploy or job_id=…"
        }, Pretty);
    }

    public static string Last(IReadOnlyDictionary<string, JsonElement> args)
    {
        string? jobId = args.TryGetValue("job_id", out var idEl) && idEl.ValueKind == JsonValueKind.String
            ? idEl.GetString()
            : null;
        string? kind = args.TryGetValue("kind", out var kindEl) && kindEl.ValueKind == JsonValueKind.String
            ? kindEl.GetString()
            : null;
        var durable = DurableJobStore.Last(jobId, kind, Pretty);
        if (!durable.Contains("\"job_not_found\"", StringComparison.Ordinal))
            return durable;

        Entry? entry = null;
        if (!string.IsNullOrWhiteSpace(jobId))
            Jobs.TryGetValue(jobId, out entry);
        if (entry is null && !string.IsNullOrWhiteSpace(kind))
        {
            if (ActiveByKind.TryGetValue(kind, out var activeId))
                Jobs.TryGetValue(activeId, out entry);
            if (entry is null)
            {
                entry = Jobs.Values
                    .Where(e => e.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(e => e.FinishedUtc ?? e.StartedUtc)
                    .FirstOrDefault();
            }
        }

        if (entry is null)
        {
            return JsonSerializer.Serialize(new
            {
                schema = Schema,
                ok = false,
                error = "job_not_found",
                hint = "Pass kind=build|test|deploy or job_id= from start response."
            }, Pretty);
        }

        if (entry.State == "running")
        {
            return JsonSerializer.Serialize(new
            {
                schema = Schema,
                ok = true,
                state = "running",
                job_id = entry.Id,
                kind = entry.Kind,
                ignite_event = entry.IgniteEvent,
                started_utc = entry.StartedUtc,
                hint = "Still running — AutoIgnition will wake on finish; poll again or wait for wake."
            }, Pretty);
        }

        if (!string.IsNullOrEmpty(entry.ResultJson))
            return entry.ResultJson;

        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = false,
            state = entry.State,
            job_id = entry.Id,
            kind = entry.Kind,
            error = entry.Error ?? "failed",
            started_utc = entry.StartedUtc,
            finished_utc = entry.FinishedUtc
        }, Pretty);
    }

    static string Start(
        string kind,
        string igniteEvent,
        IReadOnlyDictionary<string, JsonElement> args,
        JsonSerializerOptions pretty,
        Func<CancellationToken, Task<string>> work,
        Action<bool, string?>? onFinished = null)
    {
        if (ActiveByKind.TryGetValue(kind, out var existingId)
            && Jobs.TryGetValue(existingId, out var existing)
            && existing.State == "running")
        {
            return JsonSerializer.Serialize(new
            {
                schema = Schema,
                ok = false,
                error = $"{kind}_in_flight",
                job_id = existing.Id,
                kind,
                state = "running",
                started_utc = existing.StartedUtc,
                hint = $"Another {kind} job is still running — poll cdp_lifecycle_last kind={kind}."
            }, pretty);
        }

        var targetHint = Opt(args, "path")
                         ?? Opt(args, "solution_path")
                         ?? Opt(args, "target")
                         ?? Opt(args, "mode")
                         ?? kind;
        var id = $"{kind}-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6]}";
        var entry = new Entry { Id = id, Kind = kind, IgniteEvent = igniteEvent };
        Jobs[id] = entry;
        ActiveByKind[kind] = id;

        string? armId = null;
        if (IdeShellIgnite.ResolveIgniteArmEnabled(args, background: true))
            IdeLifecycleIgnite.TryAutoArm(igniteEvent, kind, targetHint, enabled: true, out armId);

        _ = Task.Run(async () =>
        {
            try
            {
                var result = await work(CancellationToken.None).ConfigureAwait(false);
                entry.ResultJson = result;
                var ok = LooksOk(result);
                entry.State = ok ? "idle" : "failed";
                onFinished?.Invoke(ok, result);
            }
            catch (Exception ex)
            {
                entry.State = "failed";
                entry.Error = ex.Message;
                IdeIgniteArmHost.Notify(igniteEvent, ok: false, pulse: "exception", detail: ex.Message);
                onFinished?.Invoke(false, null);
            }
            finally
            {
                entry.FinishedUtc = DateTimeOffset.UtcNow;
                if (ActiveByKind.TryGetValue(kind, out var active) && active == id)
                    ActiveByKind.TryRemove(kind, out _);
            }
        });

        var started = JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            state = "running",
            job_id = id,
            kind,
            ignite_event = igniteEvent,
            started_utc = entry.StartedUtc,
            hint = $"Job enqueued — MCP returns now. Wake on {igniteEvent} or poll cdp_lifecycle_last kind={kind}."
        }, pretty);
        return IdeLifecycleIgnite.AnnotateStarted(pretty, started, igniteEvent, armId);
    }

    static string StartDurable(
        SessionContext session,
        string kind,
        string igniteEvent,
        IReadOnlyDictionary<string, JsonElement> args,
        JsonSerializerOptions pretty)
    {
        var targetHint = Opt(args, "path")
                         ?? Opt(args, "solution_path")
                         ?? Opt(args, "target")
                         ?? Opt(args, "mode")
                         ?? kind;

        string? armId = null;
        if (ResolveIgniteArm(args))
            IdeLifecycleIgnite.TryAutoArm(igniteEvent, kind, targetHint, enabled: true, out armId);

        var workerExe = ResolveWorkerExePath();
        if (kind.Equals("deploy", StringComparison.OrdinalIgnoreCase) && workerExe is not null)
            workerExe = CloneDeployWorker(workerExe);
        var igniteSeat = IdeIgniteArmHost.Seat;
        if (string.IsNullOrWhiteSpace(igniteSeat))
            igniteSeat = DurableHostPaths.DeriveIgniteSeat(workerExe) ?? igniteSeat;

        var life = new DurableLifecyclePayload
        {
            ProjectRoot = session.ProjectRoot,
            ScmRoot = session.ScmRoot,
            SolutionOrProjectPath = session.SolutionOrProjectPath,
            ProjectKind = session.ProjectKind,
            TsConfigPath = session.TsConfigPath,
            WorkerExePath = workerExe,
            IgniteSeat = igniteSeat,
            ArgsJson = JsonSerializer.Serialize(args)
        };
        PinDeployLifecycle(kind, life);

        var queued = DurableJobStore.EnqueueLifecycle(kind, igniteEvent, life, targetHint, armId, pretty);
        DurableJobSupervisorHost.TryEnsureRunning();
        return armId is null
            ? queued
            : IdeLifecycleIgnite.AnnotateStarted(pretty, queued, igniteEvent, armId);
    }

    static bool ResolveIgniteArm(IReadOnlyDictionary<string, JsonElement> args)
    {
        if (args.TryGetValue("ignite_arm", out var el))
        {
            return el.ValueKind switch
            {
                JsonValueKind.False => false,
                JsonValueKind.True => true,
                JsonValueKind.String => !IsFalseToken(el.GetString()),
                JsonValueKind.Number => el.TryGetInt32(out var n) && n != 0,
                _ => true
            };
        }

        return true;
    }

    static bool LooksOk(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("ok", out var ok))
                return ok.ValueKind == JsonValueKind.True;
            if (root.TryGetProperty("success", out var success))
                return success.ValueKind == JsonValueKind.True;
            if (root.TryGetProperty("exit_code", out var code) && code.TryGetInt32(out var n))
                return n == 0;
            return true;
        }
        catch
        {
            return false;
        }
    }

    static bool IsTruthy(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return false;
        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => el.TryGetInt32(out var n) && n != 0,
            JsonValueKind.String => bool.TryParse(el.GetString(), out var b) && b
                                   || string.Equals(el.GetString(), "1", StringComparison.Ordinal),
            _ => false
        };
    }

    static bool IsFalseToken(string? raw) =>
        raw is "0" or "false" or "no" or "off";

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
        => args.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    internal static string? ResolveWorkerExePath()
    {
        var path = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(path))
            return path;

        path = Process.GetCurrentProcess().MainModule?.FileName;
        if (!string.IsNullOrWhiteSpace(path))
            return path;

        var bundled = Path.Combine(AppContext.BaseDirectory, "CdpMcp.exe");
        return File.Exists(bundled) ? bundled : null;
    }
    /// <summary>
    /// ADR-0211: deploy workers run from a disposable clone under %LocalAppData%/cdp-mcp/workers —
    /// a promote must never be executed by a process whose own bits live in the install dir it
    /// replaces (worker exe would self-lock the target). Non-deploy jobs keep the original exe.
    /// </summary>
    internal static string CloneDeployWorker(string workerExe)
    {
        var sourceDir = Path.GetDirectoryName(Path.GetFullPath(workerExe))!;
        var targetDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "cdp-mcp",
            "workers",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(targetDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.TopDirectoryOnly))
        {
            try
            {
                File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)), true);
            }
            catch
            {
                /* pinned/optional files — runner does not need them all */
            }
        }

        foreach (var dir in Directory.EnumerateDirectories(sourceDir))
        {
            try
            {
                CopyDirTree(dir, Path.Combine(targetDir, Path.GetFileName(dir)));
            }
            catch
            {
                /* best effort */
            }
        }

        var cloned = Path.Combine(targetDir, Path.GetFileName(workerExe));
        return File.Exists(cloned) ? cloned : workerExe;
    }

    static void CopyDirTree(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = file.Replace(source, dest, StringComparison.OrdinalIgnoreCase);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }

    static void PinDeployLifecycle(string kind, DurableLifecyclePayload life)
    {
        if (!kind.Equals("deploy", StringComparison.OrdinalIgnoreCase))
            return;

        var session = new SessionContext
        {
            ProjectRoot = life.ProjectRoot,
            ScmRoot = life.ScmRoot,
            SolutionOrProjectPath = life.SolutionOrProjectPath,
            ProjectKind = life.ProjectKind,
            TsConfigPath = life.TsConfigPath
        };
        var script = IdeDeploy.ResolveScript(session, null);
        if (script is null)
            return;

        var cdpRoot = Path.GetDirectoryName(script)!;
        life.ProjectRoot = cdpRoot;
        life.ScmRoot = cdpRoot;
        life.SolutionOrProjectPath = Path.Combine(cdpRoot, "CdpMcp.csproj");
        life.ProjectKind = "csproj";
    }
}
