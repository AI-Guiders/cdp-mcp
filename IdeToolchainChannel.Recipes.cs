#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;
using Cdp.Core;
using CdpMcp.Cockpit.DataAcquisition;
using TerminalMcp.Core;

namespace CdpMcp;

/// <summary>Recipe catalog / probe / install helpers for go=toolchain.</summary>
internal static partial class IdeToolchainChannel
{
    static object InstallCore(Recipe recipe, string via)
    {
        if (_shell is null || _shellDefaults is null)
            return Fail("shell_unconfigured", "IdeToolchainChannel.Configure not called");

        var hit = recipe.Vias.FirstOrDefault(v => v.Name.Equals(via, StringComparison.OrdinalIgnoreCase));
        if (hit is null)
            return Fail("unknown_via", $"via={string.Join("|", recipe.Vias.Select(v => v.Name))}");

        try
        {
            var shellJson = _shell.Run(
                _shellDefaults(),
                command: null,
                tabId: "toolchain-install",
                cwd: null,
                shellPrefer: null,
                timeoutSeconds: Math.Max(IdeSettingsHabitat.EffectiveShellTimeout(), 300),
                background: false,
                codepage: IdeSettingsHabitat.EffectiveShellCodepage(),
                argv: hit.Argv);
            return new
            {
                schema = SchemaVersion,
                ok = true,
                op = "install_core",
                id = recipe.Id,
                via,
                shell = JsonSerializer.Deserialize<object>(shellJson)
            };
        }
        catch (Exception ex)
        {
            return Fail("install_failed", ex.Message);
        }
    }


    sealed record ViaStep(string Name, string[] Argv);

    sealed record Recipe(
        string Id,
        string Label,
        string[] Bins,
        string SearchQuery,
        string? PairsLsp,
        ViaStep[] Vias);

    sealed record BinHit(string Bin, bool Ok, string? Path);

    sealed record Row(
        string Id,
        bool Ok,
        List<BinHit> BinResults,
        string SearchQuery,
        string? PairsLsp,
        string? Error);

    sealed class UserRecipeDoc
    {
        public string? Id { get; set; }
        public string? Label { get; set; }
        public string[]? Bins { get; set; }
        public string? SearchQ { get; set; }
        public string? PairsLsp { get; set; }
        public UserViaDoc[]? Vias { get; set; }
    }

    sealed class UserViaDoc
    {
        public string? Via { get; set; }
        public string[]? Argv { get; set; }
    }

    static readonly Dictionary<string, Recipe> BuiltIns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["python"] = new(
            "python",
            "Python runtime",
            ["python"],
            "python install windows winget",
            "python",
            [
                new("winget", ["winget", "install", "-e", "--id", "Python.Python.3.12"]),
                new("scoop", ["scoop", "install", "python"])
            ]),
        ["gcc"] = new(
            "gcc",
            "GCC / MinGW",
            ["gcc"],
            "mingw gcc install windows",
            null,
            [
                new("winget", ["winget", "install", "-e", "--id", "BrechtSanders.WinLibs.POSIX.UCRT"]),
                new("scoop", ["scoop", "install", "gcc"])
            ]),
        ["javac"] = new(
            "javac",
            "JDK (javac)",
            ["javac"],
            "jdk javac install windows",
            null,
            [
                new("winget", ["winget", "install", "-e", "--id", "Microsoft.OpenJDK.21"]),
                new("scoop", ["scoop", "install", "temurin-jdk"])
            ]),
        ["go"] = new(
            "go",
            "Go toolchain",
            ["go"],
            "go programming language install windows",
            "go",
            [
                new("winget", ["winget", "install", "-e", "--id", "GoLang.Go"]),
                new("scoop", ["scoop", "install", "go"])
            ]),
        ["rust"] = new(
            "rust",
            "Rust toolchain (rustc + cargo)",
            ["rustc", "cargo"],
            "rustup install rust windows",
            "rust",
            [
                new("winget", ["winget", "install", "-e", "--id", "Rustlang.Rustup"]),
                new("scoop", ["scoop", "install", "rustup"])
            ]),
    };
}
