#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

public sealed class LrcBufferDiagnosticsTests
{
    [Fact]
    public async Task Buffer_diagnostics_routes_fsharp_through_lrc()
    {
        IdeLanguageTools.Configure(LanguageRegistry.Default);
        IdeLanguageTools.BindDocumentStore(null);

        var root = Path.Combine(Path.GetTempPath(), "cdp-lrc-buf-" + Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "Broken.fs");
        var store = new DocumentBufferStore();
        var session = new SessionContext
        {
            ProjectRoot = root,
            Language = CdpLanguages.Fsharp,
        };

        try
        {
            store.Create(path, "module Broken\nlet x = (\n", overwrite: true);
            var raw = await DocumentEditPlane.DispatchAsync(
                "cdp_buffer",
                store,
                session,
                new Dictionary<string, ICdpBackendModule>(),
                new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["op"] = JsonSerializer.SerializeToElement("diagnostics"),
                    ["path"] = JsonSerializer.SerializeToElement(path),
                    ["force"] = JsonSerializer.SerializeToElement(true),
                },
                default);

            using var doc = JsonDocument.Parse(raw);
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("fsharp", doc.RootElement.GetProperty("meta").GetProperty("language").GetString());

            var note = doc.RootElement.GetProperty("diagnostics_note").GetString();
            Assert.True(note is null or not { Length: > 0 } || !note.Contains("No online diagnostics", StringComparison.Ordinal));

            var diags = doc.RootElement.GetProperty("diagnostics");
            Assert.Equal(JsonValueKind.Object, diags.ValueKind);
            Assert.True(diags.TryGetProperty("items", out var items));
            Assert.True(items.GetArrayLength() > 0);
            Assert.Contains(
                items.EnumerateArray(),
                d => d.GetProperty("severity").GetString() == "error");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
            IdeLanguageTools.Configure(LanguageRegistry.Default);
        }
    }
}
