#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

public sealed class Ps1BufferDiagnosticsTests
{
    [Fact]
    public void IsPs1Path_matches_extensions()
    {
        Assert.True(Ps1BufferDiagnostics.IsPs1Path("a.ps1"));
        Assert.True(Ps1BufferDiagnostics.IsPs1Path("b.psm1"));
        Assert.True(Ps1BufferDiagnostics.IsPs1Path("c.psd1"));
        Assert.False(Ps1BufferDiagnostics.IsPs1Path("a.cs"));
    }

    [Fact]
    public async Task DiagnoseAsync_green_script()
    {
        if (Ps1PwshRuntime.Resolve() is null)
            return;

        var raw = await Ps1BufferDiagnostics.DiagnoseAsync(
            Path.Combine(Path.GetTempPath(), "ok.ps1"),
            "Write-Output 'ok'\n",
            null);
        using var doc = JsonDocument.Parse(raw);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(0, doc.RootElement.GetProperty("data").GetProperty("error_count").GetInt32());
    }

    [Fact]
    public async Task DiagnoseAsync_syntax_error()
    {
        if (Ps1PwshRuntime.Resolve() is null)
            return;

        var raw = await Ps1BufferDiagnostics.DiagnoseAsync(
            Path.Combine(Path.GetTempPath(), "bad.ps1"),
            "if ( { Write-Output 'nope'\n",
            null);
        using var doc = JsonDocument.Parse(raw);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("data").GetProperty("error_count").GetInt32() > 0);
    }

    [Fact]
    public async Task Buffer_edit_returns_powershell_diagnostics()
    {
        if (Ps1PwshRuntime.Resolve() is null)
            return;

        var root = Path.Combine(Path.GetTempPath(), "cdp-ps1-buf-" + Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "probe.ps1");
            var store = new DocumentBufferStore();
            var session = new SessionContext { ProjectRoot = root };
            var empty = new Dictionary<string, ICdpBackendModule>();

            store.Create(path, "Write-Output 'ok'\n", overwrite: true);
            var editJson = await DocumentEditPlane.DispatchAsync(
                "cdp_buffer",
                store,
                session,
                empty,
                new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["op"] = JsonSerializer.SerializeToElement("edit"),
                    ["path"] = JsonSerializer.SerializeToElement(path),
                    ["edit_op"] = JsonSerializer.SerializeToElement("set_text"),
                    ["text"] = JsonSerializer.SerializeToElement("if ( { broken\n"),
                    ["force"] = JsonSerializer.SerializeToElement(true),
                    ["allow_shrink"] = JsonSerializer.SerializeToElement(true),
                    ["diagnose"] = JsonSerializer.SerializeToElement(true)
                },
                default);
            using var editDoc = JsonDocument.Parse(editJson);
            Assert.True(editDoc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("powershell", editDoc.RootElement.GetProperty("meta").GetProperty("language").GetString());
            var note = editDoc.RootElement.GetProperty("diagnostics_note").GetString();
            Assert.True(note is null or not { Length: > 0 } || !note.Contains("No online diagnostics", StringComparison.Ordinal));
            var diags = editDoc.RootElement.GetProperty("diagnostics");
            Assert.Equal(JsonValueKind.Object, diags.ValueKind);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }
}
