#nullable enable
using System.Text;
using System.Text.Json;

namespace CdpMcp;

internal static partial class IdeRepl
{
    /// <summary>CCL verbs peeled from Apply (soft-warn). null = not handled.</summary>
    static (Dictionary<string, JsonElement> Args, object? Direct)? TryCrm(
        string head,
        IReadOnlyList<string> tokens,
        Dictionary<string, JsonElement> merged)
    {
        if (head is "confirm" or "approved" or "cleared")
        {
            merged["go"] = JsonSerializer.SerializeToElement("crm");
            merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "respond", code = "approved" });
            return (merged, null);
        }

        if (head is "reject" or "denied" or "go_around" or "goaround")
        {
            merged["go"] = JsonSerializer.SerializeToElement("crm");
            merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "respond", code = "go_around" });
            return (merged, null);
        }

        if (head is "stabilized" or "stable")
        {
            merged["go"] = JsonSerializer.SerializeToElement("crm");
            merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "respond", code = "stabilized" });
            return (merged, null);
        }

        if (head is "hold" or "standby")
        {
            merged["go"] = JsonSerializer.SerializeToElement("crm");
            merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "respond", code = "hold" });
            return (merged, null);
        }

        if (head is "unable")
        {
            merged["go"] = JsonSerializer.SerializeToElement("crm");
            merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "respond", code = "unable" });
            return (merged, null);
        }

        if (head is "negative")
        {
            merged["go"] = JsonSerializer.SerializeToElement("crm");
            merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "respond", code = "negative" });
            return (merged, null);
        }

        if (head is "say_again" or "sayagain")
        {
            merged["go"] = JsonSerializer.SerializeToElement("crm");
            merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "respond", code = "say_again" });
            return (merged, null);
        }

        if (head is "roger" or "wilco" or "continue")
        {
            merged["go"] = JsonSerializer.SerializeToElement("crm");
            merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "respond", code = head });
            return (merged, null);
        }

        // "go around" two-token
        if (head is "go" && tokens.Count >= 2 && tokens[1] is "around")
        {
            merged["go"] = JsonSerializer.SerializeToElement("crm");
            merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "respond", code = "go_around" });
            return (merged, null);
        }

        if (head is "crm" or "callout")
        {
            merged["go"] = JsonSerializer.SerializeToElement("crm");
            if (tokens.Count >= 2)
            {
                var sub = string.Join('_', tokens.Skip(1)).ToLowerInvariant();
                if (sub is "scene" or "last" or "clear" or "lexicon" or "call")
                    merged["go_args"] = JsonSerializer.SerializeToElement(new { op = sub });
                else
                {
                    var code = IdeCrmChannel.NormCode(sub);
                    if (code is not null)
                        merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "respond", code });
                }
            }
            return (merged, null);
        }

        if (head is "files" or "files_desk" or "explorer" or "fm" or "ls" or "dir")
        {
            merged["go"] = JsonSerializer.SerializeToElement("files_desk");
            if (head is "ls" or "dir")
                merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "list" });
            else if (tokens.Count >= 2)
            {
                var sub = tokens[1].ToLowerInvariant();
                if (sub is "scene" or "list" or "ls" or "up" or "tree" or "roots" or "clear" or "stat" or "open" or "search" or "cd")
                {
                    var op = sub is "ls" ? "list" : sub;
                    if (tokens.Count >= 3)
                        merged["go_args"] = JsonSerializer.SerializeToElement(new { op, path = string.Join(' ', tokens.Skip(2)) });
                    else
                        merged["go_args"] = JsonSerializer.SerializeToElement(new { op });
                }
                else
                    merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "cd", path = string.Join(' ', tokens.Skip(1)) });
            }
            return (merged, null);
        }

        if (head is "cd" && tokens.Count >= 2)
        {
            merged["go"] = JsonSerializer.SerializeToElement("files_desk");
            merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "cd", path = string.Join(' ', tokens.Skip(1)) });
            return (merged, null);
        }

        if (head is "ignite" or "ignite_desk" or "autoignite" or "cdt_ignite" or "cdp_ignite")
        {
            merged["go"] = JsonSerializer.SerializeToElement("ignite_desk");
            if (tokens.Count >= 2)
            {
                var sub = tokens[1].ToLowerInvariant();
                if (sub is "scene" or "probe" or "chats" or "list" or "arms" or "disarm")
                {
                    if (sub is "disarm" && tokens.Count >= 3)
                        merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "disarm", id = tokens[2] });
                    else if (sub is "arms")
                        merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "list" });
                    else
                        merged["go_args"] = JsonSerializer.SerializeToElement(new { op = sub });
                }
                else if (sub is "arm" or "send" or "fire")
                {
                    var rest = string.Join(' ', tokens.Skip(2));
                    if (sub is "arm")
                    {
                        // arm build_finished … | arm 5m … | arm timer 5m …
                        var when = "timer";
                        var inRaw = (string?)null;
                        var msgStart = 2;
                        if (tokens.Count >= 3)
                        {
                            var t2 = tokens[2].ToLowerInvariant();
                            if (t2 is "build" or "build_finished" or "test" or "test_finished" or "timer")
                            {
                                when = IdeIgniteArmHost.NormalizeEvent(t2);
                                msgStart = 3;
                                if (when == "timer" && tokens.Count >= 4
                                    && IdeIgniteArmHost.TryParseDuration(tokens[3], out _))
                                {
                                    inRaw = tokens[3];
                                    msgStart = 4;
                                }
                            }
                            else if (IdeIgniteArmHost.TryParseDuration(t2, out _))
                            {
                                when = "timer";
                                inRaw = t2;
                                msgStart = 3;
                            }
                        }

                        var body = string.Join(' ', tokens.Skip(msgStart));
                        merged["go_args"] = JsonSerializer.SerializeToElement(new
                        {
                            op = "arm",
                            when,
                            @in = inRaw,
                            message = string.IsNullOrWhiteSpace(body) ? null : body,
                            task = string.IsNullOrWhiteSpace(body) ? null : body
                        });
                    }
                    else if (!string.IsNullOrWhiteSpace(rest))
                        merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "send", message = rest });
                    else
                        merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "send" });
                }
                else
                    merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "send", message = string.Join(' ', tokens.Skip(1)) });
            }
            return (merged, null);
        }

        if (head is "arm")
        {
            // shorthand: arm 5m do X | arm build_finished do X
            merged["go"] = JsonSerializer.SerializeToElement("ignite_desk");
            var when = "timer";
            var inRaw = (string?)null;
            var msgStart = 1;
            if (tokens.Count >= 2)
            {
                var t1 = tokens[1].ToLowerInvariant();
                if (t1 is "build" or "build_finished" or "test" or "test_finished" or "timer")
                {
                    when = IdeIgniteArmHost.NormalizeEvent(t1);
                    msgStart = 2;
                    if (when == "timer" && tokens.Count >= 3 && IdeIgniteArmHost.TryParseDuration(tokens[2], out _))
                    {
                        inRaw = tokens[2];
                        msgStart = 3;
                    }
                }
                else if (IdeIgniteArmHost.TryParseDuration(t1, out _))
                {
                    inRaw = t1;
                    msgStart = 2;
                }
            }

            var body = string.Join(' ', tokens.Skip(msgStart));
            merged["go_args"] = JsonSerializer.SerializeToElement(new
            {
                op = "arm",
                when,
                @in = inRaw,
                message = string.IsNullOrWhiteSpace(body) ? null : body,
                task = string.IsNullOrWhiteSpace(body) ? null : body
            });
            return (merged, null);
        }

        if (head is "disarm")
        {
            merged["go"] = JsonSerializer.SerializeToElement("ignite_desk");
            if (tokens.Count >= 2 && tokens[1].Equals("all", StringComparison.OrdinalIgnoreCase))
                merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "disarm", all = true });
            else if (tokens.Count >= 2)
                merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "disarm", id = tokens[1] });
            else
                merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "list" });
            return (merged, null);
        }

        if (head is "drop" or "rm" or "delete")
        {
            // drop feature X | drop task X | drop X | drop
            if (tokens.Count >= 3
                && tokens[1] is "feature" or "intent" or "task" or "stage")
            {
                var kind = tokens[1] is "feature" or "intent" ? "feature" : "task";
                var title = string.Join(' ', tokens.Skip(2));
                merged["go"] = JsonSerializer.SerializeToElement("plan");
                merged["tm_op"] = JsonSerializer.SerializeToElement(kind == "feature" ? "feature_drop" : "task_drop");
                merged["go_args"] = JsonSerializer.SerializeToElement(new { title, kind, op = "drop" });
                return (merged, null);
            }

            merged["go"] = JsonSerializer.SerializeToElement("plan");
            merged["tm_op"] = JsonSerializer.SerializeToElement("drop");
            if (tokens.Count >= 2)
            {
                var title = string.Join(' ', tokens.Skip(1));
                merged["go_args"] = JsonSerializer.SerializeToElement(new { title, op = "drop" });
            }
            else
                merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "drop" });
            return (merged, null);
        }

        if (head is "full" or "pane_full")
        {
            if (tokens.Count < 2)
                return (merged, Err("full needs pin", "full browser | full report"));
            merged["pane_full"] = JsonSerializer.SerializeToElement(tokens[1]);
            return (merged, null);
        }

        // Bare organ: `browser`, `git`, `editor` …
        if (IdeCockpit.IsKnownGoVerb(tokens[0]) || IdeCockpit.IsKnownPinAlias(tokens[0]))
        {
            ApplyGo(merged, tokens, start: 0);
            return (merged, null);
        }

        // `p project` / `m browser` / `forward editor` — seat shorthand
        if (IdeDeskSeats.NormalizeSeatId(head) is { } seatId)
        {
            if (tokens.Count < 2)
                return (merged, Err($"{seatId} needs organ", $"{seatId} browser"));
            merged["seat"] = JsonSerializer.SerializeToElement(seatId);
            merged["organ"] = JsonSerializer.SerializeToElement(tokens[1]);
            return (merged, null);
        }
        return null;
    }
}
