using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;
using CdpMcp.Cockpit.Surface;

namespace CdpMcp;

/// <summary>YAML slice/step DTOs + parse (≤ADX soft-warn peel).</summary>
internal static partial class EditorPlane
{
    static IReadOnlyList<EditSlice> ParseYamlSlices(string yaml)
    {
        try
        {
            var trimmed = yaml.TrimStart();
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
}
