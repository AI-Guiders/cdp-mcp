#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=review</c> — judgment board after verify, before ship.
/// Machine lane (problems/tests/quality) + judgment prompts + ranked dirty-file cards.
/// Partials: Models (DTO), View (next/dto), Git (dirty/risk), Util (args).
/// </summary>
internal static partial class IdeReviewChannel
{
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

}
