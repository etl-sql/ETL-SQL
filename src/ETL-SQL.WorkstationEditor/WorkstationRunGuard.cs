using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using CoreParser = ETL_SQL.Core.Parser.Parser;

namespace ETL_SQL.WorkstationEditor;

/// <summary>
/// Flags statements that would destroy persistent data, so the editor can require an explicit
/// confirmation before running them.
/// </summary>
/// <remarks>
/// The engine already has <c>MutationGuardrailPolicy</c>, but it returns early unless the process
/// is enrolled in enterprise policy — a standalone workstation never is. Local convenience is not
/// a reason to run an unguarded DROP, so this applies the same intent without requiring enrollment.
///
/// Session-local <c>#temp</c> targets are exempt: they die with the session and are the normal
/// working material of a script.
/// </remarks>
public static class WorkstationRunGuard
{
    /// <summary>Describes each destructive statement in the text, in order. Empty when there are none.</summary>
    public static IReadOnlyList<string> FindDestructiveStatements(string? scriptText)
    {
        if (string.IsNullOrWhiteSpace(scriptText)) return [];

        Script script;
        try
        {
            script = new CoreParser(new Lexer(scriptText).Tokenize(), scriptText).Parse();
        }
        catch
        {
            // Unparseable text cannot be classified; the run itself will surface the parse error.
            return [];
        }

        var found = new List<string>();
        Walk(script.Statements, found);
        return found;
    }

    private static void Walk(IEnumerable<Statement> statements, List<string> found)
    {
        foreach (var statement in statements)
        {
            if (Describe(statement) is { } description)
                found.Add(description);

            // Destructive work hidden inside control flow still destroys data.
            switch (statement)
            {
                case BlockStatement block:
                    Walk(block.Statements, found);
                    break;
                case IfStatement ifStatement:
                    Walk([ifStatement.IfBody], found);
                    foreach (var clause in ifStatement.ElseIfClauses ?? [])
                        Walk([clause.Body], found);
                    if (ifStatement.ElseBody is not null) Walk([ifStatement.ElseBody], found);
                    break;
                case WhileStatement whileStatement:
                    Walk([whileStatement.Body], found);
                    break;
                case ForStatement forStatement:
                    Walk([forStatement.Body], found);
                    break;
                case ForeachStatement foreachStatement:
                    Walk([foreachStatement.Body], found);
                    break;
                case TryCatchStatement tryCatch:
                    Walk([tryCatch.TryBody, tryCatch.CatchBody], found);
                    break;
            }
        }
    }

    private static string? Describe(Statement statement) => statement switch
    {
        DropTableStatement s when IsPersistent(s.TargetTable.TableName) =>
            $"DROP TABLE {Qualify(s.TargetTable)} (line {s.Line})",
        TruncateTableStatement s when IsPersistent(s.TargetTable.TableName) =>
            $"TRUNCATE TABLE {Qualify(s.TargetTable)} (line {s.Line})",
        DeleteStatement s when IsPersistent(s.TargetTable.TableName) =>
            $"DELETE FROM {Qualify(s.TargetTable)} (line {s.Line})",
        MergeStatement s when IsPersistent(s.TargetTable.TableName) =>
            $"MERGE INTO {Qualify(s.TargetTable)} (line {s.Line})",
        _ => null
    };

    /// <summary>
    /// Renders the connection-qualified name. The warning has to say which database is about to
    /// lose a table — a bare "DROP TABLE Users" does not tell the user what they are approving.
    /// </summary>
    private static string Qualify(TableReference table) =>
        string.IsNullOrEmpty(table.ConnectionName)
            ? table.TableName
            : $"{table.ConnectionName}.{table.TableName}";

    private static bool IsPersistent(string tableName) => !tableName.StartsWith('#');
}
