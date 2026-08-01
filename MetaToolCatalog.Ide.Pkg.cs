#nullable enable
using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace CdpMcp;

/// <summary>ListTools Meta catalog — Ide pkg/project/sln peel (soft-warn).</summary>
internal static partial class MetaToolCatalog
{
    static IEnumerable<Tool> IdePkg() =>
    [
    Meta("cdp_pkg_find", "Search packages (NuGet or npm by session language). Prefer over shell.", new
    {
        type = "object",
        properties = new
        {
            query = new { type = "string" },
            take = new { type = "integer", description = "Default 5, max 25" }
        },
        required = new[] { "query" }
    }),
    Meta("cdp_pkg_list", "List dependencies of session project.", new
    {
        type = "object",
        properties = new
        {
            path = new { type = "string", description = "Optional .csproj / package root override" }
        }
    }),
    Meta("cdp_pkg_add", "Add package reference (dotnet add / npm install) to session project.", new
    {
        type = "object",
        properties = new
        {
            id = new { type = "string", description = "Package id" },
            version = new { type = "string", description = "Optional version; omit = latest stable tooling default" },
            path = new { type = "string", description = "Optional .csproj / package root (needed if session is .sln)" }
        },
        required = new[] { "id" }
    }),
    Meta("cdp_pkg_remove", "Remove package reference from session project.", new
    {
        type = "object",
        properties = new
        {
            id = new { type = "string" },
            path = new { type = "string" }
        },
        required = new[] { "id" }
    }),
    Meta("cdp_pkg_update", "Update package (NuGet: add version; npm: update/install @ver).", new
    {
        type = "object",
        properties = new
        {
            id = new { type = "string" },
            version = new { type = "string" },
            path = new { type = "string" }
        },
        required = new[] { "id" }
    }),
    Meta("cdp_pkg_outdated", "List outdated packages for session project.", new
    {
        type = "object",
        properties = new { path = new { type = "string" } }
    }),
    Meta("cdp_project_scene", "Project map before create (git_scene analogue): curated templates (VS-like), session anchors, existing csproj/sln. Optional include_installed=true → dotnet new list. Prefer before inventing files / guessing template=.", new
    {
        type = "object",
        properties = new
        {
            root = new { type = "string", description = "Scan root (default session work root)" },
            include_installed = new { type = "boolean", description = "Also parse `dotnet new list --type project` (capped)" },
            max_existing = new { type = "integer", description = "Cap for existing projects (default 40)" },
            max_installed = new { type = "integer", description = "Cap for installed templates (default 80)" }
        }
    }),
    Meta("cdp_project_create", "Scaffold project. csharp: tfm_policy (+ tfm=). typescript: engine_policy (+ engines=). Policies prefer_most_used|latest|lts|specified — LTS from vendor meta (dotnet releases-index release-type / node dist lts). Optional open=true. Prefer cdp_project_scene first.", new
    {
        type = "object",
        properties = new
        {
            output_dir = new { type = "string", description = "Directory for the new project" },
            name = new { type = "string", description = "Project name (default = folder name)" },
            template = new { type = "string", description = "dotnet new template id (default console)" },
            tfm_policy = new { type = "string", description = "prefer_most_used|latest|lts|specified (default prefer_most_used)" },
            tfm = new { type = "string", description = "Required when tfm_policy=specified (e.g. net10.0)" },
            engine_policy = new { type = "string", description = "typescript: prefer_most_used|latest|lts|specified" },
            engines = new { type = "string", description = "Required when engine_policy=specified (e.g. >=20)" },
            force = new { type = "boolean" },
            open = new { type = "boolean", description = "cdp_open the created project into session (default true)" }
        },
        required = new[] { "output_dir" }
    }),
    Meta("cdp_project_list", "List .csproj / tsconfig under root (default session project root).", new
    {
        type = "object",
        properties = new { root = new { type = "string" } }
    }),
    Meta("cdp_project_close", "Clear session project anchor (language/root/solution/tsconfig).", new
    {
        type = "object",
        properties = new { }
    }),
    Meta("cdp_project_add_to_sln", "Add csproj to solution (session .sln/.slnx or solution=). Alias of cdp_sln_add.", new
    {
        type = "object",
        properties = new
        {
            project = new { type = "string", description = "Path to .csproj" },
            solution = new { type = "string", description = "Path to .sln/.slnx (default: session or unique in work root)" },
            in_root = new { type = "boolean", description = "dotnet sln add --in-root" },
            solution_folder = new { type = "string", description = "dotnet sln add --solution-folder" }
        },
        required = new[] { "project" }
    }),
    Meta("cdp_sln_create", "Scaffold solution (dotnet new sln). Optional open=true into session.", new
    {
        type = "object",
        properties = new
        {
            output_dir = new { type = "string" },
            name = new { type = "string" },
            force = new { type = "boolean" },
            open = new { type = "boolean", description = "cdp_open the created .sln/.slnx (default true)" }
        },
        required = new[] { "output_dir" }
    }),
    Meta("cdp_sln_list", "List .sln / .slnx under root (default session work root).", new
    {
        type = "object",
        properties = new { root = new { type = "string" } }
    }),
    Meta("cdp_sln_projects", "List projects in a solution (dotnet sln list).", new
    {
        type = "object",
        properties = new { solution = new { type = "string" } }
    }),
    Meta("cdp_sln_add", "Add project to solution (dotnet sln add).", new
    {
        type = "object",
        properties = new
        {
            project = new { type = "string" },
            solution = new { type = "string" },
            in_root = new { type = "boolean" },
            solution_folder = new { type = "string" }
        },
        required = new[] { "project" }
    }),
    Meta("cdp_sln_remove", "Remove project from solution (dotnet sln remove).", new
    {
        type = "object",
        properties = new
        {
            project = new { type = "string" },
            solution = new { type = "string" }
        },
        required = new[] { "project" }
    }),
    ];
}
