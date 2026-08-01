#nullable enable
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Cdp.Core;

namespace CdpMcp;

internal static partial class IdeLearnChannel
{
    sealed class LearnEntry
    {
        public string Id { get; set; } = "";
        public string? AtUtc { get; set; }
        public string? Title { get; set; }
        public string? Body { get; set; }
        public string? Topic { get; set; }
        public List<string>? Tags { get; set; }
        public string? Primary { get; set; }
        public string? Scope { get; set; }
        public string? ProjectRoot { get; set; }
        public string? Phase { get; set; }
        public string? Object { get; set; }
        public string? PromotedPath { get; set; }
        public string? PromotedUtc { get; set; }
    }
}

