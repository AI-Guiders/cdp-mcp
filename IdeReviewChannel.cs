#nullable enable
using System.Diagnostics;
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=review</c> — judgment board after verify, before ship.
/// Machine lane (problems/tests/quality) + judgment prompts + ranked dirty-file cards.
/// Not a full Bugbot; desk projector so review is not a chat dump.
/// </summary>
internal static class IdeReviewChannel
{
    public const string SchemaVersion = "review_organ/v0";
    public const int MaxFiles = 32;

    public sealed record FileCard(
        string Path,
        string Status,
        string Risk,
        string Why,
        string Go);

    public sealed record Inputs(
        SessionContext Session,
        bool GitDirty,
        int ProblemErrors,
        bool TestsFailed,
        int QualityFail,
        int QualityWarn,
        IdeChkChannel.Snap? Ecl = null);

    public sealed record Snap(
        bool Ok,
        string Pulse,
        int FileCount,
        int HighRisk,
        bool MachineOk,
        IReadOnlyList<FileCard> Files);

    public static Snap Build(Inputs i)
    {
        var files = ListDirtyFiles(i.Session.ProjectRoot);
        var high = files.Count(f => f.Risk is "high" or "secret");
        var machineOk = i.ProblemErrors == 0 && !i.TestsFailed && i.QualityFail == 0;
        var pulse = i.Session.Phase is CdpPhase.Review
            ? (files.Count == 0
                ? (machineOk ? "review · idle (clean tree)" : "review · machine still open")
                : $"review · {files.Count} file(s)" + (high > 0 ? $" · risk×{high}" : ""))
            : (files.Count > 0
                ? $"review · ready ×{files.Count}"
                : "review · open when judging after verify");
        return new Snap(true, pulse, files.Count, high, machineOk, files);
    }

    public static object Handle(
        Inputs inputs,
        IReadOnlyDictionary<string, JsonElement>? args = null)
    {
        var merged = FlattenArgs(args);
        var op = (Opt(merged, "op") ?? Opt(merged, "pulse") ?? "board").Trim().ToLowerInvariant();
        var snap = Build(inputs);

        if (op is "files" or "list" or "index")
        {
            return new
            {
                ok = true,
                go = "review",
                schema = SchemaVersion,
                mode = "files",
                pulse = snap.Pulse,
                title = "Review",
                files = snap.Files.Select(FileDto).ToArray(),
                hint = "op=open path=… | go=ecl | go=qrh open skip-review"
            };
        }

        if (op is "open" or "file" or "aim")
        {
            var path = Opt(merged, "path") ?? Opt(merged, "id") ?? Opt(merged, "file");
            if (path is not { Length: > 0 })
                return Err("path_required", "review open path=IdeFoo.cs");
            var card = snap.Files.FirstOrDefault(f =>
                f.Path.Equals(path, StringComparison.OrdinalIgnoreCase)
                || f.Path.EndsWith(path, StringComparison.OrdinalIgnoreCase));
            return new
            {
                ok = true,
                go = "review",
                schema = SchemaVersion,
                mode = "open",
                pulse = card is null ? $"review · open {path}" : $"review · {card.Risk} · {card.Path}",
                title = "Review",
                file = card is null ? new { path, go = "buffer_scene" } : FileDto(card),
                next =
                    new object[]
                    {
                        new { id = "n-buf", go = "buffer_scene", label = "Open in buffer", why = path },
                        new { id = "n-scope", go = "scope", label = "Sniper aim", why = "Corridor before thick read" },
                        new { id = "n-ecl", go = "ecl", label = "ECL review", why = "Judgment checklist" }
                    },
                hint = "cdp_buffer path=… then go=scope"
            };
        }

        // Default board: two lanes + file cards + next.
        var machine = new object[]
        {
            Lane("problems", inputs.ProblemErrors == 0, inputs.ProblemErrors == 0 ? "clear" : $"E×{inputs.ProblemErrors}", "problems"),
            Lane("tests", !inputs.TestsFailed, inputs.TestsFailed ? "failed" : "not failing", "test_scene"),
            Lane("quality", inputs.QualityFail == 0,
                inputs.QualityFail > 0 ? $"FAIL×{inputs.QualityFail}" :
                inputs.QualityWarn > 0 ? $"WARN×{inputs.QualityWarn}" : "ok",
                "quality")
        };

        var judgment = new object[]
        {
            new { id = "intent", label = "Diff matches what was asked", go = "ecl", why = "ECL review · intent-match" },
            new { id = "blast", label = "Blast radius / callers", go = "qrh", why = "qrh open skip-review · or find_usages" },
            new { id = "slices", label = "Logical commit slices", go = "git_draft", why = "git_plan before handoff" },
            new { id = "scm", label = "SCM via desk — not shell", go = "git_scene", why = "git_scene / git_plan; shell only if git MCP dead" },
            new { id = "tests", label = "Tests via desk — not shell", go = "test_scene", why = "cdp_test_scene → cdp_test" },
            new { id = "secrets", label = "No secrets in slices", go = "git_scene", why = "git_preflight" }
        };

        var eclHot = string.Equals(inputs.Ecl?.HotId, "review", StringComparison.OrdinalIgnoreCase);
        return new
        {
            ok = true,
            go = "review",
            schema = SchemaVersion,
            mode = "board",
            pulse = snap.Pulse,
            title = "Review",
            note = "Judgment after verify — machine green ≠ reviewed. Then handoff/ship.",
            phase = CdpEnumParse.ToWire(inputs.Session.Phase),
            machine_ok = snap.MachineOk,
            machine,
            judgment,
            files = snap.Files.Select(FileDto).ToArray(),
            ecl = inputs.Ecl is { } e
                ? new { hot = e.HotId, open_required = e.OpenRequired, pulse = e.Pulse }
                : null,
            next = BuildNext(snap, inputs, eclHot),
            hint = "CCL: review | review files | review open path=Foo.cs | cdp_context phase=review"
        };
    }

    static object[] BuildNext(Snap snap, Inputs i, bool eclHot)
    {
        var list = new List<object>();
        void Add(string id, string go, string label, string why)
        {
            if (list.Count >= 6) return;
            list.Add(new { id, go, label, why });
        }

        if (i.Session.Phase is not CdpPhase.Review)
            Add("n-phase", "plan", "Set phase=review", "cdp_context phase=review (session SSOT)");
        if (eclHot || i.Session.Phase is CdpPhase.Review)
            Add("n-ecl", "ecl", "ECL review", i.Ecl?.Pulse ?? "phase:review checklist");
        if (!snap.MachineOk)
            Add("n-problems", "problems", "Machine lane", "Close errors before judgment");
        if (snap.Files.Count > 0)
            Add("n-files", "review", "File cards", $"op=files · {snap.FileCount}");
        Add("n-qrh", "qrh", "eQRH skip-review", "qrh open skip-review");
        if (i.GitDirty)
            Add("n-git", "git_draft", "Commit slices", "After judgment — logical commits");
        else
            Add("n-ship", "ecl", "Ship checklist", "go=ecl when ready to handoff");
        return list.ToArray();
    }

    static object Lane(string id, bool ok, string pulse, string go) =>
        new { id, ok, pulse, go };

    static object FileDto(FileCard f) => new
    {
        path = f.Path,
        status = f.Status,
        risk = f.Risk,
        why = f.Why,
        go = f.Go
    };

    public static IReadOnlyList<FileCard> ListDirtyFiles(string? projectRoot)
    {
        if (projectRoot is not { Length: > 0 } || !Directory.Exists(projectRoot))
            return [];

        string porcelain;
        try
        {
            porcelain = RunGit(projectRoot, "status --porcelain -uall") ?? "";
        }
        catch
        {
            return [];
        }

        var cards = new List<FileCard>();
        foreach (var raw in porcelain.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (cards.Count >= MaxFiles)
                break;
            if (raw.Length < 3)
                continue;
            var status = raw[..2];
            var path = raw.Length > 3 ? raw[3..].Trim() : "";
            if (path.Contains(" -> ", StringComparison.Ordinal))
                path = path[(path.LastIndexOf(" -> ", StringComparison.Ordinal) + 4)..];
            if (path.Length == 0)
                continue;
            var (risk, why) = ScoreRisk(path, status);
            cards.Add(new FileCard(path, status.Trim(), risk, why, "review"));
        }

        return cards
            .OrderByDescending(c => RiskRank(c.Risk))
            .ThenBy(c => c.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    static int RiskRank(string risk) => risk switch
    {
        "secret" => 4,
        "high" => 3,
        "med" => 2,
        _ => 1
    };

    static (string Risk, string Why) ScoreRisk(string path, string status)
    {
        var name = Path.GetFileName(path);
        if (name.Equals(".env", StringComparison.OrdinalIgnoreCase)
            || name.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || name.Contains("credential", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".pem", StringComparison.OrdinalIgnoreCase))
            return ("secret", "Possible secret — exclude from commit");

        if (path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
            || name.Equals("CdpEnums.cs", StringComparison.OrdinalIgnoreCase)
            || path.Contains("Program.cs", StringComparison.OrdinalIgnoreCase))
            return ("high", "Surface / project contract");

        if (status.Contains('?', StringComparison.Ordinal)
            || status.Contains('A', StringComparison.Ordinal)
            || status.Contains('D', StringComparison.Ordinal)
            || status.Contains('R', StringComparison.Ordinal))
            return ("med", "Add/delete/rename — check intent");

        if (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
            return ("med", "Source change — judgment lane");

        return ("low", "Support / config");
    }

    static string? RunGit(string cwd, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = args,
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi);
        if (p is null) return null;
        var stdout = p.StandardOutput.ReadToEnd();
        _ = p.StandardError.ReadToEnd();
        if (!p.WaitForExit(8000))
        {
            try { p.Kill(entireProcessTree: true); } catch { /* ignore */ }
            return null;
        }

        return p.ExitCode == 0 ? stdout : stdout;
    }

    static Dictionary<string, JsonElement> FlattenArgs(IReadOnlyDictionary<string, JsonElement>? args)
    {
        var d = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (args is null) return d;
        foreach (var (k, v) in args)
        {
            if (k.Equals("go_args", StringComparison.OrdinalIgnoreCase)
                && v.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in v.EnumerateObject())
                    d[p.Name] = p.Value.Clone();
            }
            else
                d[k] = v.Clone();
        }

        return d;
    }

    static string? Opt(IReadOnlyDictionary<string, JsonElement> d, string key) =>
        d.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    static object Err(string code, string hint) =>
        new { ok = false, go = "review", schema = SchemaVersion, error = code, hint };
}
