#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using AIGuiders.Platform.Execution.Language;
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
    public async Task Get_diagnostics_reports_semantic_fs_error_with_fsproj_context()
    {
        IdeLanguageTools.Configure(LanguageRegistry.Default);
        IdeLanguageTools.BindDocumentStore(null);

        var root = Path.Combine(Path.GetTempPath(), "cdp-lrc-sem-" + Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        var fsproj = Path.Combine(root, "SemProj.fsproj");
        var path = Path.Combine(root, "Sem.fs");
        await File.WriteAllTextAsync(
            fsproj,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup><Compile Include="Sem.fs" /></ItemGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(path, "module Sem\nlet x = totallyUnknownIdentifier\n");

        try
        {
            var session = new SessionContext
            {
                ProjectRoot = root,
                Language = CdpLanguages.Csharp,
                SolutionOrProjectPath = fsproj,
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
            var diags = doc.RootElement.GetProperty("diagnostics");
            Assert.True(diags.GetArrayLength() > 0, raw);
            Assert.Equal("error", diags[0].GetProperty("severity").GetString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            IdeLanguageTools.Configure(LanguageRegistry.Default);
        }
    }

    [Fact]
    public async Task Resolver_center_reports_semantic_fs_error_with_fsproj_context()
    {
        var root = Path.Combine(Path.GetTempPath(), "cdp-lrc-center-" + Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        var fsproj = Path.Combine(root, "SemProj.fsproj");
        var path = Path.Combine(root, "Sem.fs");
        await File.WriteAllTextAsync(
            fsproj,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup><Compile Include="Sem.fs" /></ItemGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(path, "module Sem\nlet x = totallyUnknownIdentifier\n");

        try
        {
            var req = new LanguageRequest(path, 1, 1, null, fsproj);
            var result = await CdpLanguageResolverHost.Center.DispatchDiagnosticsAsync(req, CancellationToken.None);

            Assert.NotEmpty(result.Diagnostics);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
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

    [Fact]
    public async Task All_seven_lrc_verbs_green_on_guiders_fsharp_slnx()
    {
        IdeLanguageTools.Configure(LanguageRegistry.Default);
        IdeLanguageTools.BindDocumentStore(null);

        var guidersRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "guiders-fsharp"));
        var slnx = Path.Combine(guidersRoot, "AIGuiders.Platform.Modeling.slnx");
        var kernelFs = Path.Combine(guidersRoot, "src", "AIGuiders.Platform.Modeling.Language", "Kernel.fs");
        var fcsBackendFs = Path.Combine(
            guidersRoot,
            "src",
            "AIGuiders.Platform.Modeling.Language.Adapters.Fcs",
            "FcsLanguageBackend.fs");

        Assert.True(File.Exists(slnx), slnx);
        Assert.True(File.Exists(kernelFs), kernelFs);
        Assert.True(File.Exists(fcsBackendFs), fcsBackendFs);

        var session = new SessionContext
        {
            ProjectRoot = guidersRoot,
            Language = CdpLanguages.Fsharp,
            SolutionOrProjectPath = slnx,
        };

        var backends = new Dictionary<string, ICdpBackendModule>();
        var baseArgs = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["file_path"] = JsonSerializer.SerializeToElement(kernelFs),
            ["line"] = JsonSerializer.SerializeToElement(87),
            ["column"] = JsonSerializer.SerializeToElement(10),
            ["solution_or_project_path"] = JsonSerializer.SerializeToElement(slnx),
        };

        try
        {
            var diags = await DispatchAndParseAsync("get_diagnostics", session, backends, baseArgs);
            Assert.True(diags.RootElement.TryGetProperty("diagnostics", out _));

            var symbols = await DispatchAndParseAsync("get_document_symbols", session, backends, baseArgs);
            Assert.True(symbols.RootElement.TryGetProperty("root", out var root));
            Assert.Equal("Kernel.fs", root.GetProperty("name").GetString());

            var definition = await DispatchAndParseAsync("go_to_definition", session, backends, baseArgs);
            Assert.True(definition.RootElement.TryGetProperty("definition", out _));

            var usages = await DispatchAndParseAsync("find_usages", session, backends, baseArgs);
            Assert.True(usages.RootElement.TryGetProperty("references", out var refs));
            Assert.True(refs.GetArrayLength() > 0, usages.RootElement.GetRawText());

            var symbol = await DispatchAndParseAsync("get_symbol_at_position", session, backends, baseArgs);
            Assert.Equal("LanguageRequest", symbol.RootElement.GetProperty("name").GetString());

            var completionArgs = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["file_path"] = JsonSerializer.SerializeToElement(fcsBackendFs),
                ["line"] = JsonSerializer.SerializeToElement(38),
                ["column"] = JsonSerializer.SerializeToElement(50),
                ["solution_or_project_path"] = JsonSerializer.SerializeToElement(slnx),
            };
            var completions = await DispatchAndParseAsync("get_completions", session, backends, completionArgs);
            Assert.True(completions.RootElement.TryGetProperty("items", out var items));
            Assert.True(items.GetArrayLength() > 0, completions.RootElement.GetRawText());

            var renameArgs = new Dictionary<string, JsonElement>(baseArgs, StringComparer.Ordinal)
            {
                ["new_name"] = JsonSerializer.SerializeToElement("LanguageRequestPreview"),
                ["apply"] = JsonSerializer.SerializeToElement(false),
            };
            var rename = await DispatchAndParseAsync("rename_symbol", session, backends, renameArgs);
            Assert.Equal("LanguageRequest", rename.RootElement.GetProperty("oldName").GetString());
            Assert.False(rename.RootElement.GetProperty("applied").GetBoolean());
            Assert.True(rename.RootElement.GetProperty("changes").GetArrayLength() > 0);
        }
        finally
        {
            IdeLanguageTools.Configure(LanguageRegistry.Default);
        }
    }

    static async Task<JsonDocument> DispatchAndParseAsync(
        string verb,
        SessionContext session,
        Dictionary<string, ICdpBackendModule> backends,
        Dictionary<string, JsonElement> args)
    {
        var raw = await IdeLanguageTools.DispatchBareAsync(
            verb,
            session,
            backends,
            args,
            CancellationToken.None);
        return JsonDocument.Parse(raw);
    }
}
