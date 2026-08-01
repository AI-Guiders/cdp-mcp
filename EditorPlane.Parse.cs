using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;
using CdpMcp.Cockpit.Surface;

namespace CdpMcp;

internal static partial class EditorPlane
{
    sealed class EditSlice(string Message, IReadOnlyList<EditStep> Steps, string? Path = null, IReadOnlyList<string>? FixIds = null)
    {
        public string Message { get; } = Message;
        public string? Path { get; } = Path;
        public IReadOnlyList<string> FixIds { get; } = FixIds ?? [];
        public IReadOnlyList<EditStep> Steps { get; } = Steps;
    }

    sealed class EditStep
    {
        public string? Path { get; init; }
        public string? EditOp { get; init; }
        public string? Anchor { get; init; }
        public string? At { get; init; }
        public string? Text { get; init; }
        public string? OldString { get; init; }
        public string? NewString { get; init; }
        public int? StartLine { get; init; }
        public int? StartColumn { get; init; }
        public int? EndLine { get; init; }
        public int? EndColumn { get; init; }
        public bool? AllowShrink { get; init; }
    }

    static IReadOnlyList<EditSlice>? TryGetSlices(IReadOnlyDictionary<string, JsonElement> args)
    {
        // Prefer YAML string: yaml= | slices_yaml= | plan=
        foreach (var key in new[] { "yaml", "slices_yaml", "plan" })
        {
            var y = OptString(args, key);
            if (y is { Length: > 0 })
                return ParseYamlSlices(y);
        }

        if (!args.TryGetValue("slices", out var el))
            return null;

        // slices as YAML/JSON string (agents often paste a block)
        if (el.ValueKind == JsonValueKind.String)
        {
            var s = el.GetString();
            if (string.IsNullOrWhiteSpace(s))
                return null;
            var t = s.TrimStart();
            if (t.StartsWith('[') || t.StartsWith('{'))
                return ParseJsonSlices(s);
            return ParseYamlSlices(s);
        }

        if (el.ValueKind != JsonValueKind.Array)
            return null;

        return ParseJsonArray(el);
    }

    static IReadOnlyList<EditSlice> ParseYamlSlices(string yaml)
    {
        try
        {
            var trimmed = yaml.TrimStart();
            // Document form: path + fix: (and optional steps/slices wrapper)
            if (trimmed.StartsWith("path:", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("fix:", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("slices:", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("---", StringComparison.Ordinal))
            {
                var wrap = Yaml.Deserialize<YamlPlanDoc>(yaml);
                if (wrap is not null)
                {
                    if (wrap.Slices is { Count: > 0 })
                        return wrap.Slices.Select(FromYamlSlice).ToArray();
                    if ((wrap.Fix is { Count: > 0 } || wrap.Steps is { Count: > 0 })
                        && !string.IsNullOrWhiteSpace(wrap.Path))
                    {
                        return
                        [
                            new EditSlice(
                                wrap.Message ?? "",
                                (wrap.Steps ?? []).Select(FromYamlStep).ToArray(),
                                wrap.Path,
                                wrap.Fix ?? [])
                        ];
                    }
                }
            }

            var list = Yaml.Deserialize<List<YamlSliceDto>>(yaml);
            if (list is null || list.Count == 0)
                throw new ArgumentException("YAML slices empty.");
            return list.Select(FromYamlSlice).ToArray();
        }
        catch (Exception ex) when (ex is not ArgumentException)
        {
            throw new ArgumentException($"YAML slices parse failed: {ex.Message}");
        }
    }

    static EditSlice FromYamlSlice(YamlSliceDto s)
    {
        var steps = (s.Steps ?? []).Select(FromYamlStep).ToArray();
        // If steps omit path but slice has path, inherit for mutate steps.
        if (!string.IsNullOrWhiteSpace(s.Path))
        {
            steps = steps.Select(st => string.IsNullOrWhiteSpace(st.Path)
                ? new EditStep
                {
                    Path = s.Path,
                    EditOp = st.EditOp,
                    Anchor = st.Anchor,
                    At = st.At,
                    Text = st.Text,
                    OldString = st.OldString,
                    NewString = st.NewString,
                    StartLine = st.StartLine,
                    StartColumn = st.StartColumn,
                    EndLine = st.EndLine,
                    EndColumn = st.EndColumn,
                    AllowShrink = st.AllowShrink
                }
                : st).ToArray();
        }

        return new EditSlice(s.Message ?? "", steps, s.Path, s.Fix ?? []);
    }

    static EditStep FromYamlStep(YamlStepDto st) => new()
    {
        Path = st.Path,
        EditOp = st.EditOp ?? st.Op,
        Anchor = st.Anchor,
        At = st.At,
        Text = st.Text,
        OldString = st.OldString,
        NewString = st.NewString,
        StartLine = st.StartLine,
        StartColumn = st.StartColumn,
        EndLine = st.EndLine,
        EndColumn = st.EndColumn,
        AllowShrink = st.AllowShrink
    };

    static IReadOnlyList<EditSlice> ParseJsonSlices(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("slices", out var inner))
            return ParseJsonArray(inner);
        if (root.ValueKind == JsonValueKind.Array)
            return ParseJsonArray(root);
        throw new ArgumentException("JSON slices must be an array or {slices:[…]}.");
    }

    static IReadOnlyList<EditSlice> ParseJsonArray(JsonElement el)
    {
        var list = new List<EditSlice>();
        foreach (var item in el.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;
            var message = item.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
            var path = item.TryGetProperty("path", out var p) ? p.GetString() : null;
            var fixIds = new List<string>();
            if (item.TryGetProperty("fix", out var fixEl) && fixEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var f in fixEl.EnumerateArray())
                {
                    if (f.ValueKind == JsonValueKind.String && f.GetString() is { Length: > 0 } id)
                        fixIds.Add(id);
                    else if (f.ValueKind == JsonValueKind.Object && f.TryGetProperty("id", out var idEl)
                             && idEl.GetString() is { Length: > 0 } oid)
                        fixIds.Add(oid);
                }
            }

            var steps = new List<EditStep>();
            if (item.TryGetProperty("steps", out var stepsEl) && stepsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in stepsEl.EnumerateArray())
                {
                    if (s.ValueKind != JsonValueKind.Object)
                        continue;
                    steps.Add(ParseStep(s));
                }
            }

            list.Add(new EditSlice(message, steps, path, fixIds));
        }

        return list;
    }

    sealed class YamlPlanDoc
    {
        public string? Path { get; set; }
        public string? Message { get; set; }
        public List<string>? Fix { get; set; }
        public List<YamlStepDto>? Steps { get; set; }
        public List<YamlSliceDto>? Slices { get; set; }
    }

    sealed class YamlSliceDto
    {
        public string? Path { get; set; }
        public string? Message { get; set; }
        public List<string>? Fix { get; set; }
        public List<YamlStepDto>? Steps { get; set; }
    }

    sealed class YamlStepDto
    {
        public string? Path { get; set; }
        public string? EditOp { get; set; }
        public string? Op { get; set; }
        public string? Anchor { get; set; }
        public string? At { get; set; }
        public string? Text { get; set; }
        public string? OldString { get; set; }
        public string? NewString { get; set; }
        public int? StartLine { get; set; }
        public int? StartColumn { get; set; }
        public int? EndLine { get; set; }
        public int? EndColumn { get; set; }
        public bool? AllowShrink { get; set; }
    }

    static EditStep ParseStep(JsonElement s) => new()
    {
        Path = PropString(s, "path"),
        EditOp = PropString(s, "edit_op") ?? PropString(s, "op"),
        Anchor = PropString(s, "anchor"),
        At = PropString(s, "at"),
        Text = PropString(s, "text"),
        OldString = PropString(s, "old_string"),
        NewString = PropString(s, "new_string"),
        StartLine = PropInt(s, "start_line"),
        StartColumn = PropInt(s, "start_column"),
        EndLine = PropInt(s, "end_line"),
        EndColumn = PropInt(s, "end_column"),
        AllowShrink = PropBool(s, "allow_shrink")
    };

    static string? PropString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    static int? PropInt(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.TryGetInt32(out var n) ? n : null;

    static bool? PropBool(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p))
            return null;
        return p.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    static IReadOnlyList<string> StringArray(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el) || el.ValueKind != JsonValueKind.Array)
            return [];
        return el.EnumerateArray()
            .Select(e => e.GetString())
            .Where(s => s is { Length: > 0 })
            .Cast<string>()
            .ToArray();
    }

    static bool PathMatches(string full, string pattern)
    {
        if (string.Equals(full, pattern, StringComparison.OrdinalIgnoreCase))
            return true;
        if (full.EndsWith(pattern.Replace('/', Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            return true;
        return full.Contains(pattern, StringComparison.OrdinalIgnoreCase);
    }

    static string ResolveUserPath(SessionContext session, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var p = path.Trim();
        if (Path.IsPathRooted(p))
            return Path.GetFullPath(p);

        var root = session.ProjectRoot is { Length: > 0 } pr
            ? pr
            : Directory.GetCurrentDirectory();
        return Path.GetFullPath(Path.Combine(root, p));
    }

    static string ShortPath(string path)
    {
        if (path.Length <= 64) return path;
        var name = Path.GetFileName(path);
        var dir = Path.GetFileName(Path.GetDirectoryName(path) ?? "");
        return string.IsNullOrEmpty(dir) ? "…/" + name : $"…/{dir}/{name}";
    }

    static int CountLines(string text)
    {
        if (text.Length == 0) return 1;
        var n = 1;
        foreach (var ch in text)
        {
            if (ch == '\n') n++;
        }

        return n;
    }

    static string? OptString(IReadOnlyDictionary<string, JsonElement> args, string key) =>
        args.TryGetValue(key, out var el) ? el.GetString() : null;

    static int IntOr(IReadOnlyDictionary<string, JsonElement> args, string key, int defaultValue) =>
        args.TryGetValue(key, out var el) && el.TryGetInt32(out var n) ? n : defaultValue;

    static int? IntOrNull(IReadOnlyDictionary<string, JsonElement> args, string key) =>
        args.TryGetValue(key, out var el) && el.TryGetInt32(out var n) ? n : null;

    static bool BoolOr(IReadOnlyDictionary<string, JsonElement> args, string key, bool defaultValue)
    {
        if (!args.TryGetValue(key, out var el))
            return defaultValue;
        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => defaultValue
        };
    }
}
