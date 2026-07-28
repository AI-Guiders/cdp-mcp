using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cdp.Core;
using Cdp.Lsp;
using Cdp.ScriptableIde;
using TypescriptLang;
using Tool = ModelContextProtocol.Protocol.Tool;

namespace CdpMcp;
internal static partial class IdeLanguageTools
{
    public static IEnumerable<Tool> BuildBareVerbTools()
    {
        yield return Tool("go_to_definition", "IDE: go to definition. Routes by session language (Roslyn / TS worker / LSP). 1-based line/column.", PositionalSchema());
        yield return Tool("find_usages", "IDE: find references/usages. Harness routes by session language.", PositionalSchema());
        yield return Tool("get_document_symbols", "IDE: outline symbols in a file.", new { type = "object", properties = new { file_path = new { type = "string" }, language = new { type = "string", description = "optional override from [languages] config" } }, required = new[] { "file_path" } });
        yield return Tool("get_symbol_at_position", "IDE: symbol / hover at position.", PositionalSchema());
        yield return Tool("get_diagnostics", "IDE: diagnostics for a file (prefer over host ReadLints when language is open).", new { type = "object", properties = new { file_path = new { type = "string" }, language = new { type = "string", description = "optional override" } }, required = new[] { "file_path" } });
        yield return Tool("get_completions", "IDE: IntelliSense (Ctrl+Space). Completions at caret with rendered XML docs (summary/params). csharp first; injects open buffer text.", new { type = "object", properties = new { file_path = new { type = "string" }, line = new { type = "integer", description = "1-based caret" }, column = new { type = "integer", description = "1-based caret" }, prefix = new { type = "string", description = "optional filter" }, max = new { type = "integer", description = "cap (default 40)" }, language = new { type = "string" }, solution_or_project_path = new { type = "string" } }, required = new[] { "file_path", "line", "column" } });
        yield return Tool("get_signature_help", "IDE: signature help inside a call — overloads + parameter XML docs (VS tip, text). csharp first.", new { type = "object", properties = new { file_path = new { type = "string" }, line = new { type = "integer", description = "1-based inside call" }, column = new { type = "integer", description = "1-based inside call" }, language = new { type = "string" }, solution_or_project_path = new { type = "string" } }, required = new[] { "file_path", "line", "column" } });
        yield return Tool("find", "IDE: Find in buffer (VS Ctrl+F). Same shelf as get_completions — query= → hits with line/column/anchor. Alias: get_find. scope=project → find_in_files.", FindSchema());
        yield return Tool("get_find", "Alias of find (discoverability next to get_completions).", FindSchema());
        yield return Tool("find_in_files", "IDE: Find in Files (rg). query=; optional path=/glob=/regex=. Alias: find_all with scope=project.", FindInFilesSchema());
        yield return Tool("find_all", "IDE: find all hits — buffer by default; scope=project = Find in Files.", FindSchema());
        yield return Tool("take", "IDE: verify-then-ship (inverse of put). Buffer/span → body + chat_markdown. Paste chat_markdown into reply. Alias: get_take. check=false skips verify; force=true ships despite errors. PlantUML: PNG on preview_path by default; vision=true|see=true attaches ImageContent for the agent (opt-in).", TakeSchema());
        yield return Tool("get_take", "Alias of take (discoverability next to get_completions).", TakeSchema());
        yield return Tool("resolve_project_root", "Resolve project root / language markers from a path (or return session project after cdp_open).", new { type = "object", properties = new { path = new { type = "string", description = "optional; omit to echo session project" } } });
        yield return Tool("get_workspace_navigation_context", "IDE: Semantic Map related/subgraph (csharp). After cdp_open .sln/.csproj.", new { type = "object", properties = new { file_path = new { type = "string" }, mode = new { type = "string", description = "related | subgraph" }, line = new { type = "integer" }, column = new { type = "integer" }, max_related = new { type = "integer" }, max_nodes = new { type = "integer" }, max_edges = new { type = "integer" }, include_kinds = new { type = "array", items = new { type = "string" } }, exclude_kinds = new { type = "array", items = new { type = "string" } }, preset = new { type = "string" }, language = new { type = "string" }, solution_or_project_path = new { type = "string" } }, required = new[] { "file_path", "mode" } });
        yield return Tool("rename_symbol", "IDE: rename symbol (LSP textDocument/rename). language with [[languages.lsp]] preset (e.g. python).", new { type = "object", properties = new { file_path = new { type = "string" }, line = new { type = "integer", description = "1-based" }, column = new { type = "integer", description = "1-based" }, new_name = new { type = "string" }, language = new { type = "string" }, apply = new { type = "boolean", description = "default true: write workspace edit to disk/buffers" } }, required = new[] { "file_path", "line", "column", "new_name" } });
        yield return Tool("code_actions", "IDE: list LSP code actions at position (then apply_code_action). Python: prefer basedpyright (auto-import); open pyright often returns []. Unused-import removal is Pylance-only.", PositionalSchema());
        yield return Tool("apply_code_action", "IDE: apply code action by index from last code_actions on this LSP session.", new { type = "object", properties = new { action_index = new { type = "integer", description = "0-based from code_actions" }, language = new { type = "string" }, apply = new { type = "boolean", description = "default true" } }, required = new[] { "action_index" } });
    }

    private static object FindSchema() => new
    {
        type = "object",
        properties = new
        {
            query = new
            {
                type = "string",
                description = "needle (aliases: text, pattern)"
            },
            path = new
            {
                type = "string",
                description = "file; default open buffer / project-relative"
            },
            file_path = new
            {
                type = "string",
                description = "alias of path"
            },
            scope = new
            {
                type = "string",
                description = "buffer|project|files|external (default buffer)"
            },
            regex = new
            {
                type = "boolean"
            },
            ignore_case = new
            {
                type = "boolean"
            },
            glob = new
            {
                type = "string",
                description = "find_in_files: rg --glob"
            },
            max = new
            {
                type = "integer"
            }
        },
        required = new[]
        {
            "query"
        }
    };
    private static object FindInFilesSchema() => new
    {
        type = "object",
        properties = new
        {
            query = new
            {
                type = "string"
            },
            path = new
            {
                type = "string",
                description = "optional subdir under project; absolute for scope=external"
            },
            scope = new
            {
                type = "string",
                description = "project|files|external (default project)"
            },
            regex = new
            {
                type = "boolean"
            },
            ignore_case = new
            {
                type = "boolean"
            },
            glob = new
            {
                type = "string"
            },
            max = new
            {
                type = "integer"
            }
        },
        required = new[]
        {
            "query"
        }
    };
    private static object TakeSchema() => new
    {
        type = "object",
        properties = new
        {
            path = new
            {
                type = "string",
                description = "file; default open buffer"
            },
            file_path = new
            {
                type = "string",
                description = "alias of path"
            },
            anchor = new
            {
                type = "string",
                description = "csharp [F:;M:;K:] span"
            },
            start_line = new
            {
                type = "integer"
            },
            end_line = new
            {
                type = "integer"
            },
            start_column = new
            {
                type = "integer"
            },
            end_column = new
            {
                type = "integer"
            },
            fence = new
            {
                type = "string",
                description = "markdown fence override (csharp|mermaid|…)"
            },
            kind = new
            {
                type = "string",
                description = "alias of fence"
            },
            check = new
            {
                type = "boolean",
                description = "default true — run available verify"
            },
            force = new
            {
                type = "boolean",
                description = "ship despite verify errors"
            },
            vision = new
            {
                type = "boolean",
                description = "opt-in: attach ImageContent (agent vision). Default false — use preview_path + Read instead"
            },
            see = new
            {
                type = "boolean",
                description = "alias of vision="
            },
            sniper = new
            {
                type = "boolean",
                description = "take sniper hold span"
            },
            scope = new
            {
                type = "string",
                description = "diagnostics scope; default: project if path under open root (not .cdp/scratch), else syntax"
            }
        }
    };
    private static object PositionalSchema() => new
    {
        type = "object",
        properties = new
        {
            file_path = new
            {
                type = "string"
            },
            line = new
            {
                type = "integer",
                description = "1-based"
            },
            column = new
            {
                type = "integer",
                description = "1-based"
            },
            language = new
            {
                type = "string",
                description = "optional override from [languages] config"
            },
            solution_or_project_path = new
            {
                type = "string",
                description = "csharp escape; default session after cdp_open"
            }
        },
        required = new[]
        {
            "file_path",
            "line",
            "column"
        }
    };
    private static Tool Tool(string name, string desc, object schema) => new()
    {
        Name = name,
        Description = desc,
        InputSchema = JsonSerializer.SerializeToElement(schema)
    };
}