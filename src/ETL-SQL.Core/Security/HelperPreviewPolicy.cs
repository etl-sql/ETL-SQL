using ETL_SQL.Core.Common;
using ETL_SQL.Core.Parser;
using CoreParser = ETL_SQL.Core.Parser.Parser;

namespace ETL_SQL.Core.Security;

/// <summary>Builds an explicit read-only helper attempt without replaying arbitrary predecessors.</summary>
public static class HelperPreviewPolicy
{
    public static List<Statement> Parse(string text)
    {
        var script = new CoreParser(new Lexer(text).Tokenize(), text).Parse();
        if (script.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error) is { } error)
            throw new ArgumentException(error.Message);
        return script.Statements.Where(s => s is not NoOpStatement).ToList();
    }

    public static string Compose(string query, string? context, int? line)
    {
        var target = Parse(query);
        if (target.Count == 0 || target.All(s => s is CreateConnectionStatement)) throw new ArgumentException("Write a query to preview.");
        if (target.Count > 25) throw new ArgumentException("Helper preview is limited to 25 statements.");
        foreach (var statement in target)
        {
            if (statement is CreateConnectionStatement) continue;
            if (statement is ExecutePushdownStatement native)
            {
                var body = Parse(native.SqlText);
                if (native.IntoTable is not null || native.Parameters.Count != 0 || body.Count != 1
                    || !ReadOnlyQueryPolicy.IsReadOnly(body[0]))
                    throw new ArgumentException("Native helper preview requires one read-only SELECT, with no output table or parameters.");
            }
            else if (!ReadOnlyQueryPolicy.IsReadOnly(statement))
                throw new ArgumentException("Helper preview requires a read-only query.");
        }

        if (line is null) return query;
        var lines = (context ?? "").ReplaceLineEndings("\n").Split('\n');
        if (line < 1 || line > lines.Length + 1)
            throw new ArgumentException("Preview context is unavailable: the task position is outside the current script.");
        var prefix = string.Join('\n', lines.Take(line.Value - 1));
        var prefixStatements = Parse(prefix);
        if (prefixStatements.Count + target.Count > 25)
            throw new ArgumentException("Helper preview is limited to 25 statements.");
        foreach (var statement in prefixStatements)
        {
            if (statement is SectionLabelStatement or CreateConnectionStatement) continue;
            if (statement is DeclareStatement { IsSensitive: false, IsSecret: false }) continue;
            if (statement is SelectStatement { IntoTable: not null } stage && stage.IntoTable!.TableName.StartsWith('#')) continue;
            throw new ArgumentException($"Preview context is unavailable: predecessor at line {statement.Line} is not a variable declaration or read-only staging statement. Nothing was executed.");
        }
        // A query preamble may repeat a declaration already carried by the explicit context.
        var declared = prefixStatements.OfType<CreateConnectionStatement>()
            .Select(s => s.ConnectionName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var remaining = target.Where(s => s is not CreateConnectionStatement c || !declared.Contains(c.ConnectionName));
        return prefix + "\n" + string.Join('\n', remaining.Select(s => s.ToSql().TrimEnd(';') + ";"));
    }
}
