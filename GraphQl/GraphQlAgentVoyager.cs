#nullable enable
using Cdp.ScriptableIde;
using HotChocolate.Types;
using HotChocolate.Execution;

namespace CdpMcp.GraphQl;

/// <summary>
/// Agent-native Voyager: one live schema node + Kind:Nav edges.
/// Context A/C: default <c>detail=pulse</c>; <c>detail=full</c> for args/descriptions.
/// </summary>
internal static class GraphQlAgentVoyager
{
    public const string GoOrgan = "cdp_graphql";

    internal static object Frame(
        string? typeName = null,
        string? filter = null,
        string? anchorWire = null,
        string? detail = null)
    {
        var level = NormalizeDetail(detail);
        var exec = CdpGraphQlRuntime.EnsureExecutorAsync().GetAwaiter().GetResult();
        if (exec is null)
        {
            return new
            {
                ok = false,
                schema = CdpGraphqlChannel.SchemaVersion,
                role = "voyager",
                detail = level,
                error = "executor_cold",
                warm = CdpGraphQlRuntime.LastWarmError,
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
                detail = level,
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
        var full = level == "full";

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
            string? nav = targetType is null ? null : NavWire(targetType);
            if (targetType is not null && edgeTypes.Add(targetType))
                edges.Add(Edge(targetType, viaField: f.Name, viaArg: null, nav!, full));

            if (full)
            {
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
                        edges.Add(Edge(argTarget, viaField: f.Name, viaArg: a.Name, NavWire(argTarget), full));
                }

                fields.Add(new
                {
                    name = f.Name,
                    args,
                    type = returnPrint,
                    nav,
                    description = string.IsNullOrWhiteSpace(f.Description) ? null : f.Description
                });
            }
            else
            {
                // pulse: name → type → nav; arg tax = count + names only
                var argNames = f.Arguments.Select(static a => a.Name).ToArray();
                fields.Add(new
                {
                    name = f.Name,
                    type = returnPrint,
                    arg_count = argNames.Length,
                    args = argNames.Length == 0 ? null : argNames,
                    nav
                });
            }
        }

        var pulse = $"voyager · {node.Name} · fields×{fields.Count} · edges×{edges.Count}"
            + (filterNorm is null ? "" : " · filter=" + filterNorm)
            + (full ? " · full" : " · pulse");

        return new
        {
            ok = true,
            schema = CdpGraphqlChannel.SchemaVersion,
            role = "voyager",
            detail = level,
            pulse,
            type = node.Name,
            kind = "OBJECT",
            filter = filterNorm,
            field_count = fields.Count,
            fields,
            edges,
            nav_self = NavWire(node.Name),
            nav_root = NavWire("Query"),
            http = full ? CdpGraphQlRegistration.HttpPath : null,
            sdl_path = full ? CdpGraphQlRegistration.SchemaPath : null,
            hint = full
                ? "Voyager full. Land edges: cdp_land / op=voyager type=|anchor=."
                : "Voyager pulse [A]. detail=full for args shapes + descriptions + http/sdl paths."
        };
    }

    static object Edge(string type, string viaField, string? viaArg, string nav, bool full) =>
        full
            ? new
            {
                type,
                via_field = viaField,
                via_arg = viaArg,
                nav,
                hint = "cdp_land anchor=… or cdp_graphql op=voyager type=" + type
            }
            : new
            {
                type,
                via = viaArg is null ? viaField : viaField + "." + viaArg,
                nav
            };

    static string NormalizeDetail(string? detail)
    {
        var d = (detail ?? "pulse").Trim().ToLowerInvariant();
        return d switch
        {
            "full" or "fat" or "c" or "map" => "full",
            "pulse" or "slim" or "a" or "" => "pulse",
            _ => "pulse"
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
