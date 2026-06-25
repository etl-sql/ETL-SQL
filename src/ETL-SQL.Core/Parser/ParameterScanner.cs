using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ETL_SQL.Core.Parser;
/// <summary>
/// Scans an AST node recursively for variable/parameter usage.
/// Eliminates false positives from comments or literal strings by acting on the AST nodes.
/// </summary>
public static class ParameterScanner
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> TraversableProperties = new();

    public static HashSet<string> Scan(AstNode? node)
    {
        var vars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (node == null) return vars;
        ScanRecursive(node, vars);
        return vars;
    }

    private static void ScanRecursive(object? obj, HashSet<string> vars)
    {
        if (obj == null) return;

        if (obj is VariableExpression vex)
        {
            vars.Add(vex.Name);
            return;
        }

        if (obj is ParameterExpression pex)
        {
            vars.Add(pex.Value);
            return;
        }

        // Optimization for common collections in AST
        if (obj is IEnumerable enumerable && !(obj is string))
        {
            foreach (var item in enumerable) ScanRecursive(item, vars);
            return;
        }

        // Reflect on properties to walk the tree
        var type = obj.GetType();
        if (type.IsPrimitive || type == typeof(string) || type.IsEnum) return;

        foreach (var prop in TraversableProperties.GetOrAdd(type, GetTraversableProperties))
        {
            try
            {
                var val = prop.GetValue(obj);
                if (val != null) ScanRecursive(val, vars);
            }
            catch { /* skip inaccessible properties */ }
        }
    }

    private static PropertyInfo[] GetTraversableProperties(Type type) =>
        type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.Name is not ("Line" or "Column" or "EndLine" or "EndColumn"))
            .Where(p => p.GetIndexParameters().Length == 0)
            .ToArray();
}
