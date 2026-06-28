using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;

namespace ETL_SQL.Analysis.Linting.Rules;

public class SchemaValidationRule : ILintRule
{
    private readonly ILogger? _logger;

    public SchemaValidationRule()
    {
    }

    public SchemaValidationRule(ILogger? logger)
    {
        _logger = logger;
    }

    public string Name => "SchemaValidation";
    public string Description => "Validates that tables and columns exist in the connected sources.";

    public async Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
    {
        var results = new List<LintResult>();
        if (context.Metadata == null) return results;

        var scriptConnections = CollectConnections(script);

        foreach (var statement in script.Statements)
        {
            await AnalyzeStatementAsync(statement, context, results, scriptConnections);
        }
        return results;
    }

    private Dictionary<string, CreateConnectionStatement> CollectConnections(Script script)
    {
        var connections = new Dictionary<string, CreateConnectionStatement>(StringComparer.OrdinalIgnoreCase);
        CollectConnectionsFromStatements(script.Statements, connections);
        return connections;
    }

    private void CollectConnectionsFromStatements(IEnumerable<Statement> statements, Dictionary<string, CreateConnectionStatement> connections)
    {
        foreach (var stmt in statements)
        {
            if (stmt is CreateConnectionStatement conn)
            {
                connections[conn.ConnectionName] = conn;
            }
            else if (stmt is BlockStatement block)
            {
                CollectConnectionsFromStatements(block.Statements, connections);
            }
            else if (stmt is IfStatement ifStmt)
            {
                CollectConnectionsFromStatements(new[] { ifStmt.IfBody }, connections);
                if (ifStmt.ElseIfClauses != null)
                {
                    foreach (var ei in ifStmt.ElseIfClauses)
                    {
                        CollectConnectionsFromStatements(new[] { ei.Body }, connections);
                    }
                }
                if (ifStmt.ElseBody != null)
                {
                    CollectConnectionsFromStatements(new[] { ifStmt.ElseBody }, connections);
                }
            }
            else if (stmt is WhileStatement whileStmt)
            {
                CollectConnectionsFromStatements(new[] { whileStmt.Body }, connections);
            }
            else if (stmt is ForStatement forStmt)
            {
                CollectConnectionsFromStatements(new[] { forStmt.Body }, connections);
            }
            else if (stmt is ForeachStatement foreachStmt)
            {
                CollectConnectionsFromStatements(new[] { foreachStmt.Body }, connections);
            }
            else if (stmt is TryCatchStatement tryCatch)
            {
                CollectConnectionsFromStatements(new[] { tryCatch.TryBody }, connections);
                CollectConnectionsFromStatements(new[] { tryCatch.CatchBody }, connections);
            }
        }
    }

    private string? ResolvePath(string path, string documentUri, ILogger? logger)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        try
        {
            if (Path.IsPathRooted(path))
            {
                return Path.GetFullPath(path);
            }

            if (!string.IsNullOrWhiteSpace(documentUri))
            {
                var baseDir = ResolveDocumentDirectory(documentUri, logger);
                if (!string.IsNullOrEmpty(baseDir))
                {
                    return Path.GetFullPath(Path.Combine(baseDir, path));
                }
            }

            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or UnauthorizedAccessException or IOException or UriFormatException)
        {
            logger?.Warning("Failed to resolve schema validation path '{Path}': {Message}", path, ex.Message);
            return null;
        }
    }

    private string? ResolveDocumentDirectory(string documentUri, ILogger? logger)
    {
        try
        {
            if (documentUri.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri(documentUri);
                return Path.GetDirectoryName(uri.LocalPath);
            }

            if (Path.IsPathRooted(documentUri))
            {
                return Path.GetDirectoryName(documentUri);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or UriFormatException or PathTooLongException)
        {
            logger?.Warning("Failed to resolve lint document URI '{DocumentUri}': {Message}", documentUri, ex.Message);
        }

        return null;
    }

    private bool IsSafePathToProbe(string resolvedPath, string documentUri, ILogger? logger)
    {
        if (string.IsNullOrWhiteSpace(resolvedPath)) return false;

        if (IsProtectedPath(resolvedPath))
        {
            logger?.Warning("Skipping schema file existence probe for protected path '{Path}'.", resolvedPath);
            return false;
        }

        var baseDir = ResolveDocumentDirectory(documentUri, logger);
        if (!string.IsNullOrWhiteSpace(baseDir) && !SafePath.IsWithinRoot(baseDir, resolvedPath))
        {
            logger?.Warning("Skipping schema file existence probe outside document root. Path: '{Path}'", resolvedPath);
            return false;
        }

        return true;
    }

    private static bool IsProtectedPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var protectedRoots = OperatingSystem.IsWindows()
            ? new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".git")
            }
            : new[] { "/etc", "/root", "/proc", "/sys", "/dev", "/var/run", "/.ssh", "/.git" };

        return protectedRoots
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Any(root => SafePath.IsWithinRoot(root, fullPath) || string.Equals(Path.GetFullPath(root), fullPath, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<bool> FileExistsAsync(string resolvedPath, ILogger? logger)
    {
        try
        {
            return await Task.Run(() => File.Exists(resolvedPath));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or UnauthorizedAccessException or IOException)
        {
            logger?.Warning("Failed to check schema file path '{Path}': {Message}", resolvedPath, ex.Message);
            return false;
        }
    }

    private async Task AnalyzeStatementAsync(Statement statement, ILintContext context, List<LintResult> results, Dictionary<string, CreateConnectionStatement> scriptConnections)
    {
        if (statement is SelectStatement select)
        {
            var tablesInScope = new List<(string Conn, string Table, string? Alias)>();

            // 1. Resolve Tables in FROM and JOIN
            if (select.FromTable != null)
            {
                await ValidateTableRefAsync(select.FromTable, context, results, tablesInScope, scriptConnections, isInsert: false);
            }
            foreach (var join in select.Joins)
            {
                await ValidateTableRefAsync(join.Table, context, results, tablesInScope, scriptConnections, isInsert: false);
            }

            // 2. Validate Columns
            foreach (var col in select.Columns)
            {
                await ValidateExpressionAsync(col.Expression, context, results, tablesInScope);
            }
            if (select.WhereClause != null) await ValidateExpressionAsync(select.WhereClause, context, results, tablesInScope);
            if (select.GroupBy != null) foreach (var g in select.GroupBy) await ValidateExpressionAsync(g, context, results, tablesInScope);
            if (select.HavingClause != null) await ValidateExpressionAsync(select.HavingClause, context, results, tablesInScope);
            if (select.OrderBy != null) foreach (var o in select.OrderBy) await ValidateExpressionAsync(o.Expression, context, results, tablesInScope);
        }
        else if (statement is InsertStatement insert)
        {
            var tablesInScope = new List<(string Conn, string Table, string? Alias)>();
            await ValidateTableRefAsync(insert.TargetTable, context, results, tablesInScope, scriptConnections, isInsert: true);
            if (insert.SelectQuery != null) await AnalyzeStatementAsync(insert.SelectQuery, context, results, scriptConnections);
            if (insert.Columns != null && tablesInScope.Any())
            {
                var scope = tablesInScope[0];
                var cols = (await context.Metadata!.GetColumnsAsync(scope.Conn, scope.Table)).ToList();

                // Relaxed validation for file connectors: if no columns are found, assume implied schema creation
                string? connType = null;
                if (scriptConnections.TryGetValue(scope.Conn, out var connStmt))
                {
                    connType = connStmt.ConnectionType;
                }
                else
                {
                    connType = context.Metadata!.GetConnectionType(scope.Conn);
                }
                if (cols.Count == 0 && IsFileConnector(connType))
                {
                    return;
                }

                foreach (var col in insert.Columns)
                {
                    if (!cols.Any(c => string.Equals(c, col, StringComparison.OrdinalIgnoreCase)))
                    {
                        results.Add(new LintResult
                        {
                            RuleName = Name,
                            Severity = LintSeverity.Warning,
                            Message = $"Column '{col}' not found in target table '{scope.Table}'.",
                            LineNumber = insert.Line,
                            ColumnNumber = insert.Column
                        });
                    }
                }
            }
        }
        else if (statement is UpdateStatement update)
        {
            var tablesInScope = new List<(string Conn, string Table, string? Alias)>();
            await ValidateTableRefAsync(update.TargetTable, context, results, tablesInScope, scriptConnections, isInsert: false);
            foreach (var assign in update.Assignments)
            {
                await ValidateExpressionAsync(assign.Value, context, results, tablesInScope);
                // Validate target column
                if (tablesInScope.Any())
                {
                    var scope = tablesInScope[0];
                    var cols = await context.Metadata!.GetColumnsAsync(scope.Conn, scope.Table);
                    if (!cols.Any(c => string.Equals(c, assign.ColumnName, StringComparison.OrdinalIgnoreCase)))
                    {
                        results.Add(new LintResult
                        {
                            RuleName = Name,
                            Severity = LintSeverity.Warning,
                            Message = $"Column '{assign.ColumnName}' not found in target table '{scope.Table}'.",
                            LineNumber = assign.Line,
                            ColumnNumber = assign.Column
                        });
                    }
                }
            }
            if (update.WhereClause != null) await ValidateExpressionAsync(update.WhereClause, context, results, tablesInScope);
        }
        else if (statement is DeleteStatement delete)
        {
            var tablesInScope = new List<(string Conn, string Table, string? Alias)>();
            await ValidateTableRefAsync(delete.TargetTable, context, results, tablesInScope, scriptConnections, isInsert: false);
            if (delete.WhereClause != null) await ValidateExpressionAsync(delete.WhereClause, context, results, tablesInScope);
        }
        else if (statement is BlockStatement block)
        {
            foreach (var s in block.Statements) await AnalyzeStatementAsync(s, context, results, scriptConnections);
        }
        else if (statement is IfStatement ifStmt)
        {
            await AnalyzeStatementAsync(ifStmt.IfBody, context, results, scriptConnections);
            if (ifStmt.ElseIfClauses != null)
                foreach (var ei in ifStmt.ElseIfClauses) await AnalyzeStatementAsync(ei.Body, context, results, scriptConnections);
            if (ifStmt.ElseBody != null) await AnalyzeStatementAsync(ifStmt.ElseBody, context, results, scriptConnections);
        }
        else if (statement is WhileStatement whileStmt)
        {
            await AnalyzeStatementAsync(whileStmt.Body, context, results, scriptConnections);
        }
        else if (statement is ForStatement forStmt)
        {
            await AnalyzeStatementAsync(forStmt.Body, context, results, scriptConnections);
        }
        else if (statement is ForeachStatement foreachStmt)
        {
            await AnalyzeStatementAsync(foreachStmt.Body, context, results, scriptConnections);
        }
        else if (statement is TryCatchStatement tryCatch)
        {
            await AnalyzeStatementAsync(tryCatch.TryBody, context, results, scriptConnections);
            await AnalyzeStatementAsync(tryCatch.CatchBody, context, results, scriptConnections);
        }
    }

    private async Task ValidateTableRefAsync(TableReference tableRef, ILintContext context, List<LintResult> results, List<(string Conn, string Table, string? Alias)> tablesInScope, Dictionary<string, CreateConnectionStatement> scriptConnections, bool isInsert)
    {
        var logger = context.Logger ?? _logger;

        if (tableRef.Subquery != null)
        {
            // Recursive analysis for subqueries if needed
            return;
        }

        var connName = tableRef.ConnectionName ?? context.Metadata!.GetConnections().FirstOrDefault() ?? "DEFAULT";
        string? connType = null;
        CreateConnectionStatement? connStmt = null;
        if (scriptConnections.TryGetValue(connName, out connStmt))
        {
            connType = connStmt.ConnectionType;
        }
        else
        {
            connType = context.Metadata!.GetConnectionType(connName);
        }

        // Skip validation for engine-side temporary tables (#) or built-in DUAL
        if ((tableRef.ConnectionName == null && tableRef.TableName.StartsWith("#")) ||
            string.Equals(tableRef.TableName, "DUAL", StringComparison.OrdinalIgnoreCase))
        {
            // We use the table name as the connection identity for engine-side tables
            tablesInScope.Add((tableRef.TableName, tableRef.TableName, tableRef.Alias));
            return;
        }

        if (IsFileConnector(connType))
        {
            if (connStmt != null && connStmt.TargetExpression is LiteralExpression targetLit && targetLit.Value is string targetPath)
            {
                if (!isInsert)
                {
                    var resolvedPath = ResolvePath(targetPath, context.DocumentUri, logger);
                    if (!string.IsNullOrEmpty(resolvedPath) && IsSafePathToProbe(resolvedPath, context.DocumentUri, logger))
                    {
                        if (!await FileExistsAsync(resolvedPath, logger))
                        {
                            results.Add(new LintResult
                            {
                                RuleName = Name,
                                Severity = LintSeverity.Warning,
                                Message = $"File '{targetPath}' for connection '{connName}' does not exist.",
                                LineNumber = tableRef.Line,
                                ColumnNumber = tableRef.Column
                            });
                        }
                    }
                }
            }
        }

        if (string.Equals(tableRef.TableName, "FILE", StringComparison.OrdinalIgnoreCase))
        {
            if (IsFileConnector(connType))
            {
                tablesInScope.Add((connName, tableRef.TableName, tableRef.Alias));
                return;
            }
        }

        // Skip validation for DOCKER (b1)
        if (connType == "DOCKER")
        {
            tablesInScope.Add((connName, tableRef.TableName, tableRef.Alias));
            return;
        }

        var tables = await context.Metadata!.GetTablesAsync(connName);
        if (tables == null) return;

        string searchName = NormalizeName(tableRef.TableName);
        if (!tables.Any(t => string.Equals(NormalizeName(t), searchName, StringComparison.OrdinalIgnoreCase)))
        {
            results.Add(new LintResult
            {
                RuleName = Name,
                Severity = LintSeverity.Warning,
                Message = $"Table '{tableRef.TableName}' not found in connection '{connName}'.",
                LineNumber = tableRef.Line,
                ColumnNumber = tableRef.Column
            });
        }
        else
        {
            tablesInScope.Add((connName, tableRef.TableName, tableRef.Alias));
        }
    }

    private string NormalizeName(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        string clean = name.Trim('[', ']', '"');
        if (clean.Contains('.')) clean = clean.Split('.').Last();
        return clean;
    }

    private async Task ValidateExpressionAsync(Expression expr, ILintContext context, List<LintResult> results, List<(string Conn, string Table, string? Alias)> tablesInScope)
    {
        if (expr is IdentifierExpression id)
        {
            // Handle qualified names (table.col or conn.table.col)
            var parts = id.Name.Split('.');
            if (parts.Length == 1)
            {
                if (parts[0] == "*") return; // Asterisk is a meta-column, skip physical validation

                // Unqualified column - check all tables in scope
                bool found = false;
                foreach (var scope in tablesInScope)
                {
                    // Skip validation for engine-managed temporary tables (#)
                    if (scope.Table.StartsWith("#"))
                    {
                        found = true;
                        break;
                    }

                    var cols = await context.Metadata!.GetColumnsAsync(scope.Conn, scope.Table);
                    if (cols.Any(c => string.Equals(c, parts[0], StringComparison.OrdinalIgnoreCase)))
                    {
                        found = true;
                        break;
                    }
                }

                if (!found && tablesInScope.Any())
                {
                    results.Add(new LintResult
                    {
                        RuleName = Name,
                        Severity = LintSeverity.Warning,
                        Message = $"Column '{parts[0]}' not found in any table in the current scope.",
                        LineNumber = id.Line,
                        ColumnNumber = id.Column
                    });
                }
            }
            else if (parts.Length == 2)
            {
                // table.col or alias.col
                var qualifier = parts[0];
                var colName = parts[1];
                var scope = tablesInScope.FirstOrDefault(s =>
                    string.Equals(s.Alias, qualifier, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(s.Table, qualifier, StringComparison.OrdinalIgnoreCase));

                if (scope != default)
                {
                    // Skip validation for engine-managed temporary tables (#)
                    if (scope.Table.StartsWith("#")) return;

                    var cols = await context.Metadata!.GetColumnsAsync(scope.Conn, scope.Table);
                    if (!cols.Any(c => string.Equals(c, colName, StringComparison.OrdinalIgnoreCase)))
                    {
                        results.Add(new LintResult
                        {
                            RuleName = Name,
                            Severity = LintSeverity.Warning,
                            Message = $"Column '{colName}' not found in table '{scope.Table}'.",
                            LineNumber = id.Line,
                            ColumnNumber = id.Column
                        });
                    }
                }
            }
        }
        else if (expr is BinaryExpression binary)
        {
            await ValidateExpressionAsync(binary.Left, context, results, tablesInScope);
            await ValidateExpressionAsync(binary.Right, context, results, tablesInScope);
        }
        else if (expr is FunctionCallExpression call)
        {
            foreach (var arg in call.Arguments) await ValidateExpressionAsync(arg, context, results, tablesInScope);
        }
        // Add more expression types (Unary, etc.)
    }

    private bool IsFileConnector(string? type)
    {
        if (string.IsNullOrEmpty(type)) return false;
        var t = type.ToUpperInvariant();
        return t == "FLATFILE" || t == "CSV" || t == "EXCEL" || t == "JSON" ||
               t == "XML" || t == "AVRO" || t == "PARQUET";
    }
}
