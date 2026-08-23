using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;

namespace ETL_SQL.Analysis.Linting.Rules;

/// <summary>
/// Validates CREATE BOOKMARK declarations:
///   - Duplicate bookmark identifiers
///   - Multiple DEFAULT bookmarks
///   - Undefined page references
///   - Undefined visual/container references in STATE
///   - Undeclared parameter references
///   - APPLY_BOOKMARK referencing unknown bookmarks
/// </summary>
public sealed class BookmarkValidationRule : ILintRule
{
    public string Name => "BookmarkValidation";
    public string Description => "Validates author bookmark declarations, references, defaults, and actions.";

    public Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
    {
        var results = new List<LintResult>();
        var bookmarks = script.Statements.OfType<CreateBookmarkStatement>().ToList();

        var pageNames = new HashSet<string>(
            script.Statements.OfType<CreatePageStatement>().Select(p => p.Name),
            StringComparer.OrdinalIgnoreCase);

        var objectNames = new HashSet<string>(
            script.Statements.OfType<CreateVisualStatement>().Select(v => v.Name)
            .Concat(script.Statements.OfType<CreateContainerStatement>().Select(c => c.Name)),
            StringComparer.OrdinalIgnoreCase);

        var declaredParamTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in script.Statements.OfType<DeclareStatement>())
        {
            var name = d.VariableName.StartsWith("@") ? d.VariableName : "@" + d.VariableName;
            declaredParamTypes[name] = d.DataType;
        }

        var bookmarkNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var defaultBookmarks = new List<CreateBookmarkStatement>();

        foreach (var bm in bookmarks)
        {
            if (!bookmarkNames.Add(bm.Name))
            {
                results.Add(Error(bm, $"Duplicate bookmark identifier '{bm.Name}'."));
            }

            if (bm.IsDefault)
                defaultBookmarks.Add(bm);

            if (bm.PageName != null && !pageNames.Contains(bm.PageName))
            {
                results.Add(Error(bm, $"Bookmark '{bm.Name}' references undefined page '{bm.PageName}'."));
            }

            foreach (var param in bm.Parameters)
            {
                var normalized = param.ParameterName.StartsWith("@") ? param.ParameterName : "@" + param.ParameterName;
                if (!declaredParamTypes.TryGetValue(normalized, out var declaredType))
                {
                    results.Add(Error(bm, $"Bookmark '{bm.Name}' sets parameter '{param.ParameterName}' which is not declared in this script."));
                }
                else if (param.Value is LiteralExpression lit && !IsTypeCompatible(declaredType, lit))
                {
                    results.Add(new LintResult
                    {
                        RuleName = Name,
                        Severity = LintSeverity.Warning,
                        Message = $"Bookmark '{bm.Name}' sets '{param.ParameterName}' to a {DescribeLiteral(lit)} value, which does not match its declared type '{declaredType}'.",
                        LineNumber = bm.Line,
                        ColumnNumber = bm.Column
                    });
                }
            }

            foreach (var entry in bm.StateEntries)
            {
                var dotIndex = entry.ObjectKey.IndexOf('.');
                var objectName = dotIndex >= 0 ? entry.ObjectKey[..dotIndex] : entry.ObjectKey;
                if (!objectNames.Contains(objectName))
                {
                    results.Add(Error(bm, $"Bookmark '{bm.Name}' STATE references undefined object '{objectName}'."));
                }
            }
        }

        if (defaultBookmarks.Count > 1)
        {
            foreach (var bm in defaultBookmarks)
                results.Add(Error(bm, $"Multiple bookmarks declared as DEFAULT. Only one bookmark may be DEFAULT."));
        }

        ValidateApplyBookmarkActions(script, bookmarkNames, results);

        return Task.FromResult<IEnumerable<LintResult>>(results);
    }

    private void ValidateApplyBookmarkActions(Script script, HashSet<string> bookmarkNames, List<LintResult> results)
    {
        var actionSources = script.Statements.OfType<CreateVisualStatement>()
            .SelectMany(v => v.Actions.OfType<ApplyBookmarkAction>().Select(a => (Statement: (Statement)v, Action: a)))
            .Concat(script.Statements.OfType<CreateButtonStatement>()
                .SelectMany(b => b.Actions.OfType<ApplyBookmarkAction>().Select(a => (Statement: (Statement)b, Action: a))));

        foreach (var (stmt, action) in actionSources)
        {
            if (!bookmarkNames.Contains(action.BookmarkName))
            {
                results.Add(new LintResult
                {
                    RuleName = Name,
                    Severity = LintSeverity.Error,
                    Message = $"APPLY_BOOKMARK references undefined bookmark '{action.BookmarkName}'.",
                    LineNumber = stmt.Line,
                    ColumnNumber = stmt.Column
                });
            }
        }
    }

    private static readonly HashSet<string> NumericTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "INT", "INTEGER", "BIGINT", "SMALLINT", "TINYINT", "DECIMAL", "NUMERIC",
        "FLOAT", "REAL", "DOUBLE", "MONEY", "NUMBER"
    };

    private static readonly HashSet<string> BooleanTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "BIT", "BOOL", "BOOLEAN"
    };

    /// <summary>
    /// Checks a typed literal against a declared parameter type. NULL and variable references are always
    /// compatible (a variable's value is not known statically). String-typed parameters accept any
    /// literal because most declared inputs are strings that carry codes; numeric and boolean columns
    /// are strict so an obvious mismatch is surfaced as a warning.
    /// </summary>
    private static bool IsTypeCompatible(string declaredType, LiteralExpression lit)
    {
        if (lit.Value == null) return true; // NULL is assignable to any type

        // Strip any length/precision suffix, e.g. VARCHAR(50) -> VARCHAR.
        var baseType = declaredType;
        var paren = baseType.IndexOf('(');
        if (paren >= 0) baseType = baseType[..paren];
        baseType = baseType.Trim();

        var isNumberLit = lit.Type == TokenType.NUMBER;
        var isBoolLit = lit.Type is TokenType.TRUE or TokenType.FALSE or TokenType.ON or TokenType.OFF
            || lit.Value is bool;
        var isStringLit = lit.Type == TokenType.STRING_LITERAL || (!isNumberLit && !isBoolLit);

        if (NumericTypes.Contains(baseType)) return isNumberLit;
        if (BooleanTypes.Contains(baseType)) return isBoolLit;
        // String/date/other declared types accept string (and, leniently, numeric) literals.
        return isStringLit || isNumberLit;
    }

    private static string DescribeLiteral(LiteralExpression lit) => lit.Type switch
    {
        TokenType.NUMBER => "numeric",
        TokenType.TRUE or TokenType.FALSE or TokenType.ON or TokenType.OFF => "boolean",
        TokenType.STRING_LITERAL => "string",
        TokenType.NULL => "null",
        _ => lit.Value is bool ? "boolean" : "string"
    };

    private LintResult Error(CreateBookmarkStatement bm, string message) => new()
    {
        RuleName = Name,
        Severity = LintSeverity.Error,
        Message = message,
        LineNumber = bm.Line,
        ColumnNumber = bm.Column
    };
}
