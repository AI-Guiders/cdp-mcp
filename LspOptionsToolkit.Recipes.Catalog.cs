using Cdp.Lsp;

namespace CdpMcp;

/// <summary>Recipe catalog for LspOptionsToolkit (≤ADX soft-warn peel).</summary>
internal sealed partial class LspOptionsToolkit
{
    sealed record ViaSpec(string Via, string[] Argv);

    sealed record Recipe(
        string Id,
        string Title,
        string Package,
        string SearchQuery,
        LspLaunchPreset Preset,
        ViaSpec[] Vias);

    static readonly Dictionary<string, Recipe> Recipes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["python"] = new(
            "python",
            "Python (basedpyright)",
            "basedpyright",
            "basedpyright-langserver npm install windows",
            LspLaunchPreset.DefaultPython,
            [
                new("npm", ["npm", "install", "-g", "basedpyright"]),
                new("pip", ["pip", "install", "basedpyright"]),
                new("pipx", ["pipx", "install", "basedpyright"])
            ]),
        ["go"] = new(
            "go",
            "Go (gopls)",
            "gopls",
            "gopls install golang.org/x/tools/gopls",
            new LspLaunchPreset
            {
                Id = "go",
                Command = "gopls",
                CommandCandidates = ["gopls"],
                Args = ["serve"],
                LanguageIds = ["go"],
                RootMarkers = ["go.mod", ".git"]
            },
            [new("go", ["go", "install", "golang.org/x/tools/gopls@latest"])]),
        ["rust"] = new(
            "rust",
            "Rust (rust-analyzer)",
            "rust-analyzer",
            "rust-analyzer install rustup component",
            new LspLaunchPreset
            {
                Id = "rust",
                Command = "rust-analyzer",
                CommandCandidates = ["rust-analyzer"],
                Args = [],
                LanguageIds = ["rust"],
                RootMarkers = ["Cargo.toml", ".git"]
            },
            [
                new("rustup", ["rustup", "component", "add", "rust-analyzer"]),
                new("scoop", ["scoop", "install", "rust-analyzer"])
            ]),
        ["yaml"] = new(
            "yaml",
            "YAML language server",
            "yaml-language-server",
            "yaml-language-server npm install",
            new LspLaunchPreset
            {
                Id = "yaml",
                Command = "yaml-language-server",
                CommandCandidates = ["yaml-language-server"],
                Args = ["--stdio"],
                LanguageIds = ["yaml"],
                RootMarkers = [".git"]
            },
            [new("npm", ["npm", "install", "-g", "yaml-language-server"])]),
        ["json"] = new(
            "json",
            "JSON language server",
            "vscode-langservers-extracted",
            "vscode-json-language-server npm install",
            new LspLaunchPreset
            {
                Id = "json",
                Command = "vscode-json-language-server",
                CommandCandidates = ["vscode-json-language-server", "vscode-json-languageserver"],
                Args = ["--stdio"],
                LanguageIds = ["json"],
                RootMarkers = [".git"]
            },
            [new("npm", ["npm", "install", "-g", "vscode-langservers-extracted"])]),
        ["markdown"] = new(
            "markdown",
            "Markdown (marksman)",
            "marksman",
            "marksman language server scoop install",
            new LspLaunchPreset
            {
                Id = "markdown",
                Command = "marksman",
                CommandCandidates = ["marksman"],
                Args = ["server"],
                LanguageIds = ["markdown"],
                RootMarkers = [".git"]
            },
            [
                new("scoop", ["scoop", "install", "marksman"]),
                new("winget", ["winget", "install", "-e", "--id", "artempyanykh.marksman"])
            ]),
        ["typescript"] = new(
            "typescript",
            "TypeScript (typescript-language-server)",
            "typescript-language-server",
            "typescript-language-server npm install",
            new LspLaunchPreset
            {
                Id = "typescript",
                Command = "typescript-language-server",
                CommandCandidates = ["typescript-language-server", "typescript-language-server.cmd"],
                Args = ["--stdio"],
                LanguageIds = ["typescript", "javascript"],
                RootMarkers = ["tsconfig.json", "package.json", ".git"]
            },
            [new("npm", ["npm", "install", "-g", "typescript-language-server", "typescript"])]),
    };
}
