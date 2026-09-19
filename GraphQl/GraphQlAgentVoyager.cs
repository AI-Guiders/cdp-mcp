#nullable enable
using Cdp.ScriptableIde;
using HotChocolate.Types;
using HotChocolate.Execution;

namespace CdpMcp.GraphQl;

/// <summary>
/// Agent-native Voyager: one live schema node frame + Kind:Nav edges (cdp_land Command:go).
/// Not SDL dump — traverse like Banana Cake Pop / Voyager, MCP-shaped.
/// </summary>
internal static class GraphQlAgentVoyager
{
    public const string GoOrgan = "cdp_graphql";

    internal static object Frame(
        string? typeName = null,
        string? filter = null,
        string? anchorWire = null)
    {
        var exec = CdpGraphQlRuntime.EnsureExecutorAsync().GetAwaiter().GetResult();
        if (exec is null)
        {
            return new
            {
                ok = false,
                schema = CdpGraphqlChannel.SchemaVersion,
                role = "voyager",
                error = "executor_cold",
                detail = CdpGraphQlRuntime.LastWarmError,
                http = CdpGraphQlRegistration.HttpPath,
                hint = "Schema warm failed — fix HC registration then redeploy."
            };
        }

        if (!string.IsNullOrWhiteSpace(anchorWire)
            && TryTypeFromNavAnchor(anchorWire, out var fromAnchor)
            && !string.IsNullOrWhiteSpace(fromAnchor))
        {
            typeName = fromAnchor;
        }

        typeName = string.IsNullOrWhiteSpace(typeName) ? "Query" : typeName.Trim();
        var node = FindComplex(exec, typeName);
        if (node is null)
        {
            return new
            {
                ok = false,
                schema = CdpGraphqlChannel.SchemaVersion,
                role = "voyager",
                type = typeName,
                error = "type_not_found",
                nav_root = NavWire("Query"),
                hint = "op=voyager type=Query|Packages|… or land Kind:Nav Go:cdp_graphql Member:<Type>"
            };
        }

        var filterNorm = string.IsNullOrWhiteSpace(filter) ? null : filter.Trim();
        var fields = new List<object>();
        var edges = new List<object>();
        var edgeTypes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var f in node.Fields)
        {
            if (f.IsIntrospectionField)
                continue;
            if (filterNorm is not null
                && f.Name.IndexOf(filterNorm, StringComparison.OrdinalIgnoreCase) < 0
                && f.Type.Print().IndexOf(filterNorm, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            var returnPrint = f.Type.Print();
            var targetType = NavigableNamedType(f.Type);
            string? nav = null;
            if (targetType is not null)
            {
                nav = NavWire(targetType);
                if (edgeTypes.Add(targetType))
                {
                    edges.Add(new
                    {
                        type = targetType,
                        via_field = f.Name,
                        nav,
                        hint = "cdp_land anchor=… or cdp_graphql op=voyager type=" + targetType
                    });
                }
            }

            var args = new List<object>();
            foreach (var a in f.Arguments)
            {
                var argTarget = NavigableNamedType(a.Type);
                args.Add(new
                {
                    name = a.Name,
                    type = a.Type.Print(),
                    nav = argTarget is null ? null : NavWire(argTarget)
                });
                if (argTarget is not null && edgeTypes.Add(argTarget))
                {
                    edges.Add(new
                    {
                        type = argTarget,
                        via_arg = a.Name,
                        via_field = f.Name,
                        nav = NavWire(argTarget),
                        hint = "cdp_land anchor=… or cdp_graphql op=voyager type=" + argTarget
                    });
                }
            }

            fields.Add(new
            {
                name = f.Name,
                args,
                type = returnPrint,
                nav,
                description = f.Description
            });
        }

        return new
        {
            ok = true,
            schema = CdpGraphqlChannel.SchemaVersion,
            role = "voyager",
            type = node.Name,
            kind = "OBJECT",
            filter = filterNorm,
            field_count = fields.Count,
            fields,
            edges,
            nav_self = NavWire(node.Name),
            nav_root = NavWire("Query"),
            http = CdpGraphQlRegistration.HttpPath,
            sdl_path = CdpGraphQlRegistration.SchemaPath,
            hint = "Agent Voyager node. Follow edges via cdp_land (Kind:Nav Command:go Go:cdp_graphql Member:<Type>) or op=voyager type=. filter= substrings fields."
        };
    }

    static IObjectTypeDefinition? FindComplex(IRequestExecutor exec, string name)
    {
        if (name.Equals("Query", StringComparison.OrdinalIgnoreCase))
            return exec.Schema.QueryType;

        foreach (var t in exec.Schema.Types)
        {
            if (t is IObjectTypeDefinition ot
                && ot.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                && !ot.Name.StartsWith("__", StringComparison.Ordinal))
            {
                return ot;
            }
        }

        return null;
    }

    static string? NavigableNamedType(IType type)
    {
        var named = type.NamedType();
        if (named.Name.StartsWith("__", StringComparison.Ordinal))
            return null;
        // Object / interface / input / enum — same Voyager click targets; scalars stay leaves.
        if (named is IObjectTypeDefinition or IInterfaceTypeDefinition
            or IInputObjectTypeDefinition or IEnumTypeDefinition)
        {
            return named.Name;
        }

        return null;
    }

    internal static string NavWire(string typeName) =>
        BracketLocate.Format(new BracketLocate.Span(
            File: null,
            MemberKey: typeName,
            LineStart: null,
            LineEnd: null,
            Family: "navigation",
            Command: "go",
            Go: GoOrgan));

    internal static bool TryTypeFromNavAnchor(string wire, out string? typeName)
    {
        typeName = null;
        try
        {
            var span = BracketLocate.Parse(wire);
            if (BracketLocate.ClassifyFamily(span, out _) != BracketLocate.AxisFamily.Navigation)
                return false;
            if (!string.Equals(span.Command, "go", StringComparison.OrdinalIgnoreCase))
                return false;
            if (!IsGraphqlGo(span.Go))
                return false;
            if (string.IsNullOrWhiteSpace(span.MemberKey))
                return false;
            typeName = span.MemberKey.Trim();
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    internal static bool IsGraphqlGo(string? go) =>
        go is not null
        && (go.Equals(GoOrgan, StringComparison.OrdinalIgnoreCase)
            || go.Equals("graphql", StringComparison.OrdinalIgnoreCase)
            || go.Equals("voyager", StringComparison.OrdinalIgnoreCase));
}
