#nullable enable
using System.Text.Json;

namespace CdpMcp;

internal static partial class IdeRefactorPlanChannel
{
    static object BuildBlast(IReadOnlyDictionary<string, JsonElement> args)
    {
        var path = Opt(args, "path") ?? Opt(args, "file_path");
        var line = OptInt(args, "line");
        var column = OptInt(args, "column") ?? 1;
        var symbol = Opt(args, "symbol") ?? Opt(args, "name");

        var ready = path is { Length: > 0 } && line is > 0;
        var pulse = ready
            ? $"refactor_plan · blast · line={line} · find_usages ready"
            : "refactor_plan · blast · need path+line for find_usages";

        var next = new List<object>();
        if (ready)
        {
            next.Add(new
            {
                go = "find_usages",
                label = "Callers / blast",
                why = $"file_path={path} line={line} column={column}"
            });
            next.Add(new
            {
                go = "go_to_definition",
                label = "Definition",
                why = $"file_path={path} line={line} column={column}"
            });
        }
        else
        {
            next.Add(new { go = "goto", label = "Land locus", why = "need line/col for blast" });
            next.Add(new { go = "scope", label = "Sniper", why = "aim method before usages" });
        }

        next.Add(new
        {
            go = "test",
            label = "Test touch",
            why = symbol is { Length: > 0 }
                ? $"filter~{symbol} — confirm test blast"
                : "filter by type/method after find_usages"
        });
        next.Add(new
        {
            go = "review",
            label = "Dirty / ship risk",
            why = "structural cut near dirty files?"
        });

        return new
        {
            ok = true,
            pulse,
            ready,
            locus = new { path, line, column, symbol },
            axes = new[]
            {
                "callers (find_usages)",
                "tests (cdp_test filter)",
                "deploy/seat — only if public Meta/dispatch surface moves"
            },
            next = next.ToArray(),
            hint = "Blast card is decide routing — run find_usages for live counts."
        };
    }

    static object[] BuildNext(Hotspot? top, object blast)
    {
        var list = new List<object>
        {
            new { go = "sa_desk", label = "SA verdict", why = "leave|touch|split before cut" },
            new { go = "scope", label = "Sniper corridor", why = "aim extract locus" },
            new { go = "quality", label = "Raw gates", why = "full findings" }
        };

        if (top is not null)
        {
            list.Insert(0, new
            {
                go = "refactor_plan",
                label = "Budget what-if",
                why = $"op=budget path={top.Path} extract_lines=N"
            });
            list.Insert(0, new
            {
                go = "buffer",
                label = "Open hotspot",
                why = $"cdp_buffer op=open path={top.Path}"
            });
        }

        try
        {
            using var bdoc = JsonDocument.Parse(JsonSerializer.Serialize(blast));
            if (bdoc.RootElement.TryGetProperty("next", out var narr) && narr.ValueKind == JsonValueKind.Array)
            {
                foreach (var n in narr.EnumerateArray().Take(2))
                {
                    if (n.TryGetProperty("go", out var g) && n.TryGetProperty("label", out var lab))
                    {
                        list.Add(new
                        {
                            go = g.GetString(),
                            label = lab.GetString(),
                            why = n.TryGetProperty("why", out var w) ? w.GetString() : null
                        });
                    }
                }
            }
        }
        catch { /* ignore */ }

        list.Add(new { go = "refactor_plan", label = "Partials seam", why = "op=partials topic=…" });
        return list.ToArray();
    }
}
