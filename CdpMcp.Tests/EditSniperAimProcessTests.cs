#nullable enable
using System.Text.Json;
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

public sealed class EditSniperAimProcessTests : IDisposable
{
    public EditSniperAimProcessTests()
    {
        EditSniper.Dispatch(new DocumentBufferStore(), new SessionContext(), Dict(("op", "clear")));
    }

    public void Dispose()
    {
        EditSniper.Dispatch(new DocumentBufferStore(), new SessionContext(), Dict(("op", "clear")));
    }

    [Fact]
    public void Scope_L_range_is_line_literal_full_lines_and_arms()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cdp-sniper-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "Sample.csproj");
            File.WriteAllText(path, string.Join('\n',
            [
                "<Project>",
                "  <ItemGroup>",
                "    <Compile Remove=\"**/obj/**\" />",
                "    <Compile Remove=\"**/bin/**\" />",
                "    <!-- Contracts -->",
                "    <Compile Remove=\"A/**/*.cs\" />",
                "    <Compile Remove=\"B/**/*.cs\" />",
                "    <Compile Remove=\"C/**/*.cs\" />",
                "  </ItemGroup>",
                "</Project>",
                ""
            ]));

            var store = new DocumentBufferStore();
            var session = new SessionContext { ProjectRoot = dir };
            var fileName = Path.GetFileName(path);
            var json = EditSniper.Dispatch(store, session, Dict(
                ("op", "scope"),
                ("from", $"[F:{fileName};L:4]"),
                ("till", $"[F:{fileName};L:6]")));

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            Assert.True(root.GetProperty("ok").GetBoolean());
            Assert.Equal("armed", root.GetProperty("phase").GetString());
            Assert.Equal("line_literal+line_literal", root.GetProperty("resolve").GetString());
            Assert.Equal(4, root.GetProperty("hold").GetProperty("line_start").GetInt32());
            Assert.Equal(6, root.GetProperty("hold").GetProperty("line_end").GetInt32());
            Assert.Contains("bin", root.GetProperty("text").GetString(), StringComparison.Ordinal);
            Assert.Contains("Contracts", root.GetProperty("text").GetString(), StringComparison.Ordinal);
            Assert.True(EditSniper.IsArmed);
            Assert.True(EditSniper.TryEnsureFire(out _, out _));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Scope_T_needle_survives_wrong_L_hint()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cdp-sniper-t-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "Sample.cs");
            File.WriteAllText(path, string.Join('\n',
            [
                "namespace Demo;",
                "",
                "public static class Host", 
                "{",
                "    public static void Run() { }",
                "    public static void Arm() { }",
                "}",
                ""
            ]));

            var store = new DocumentBufferStore();
            var session = new SessionContext { ProjectRoot = dir };
            var fileName = Path.GetFileName(path);
            // L:2 is blank / wrong after inserts; T: finds Arm() on real line.
            var json = EditSniper.Dispatch(store, session, Dict(
                ("op", "scope"),
                ("from", $"[F:{fileName};L:2;T:public static void Arm()]")));

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            Assert.True(root.GetProperty("ok").GetBoolean());
            Assert.Equal("armed", root.GetProperty("phase").GetString());
            Assert.Contains("content_literal", root.GetProperty("resolve").GetString(), StringComparison.Ordinal);
            Assert.Equal(6, root.GetProperty("hold").GetProperty("line_start").GetInt32());
            Assert.Contains("Arm()", root.GetProperty("text").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void TryEnsureFire_hard_blocks_without_hold()
    {
        Assert.False(EditSniper.TryEnsureFire(out var err, out var hint));
        Assert.Equal("no_sniper_hold", err);
        Assert.Contains("scope", hint, StringComparison.OrdinalIgnoreCase);
    }

    static Dictionary<string, JsonElement> Dict(params (string Key, string Value)[] pairs)
    {
        var d = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (k, v) in pairs)
            d[k] = JsonSerializer.SerializeToElement(v);
        return d;
    }
}
