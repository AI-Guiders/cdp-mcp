#nullable enable
using System.Text.Json;

namespace CdpMcp;

internal static partial class IdeLearnChannel
{
    static object ExperienceScene()
    {
        var guest = HabitatExperienceLedger.GetPosition("guest");
        var citizen = HabitatExperienceLedger.GetPosition("citizen");
        var human = HabitatExperienceLedger.GetPosition("human");
        return new
        {
            schema = HabitatExperienceLedger.Schema,
            ok = true,
            op = "xp_scene",
            go = GoName,
            tool = ToolName,
            latch = HabitatExperienceLedger.LatchPath,
            positions = new
            {
                guest,
                citizen,
                human
            },
            affordance_guest = HabitatExperienceLedger.AffordanceFor(guest.Position),
            ops = new[] { "xp_scene", "xp_record", "xp_list", "xp_position" },
            next = new object[]
            {
                new { go = "learn", label = "Record lived", why = "op=xp_record principal=guest|citizen|human kind= line=" },
                new { go = "learn", label = "List XP", why = "op=xp_list [principal=]" },
                new { go = "learn", label = "Set position", why = "op=xp_position principal= set=Junior|Middle|Senior|Architect" }
            },
            hint =
                "Habitat experience: lived living-in-substrate for any principal. " +
                "Not SoftFL leaf theater. Position Junior→Architect; Architect = explicit set. " +
                "Vision: playbook-habitat-position-experience-v1 · domain experience.md"
        };
    }

    static object ExperienceRecord(IReadOnlyDictionary<string, JsonElement> args)
    {
        var principal = Opt(args, "principal") ?? Opt(args, "who") ?? HabitatExperienceLedger.DefaultPrincipal;
        var kind = Opt(args, "kind") ?? Opt(args, "type") ?? "dogfood";
        var line = Opt(args, "line") ?? Opt(args, "body") ?? Opt(args, "text");
        if (string.IsNullOrWhiteSpace(line))
            return Fail("missing_line", "xp_record needs line= (lived lesson text)");

        var organ = Opt(args, "organ");
        var source = Opt(args, "source") ?? "learn";
        var lesson = HabitatExperienceLedger.Record(principal, kind, line, source, organ);
        var position = HabitatExperienceLedger.GetPosition(lesson.Principal);
        return new
        {
            schema = HabitatExperienceLedger.Schema,
            ok = true,
            op = "xp_record",
            lesson,
            position,
            affordance = HabitatExperienceLedger.AffordanceFor(position.Position),
            latch = HabitatExperienceLedger.LatchPath
        };
    }

    static object ExperienceList(IReadOnlyDictionary<string, JsonElement> args)
    {
        var principal = Opt(args, "principal") ?? Opt(args, "who");
        var limitRaw = Opt(args, "limit");
        var limit = 20;
        if (int.TryParse(limitRaw, out var n) && n > 0)
            limit = Math.Min(n, 100);

        var lessons = HabitatExperienceLedger.Snapshot(principal).Take(limit).ToArray();
        return new
        {
            schema = HabitatExperienceLedger.Schema,
            ok = true,
            op = "xp_list",
            principal = string.IsNullOrWhiteSpace(principal) ? null : principal,
            count = lessons.Length,
            lessons,
            latch = HabitatExperienceLedger.LatchPath
        };
    }

    static object ExperiencePosition(IReadOnlyDictionary<string, JsonElement> args)
    {
        var principal = Opt(args, "principal") ?? Opt(args, "who") ?? HabitatExperienceLedger.DefaultPrincipal;
        var set = Opt(args, "set") ?? Opt(args, "position") ?? Opt(args, "to");
        if (!string.IsNullOrWhiteSpace(set))
        {
            if (!HabitatExperienceLedger.TryParsePosition(set, out var pos))
                return Fail("bad_position", "set=Junior|Middle|Senior|Architect");
            var pin = Opt(args, "pin");
            var pinned = pin is null
                || !pin.Equals("false", StringComparison.OrdinalIgnoreCase)
                    && !pin.Equals("0", StringComparison.OrdinalIgnoreCase);
            var state = HabitatExperienceLedger.SetPosition(principal, pos, pin: pinned);
            return new
            {
                schema = HabitatExperienceLedger.Schema,
                ok = true,
                op = "xp_position",
                set = true,
                position = state,
                affordance = HabitatExperienceLedger.AffordanceFor(state.Position),
                latch = HabitatExperienceLedger.LatchPath
            };
        }

        var current = HabitatExperienceLedger.GetPosition(principal);
        return new
        {
            schema = HabitatExperienceLedger.Schema,
            ok = true,
            op = "xp_position",
            set = false,
            position = current,
            affordance = HabitatExperienceLedger.AffordanceFor(current.Position),
            latch = HabitatExperienceLedger.LatchPath
        };
    }
}
