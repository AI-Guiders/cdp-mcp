#nullable enable
using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace CdpMcp;

/// <summary>ListTools Meta catalog — Ide lifecycle (build→test_plan); pkg peel in Ide.Pkg.</summary>
internal static partial class MetaToolCatalog
{
    static IEnumerable<Tool> IdeLifecycle() =>
    [
    Meta("cdp_build", "IDE Build: session project after cdp_open. Harness picks projection (csharp→dotnet / typescript→npm|tsc). Prefer over shell. Default detail=auto: green→pulse; fail→errors[].",
    new
    {
        type = "object",
        properties = new
        {
            path = new { type = "string", description = "Optional .sln/.csproj/tsconfig; default = session after cdp_open" },
            solution_path = new { type = "string", description = "Alias of path (csharp)" },
            configuration = new { type = "string", description = "Debug|Release (csharp)" },
            framework = new { type = "string" },
            no_restore = new { type = "boolean" },
            detail = new { type = "string", description = "auto (default: green→pulse, fail→slim) | pulse | slim | full" },
            include_raw_output = new { type = "boolean", description = "forces detail=full" },
            timeout_seconds = new { type = "integer" }
        }
    }),
    Meta("cdp_run", "IDE Run: session project. csharp→dotnet run; typescript→npm start|dev. Prefer over shell.", new
    {
        type = "object",
        properties = new
        {
            path = new { type = "string", description = "Optional project path; default = session" },
            configuration = new { type = "string" },
            no_build = new { type = "boolean", description = "Pass --no-build (csharp)" },
            timeout_seconds = new { type = "integer", description = "Default 120" },
            additional_arguments = new
            {
                type = "array",
                items = new { type = "string" },
                description = "Args after -- passed to the app (csharp)"
            }
        }
    }),
    Meta("cdp_test", "IDE Test: session project. csharp→dotnet test; typescript→npm test. Prefer over shell. Prefer cdp_test_scene first. Default detail=auto: green→pulse only; fail→failed_tests[].",
    new
    {
        type = "object",
        properties = new
        {
            path = new { type = "string", description = "Optional .sln/.csproj/package root; default = session" },
            solution_path = new { type = "string", description = "Alias of path (csharp)" },
            configuration = new { type = "string" },
            filter = new { type = "string", description = "VSTest --filter" },
            detail = new { type = "string", description = "auto (default: green→pulse, fail→slim) | pulse | slim | full" },
            include_raw_output = new { type = "boolean", description = "forces detail=full" },
            timeout_seconds = new { type = "integer" }
        }
    }),
    Meta("cdp_test_scene", "Test Runner map (git_scene analogue): discover FQNs via `dotnet test --list-tests` + last_run cache. Prefer before cdp_test / shell archaeology.", new
    {
        type = "object",
        properties = new
        {
            path = new { type = "string" },
            solution_path = new { type = "string" },
            configuration = new { type = "string" },
            max_tests = new { type = "integer", description = "Cap discovered FQNs (default 500)" },
            timeout_seconds = new { type = "integer" }
        }
    }),
    Meta("cdp_goto", "VS Ctrl+T Go To All + Ctrl+Q features: fuzzy files/types/members → anchors (top hit auto open+peek). Prefixes: f/t/m/# code; q: desk verbs (or kind=feature). Prefer over find when you know the name.", new
    {
        type = "object",
        properties = new
        {
            query = new { type = "string", description = "Search text; optional prefix 't Foo' / 'f:Bar' / 'q undo'" },
            q = new { type = "string", description = "Alias of query" },
            kind = new { type = "string", description = "all|file|type|member|symbol|feature (default all)" },
            filter = new { type = "string", description = "Alias of kind" },
            max = new { type = "integer", description = "Cap hits (default 40)" },
            peek = new { type = "boolean", description = "Auto open+peek top code hit (default true)" }
        }
    }),
    Meta("cdp_analysis_scene", "Code Analysis domain scene (git_scene/test_scene peer). On demand — not MFD. feature omit → map; correspondence → ADR/docs↔code (+ doc_body reverse, unified context=); semantic_map → related neighbors; clones → VS-style duplicates. path=/anchor=; mode= for semantic_map.", new
    {
        type = "object",
        properties = new
        {
            feature = new { type = "string", description = "omit|scene → map; correspondence|semantic_map|clones" },
            op = new { type = "string", description = "Alias of feature" },
            scope = new { type = "string", description = "clones: file|method|selection|project|solution" },
            path = new { type = "string", description = "file under analysis" },
            anchor = new { type = "string", description = "seed wire [F:;M:;L:]" },
            from = new { type = "string", description = "Alias of anchor" },
            mode = new { type = "string", description = "semantic_map: related|subgraph|… (default related)" },
            preset = new { type = "string", description = "semantic_map: navigation preset id" },
            max_related = new { type = "integer", description = "semantic_map hit cap" },
            line = new { type = "integer", description = "semantic_map optional line" },
            column = new { type = "integer", description = "semantic_map optional column" },
            search_in = new { type = "string", description = "When seed set: file|project|solution (default project if open)" },
            min_statements = new { type = "integer", description = "Default 10 project/solution, 3 local" },
            max_files = new { type = "integer" },
            max_groups = new { type = "integer" },
            start_line = new { type = "integer" },
            end_line = new { type = "integer" }
        }
    }),
    Meta("cdp_script_scene", "Script habitat (put → buffer diagnostics → check → run → report). Not external throwaway CSX. op omit → map; put name=/text= under .cdp/scripts; open|check|run|last|help. Run wraps cdp_csx_run; check = ScriptHost allowlist + buffer diags.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "omit|scene|put|open|check|run|last|help" },
            feature = new { type = "string", description = "Alias of op" },
            name = new { type = "string", description = "put/open/check/run: script file name under .cdp/scripts" },
            path = new { type = "string", description = "script path (absolute or project-relative)" },
            file = new { type = "string", description = "Alias of path/name" },
            text = new { type = "string", description = "put: draft body" },
            body = new { type = "string", description = "put: alias of text" },
            code = new { type = "string", description = "put: alias of text" },
            overwrite = new { type = "boolean", description = "put: replace existing (default true if exists)" },
            mode = new { type = "string", description = "run: run|dry_run" },
            refresh = new { type = "boolean", description = "open: reload from disk" }
        }
    }),
    Meta("cdp_ps1_scene", "PowerShell ISE-analogue habitat (put → buffer → AST check → pwsh -File run → last). Scripts under .cdp/ps1/*.ps1. op omit → map; put|open|check|run|last|help. dry_run = AST-only.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "omit|scene|put|open|check|run|last|help" },
            feature = new { type = "string", description = "Alias of op" },
            name = new { type = "string", description = "put/open/check/run: file name under .cdp/ps1" },
            path = new { type = "string", description = "script path (absolute or project-relative)" },
            file = new { type = "string", description = "Alias of path/name" },
            text = new { type = "string", description = "put: draft body" },
            body = new { type = "string", description = "put: alias of text" },
            code = new { type = "string", description = "put: alias of text" },
            overwrite = new { type = "boolean", description = "put: replace existing" },
            mode = new { type = "string", description = "run: run|dry_run (dry_run=AST check)" },
            refresh = new { type = "boolean", description = "open: reload from disk" },
            timeout_seconds = new { type = "integer", description = "run: process timeout (default 120)" }
        }
    }),
    Meta("cdp_test_plan", "Select tests then preview|apply. include[] FQNs, failed_first=true (from last_run), or filter=. op=preview|apply → structured test_run/v0 + evidence.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "preview|apply (aliases draft|run); default preview" },
            path = new { type = "string" },
            solution_path = new { type = "string" },
            include = new { type = "array", items = new { type = "string" }, description = "FQNs or filter fragments" },
            failed_first = new { type = "boolean", description = "Re-run last_run failed set" },
            filter = new { type = "string", description = "Raw VSTest --filter" },
            configuration = new { type = "string" },
            detail = new { type = "string", description = "auto|pulse|slim|full (same as cdp_test)" },
            include_raw_output = new { type = "boolean" },
            timeout_seconds = new { type = "integer" }
        }
    }),
    ];
}
