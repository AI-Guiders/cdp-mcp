#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

public sealed class CdpLrcDispatchTests
{
    [Fact]
    public async Task Get_diagnostics_routes_fsharp_through_lrc()
    {
        IdeLanguageTools.Configure(LanguageRegistry.Default);
        IdeLanguageTools.BindDocumentStore(null);

        var root = Path.Combine(Path.GetTempPath(), "cdp-lrc-fs-" + Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "Module.fs");
        await File.WriteAllTextAsync(path, "module Sample\nlet answer = 42\n");

        try
        {
            var session = new SessionContext { ProjectRoot = root, Language = CdpLanguages.Fsharp };
            var raw = await IdeLanguageTools.DispatchBareAsync(
                "get_diagnostics",
                session,
                new Dictionary<string, ICdpBackendModule>(),
                new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["file_path"] = JsonSerializer.SerializeToElement(path),
                },
                CancellationToken.None);

            using var doc = JsonDocument.Parse(raw);
            Assert.True(doc.RootElement.TryGetProperty("diagnostics", out var diags));
            Assert.Equal(JsonValueKind.Array, diags.ValueKind);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            IdeLanguageTools.Configure(LanguageRegistry.Default);
        }
    }

    [Fact]
    public async Task Get_diagnostics_routes_fs_by_extension_when_session_is_csharp()
    {
        IdeLanguageTools.Configure(LanguageRegistry.Default);
        IdeLanguageTools.BindDocumentStore(null);

        var root = Path.Combine(Path.GetTempPath(), "cdp-lrc-mixed-" + Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "Module.fs");
        await File.WriteAllTextAsync(path, "module Sample\nlet answer = 42\n");

        try
        {
            var session = new SessionContext
            {
                ProjectRoot = root,
                Language = CdpLanguages.Csharp,
                SolutionOrProjectPath = Path.Combine(root, "Mixed.slnx"),
            };
            var raw = await IdeLanguageTools.DispatchBareAsync(
                "get_diagnostics",
                session,
                new Dictionary<string, ICdpBackendModule>(),
                new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["file_path"] = JsonSerializer.SerializeToElement(path),
                },
                CancellationToken.None);

            using var doc = JsonDocument.Parse(raw);
            Assert.True(doc.RootElement.TryGetProperty("diagnostics", out var diags));
            Assert.Equal(JsonValueKind.Array, diags.ValueKind);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            IdeLanguageTools.Configure(LanguageRegistry.Default);
        }
    }

    [Fact]
    public async Task Get_diagnostics_returns_syntax_errors_for_broken_fs_source()
    {
        IdeLanguageTools.Configure(LanguageRegistry.Default);
        IdeLanguageTools.BindDocumentStore(null);

        var root = Path.Combine(Path.GetTempPath(), "cdp-lrc-syntax-" + Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "Broken.fs");

        try
        {
            var session = new SessionContext { ProjectRoot = root, Language = CdpLanguages.Csharp };
            var raw = await IdeLanguageTools.DispatchBareAsync(
                "get_diagnostics",
                session,
                new Dictionary<string, ICdpBackendModule>(),
                new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["file_path"] = JsonSerializer.SerializeToElement(path),
                    ["source_text"] = JsonSerializer.SerializeToElement("module Broken\nlet x =\n"),
                },
                CancellationToken.None);

            using var doc = JsonDocument.Parse(raw);
            var diags = doc.RootElement.GetProperty("diagnostics");
            Assert.Equal(JsonValueKind.Array, diags.ValueKind);
            Assert.True(diags.GetArrayLength() > 0);
            Assert.Equal("error", diags[0].GetProperty("severity").GetString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            IdeLanguageTools.Configure(LanguageRegistry.Default);
        }
    }

    [Fact]
    public async Task Refuses_csharp_engine_for_fs_file()
    {
        IdeLanguageTools.Configure(LanguageRegistry.Default);
        IdeLanguageTools.BindDocumentStore(null);

        var root = Path.Combine(Path.GetTempPath(), "cdp-lrc-refuse-" + Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "Module.fs");
        await File.WriteAllTextAsync(path, "let x = 1\n");

        try
        {
            var session = new SessionContext { ProjectRoot = root, Language = CdpLanguages.Csharp };
            var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
                IdeLanguageTools.DispatchBareAsync(
                    "get_diagnostics",
                    session,
                    new Dictionary<string, ICdpBackendModule>(),
                    new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                    {
                        ["file_path"] = JsonSerializer.SerializeToElement(path),
                        ["language"] = JsonSerializer.SerializeToElement(CdpLanguages.Csharp),
                    },
                    CancellationToken.None));

            Assert.Contains("Refusing csharp engine", ex.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            IdeLanguageTools.Configure(LanguageRegistry.Default);
        }
    }

    [Fact]
    public void Detect_fs_file_as_fsharp()
    {
        var registry = LanguageRegistry.Default;
        var root = Path.Combine(Path.GetTempPath(), "cdp-detect-fs-" + Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "X.fs");
        File.WriteAllText(path, "let a = 1");

        try
        {
            var detected = registry.Detect(path);
            Assert.Equal(CdpLanguages.Fsharp, detected.Language);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
