using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core.Parser;

namespace ETL_SQL.Analysis.Linting.Rules
{
    /// <summary>
    /// Enforces that table references for database connectors (MSSQL, POSTGRES, MOCKDB, etc.) 
    /// are qualified with a connection name (e.g., 'conn.table' instead of just 'table').
    /// </summary>
    public class DatabaseQualificationRule : ILintRule
    {
        public string Name => "DatabaseQualification";
        public string Description => "Ensures database table references are qualified with a connection name.";

        private static readonly HashSet<string> DatabaseConnectorTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "MSSQL", "POSTGRES", "ORACLE", "MYSQL", "SQLITE", "ODBC", "MOCKDB", "SNOWFLAKE", "REDSHIFT", "BIGQUERY"
        };

        public async Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
        {
            var results = new List<LintResult>();
            if (context.Metadata == null) return results;

            var connections = context.Metadata.GetConnections().ToList();
            var dbConnections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Identify which connections are database-type
            // Note: This requires the MetadataProvider to expose connection types if possible, 
            // or we look at the currently registered connections in the script.

            // For now, we'll check the statements to see what connections are declared
            foreach (var statement in script.Statements)
            {
                await AnalyzeStatementAsync(statement, context, results, DatabaseConnectorTypes);
            }

            return results;
        }

        private async Task AnalyzeStatementAsync(Statement statement, ILintContext context, List<LintResult> results, HashSet<string> dbTypes)
        {
            if (statement is SelectStatement select)
            {
                if (select.FromTable != null) await ValidateTableRefAsync(select.FromTable, context, results, dbTypes);
                foreach (var join in select.Joins) await ValidateTableRefAsync(join.Table, context, results, dbTypes);

                if (select.FromTable?.Subquery != null) await AnalyzeStatementAsync(select.FromTable.Subquery, context, results, dbTypes);
                foreach (var join in select.Joins) if (join.Table.Subquery != null) await AnalyzeStatementAsync(join.Table.Subquery, context, results, dbTypes);
            }
            else if (statement is InsertStatement insert)
            {
                await ValidateTableRefAsync(insert.TargetTable, context, results, dbTypes);
                if (insert.SelectQuery != null) await AnalyzeStatementAsync(insert.SelectQuery, context, results, dbTypes);
            }
            else if (statement is UpdateStatement update)
            {
                await ValidateTableRefAsync(update.TargetTable, context, results, dbTypes);
            }
            else if (statement is DeleteStatement delete)
            {
                await ValidateTableRefAsync(delete.TargetTable, context, results, dbTypes);
            }
            else if (statement is MergeStatement merge)
            {
                await ValidateTableRefAsync(merge.TargetTable, context, results, dbTypes);
                await ValidateTableRefAsync(merge.SourceTable, context, results, dbTypes);
            }
            else if (statement is BlockStatement block)
            {
                foreach (var s in block.Statements) await AnalyzeStatementAsync(s, context, results, dbTypes);
            }
            else if (statement is IfStatement ifStmt)
            {
                await AnalyzeStatementAsync(ifStmt.IfBody, context, results, dbTypes);
                if (ifStmt.ElseIfClauses != null) foreach (var ei in ifStmt.ElseIfClauses) await AnalyzeStatementAsync(ei.Body, context, results, dbTypes);
                if (ifStmt.ElseBody != null) await AnalyzeStatementAsync(ifStmt.ElseBody, context, results, dbTypes);
            }
            else if (statement is WhileStatement whileStmt)
            {
                await AnalyzeStatementAsync(whileStmt.Body, context, results, dbTypes);
            }
            // Add other statement types as needed
        }

        private async Task ValidateTableRefAsync(TableReference tableRef, ILintContext context, List<LintResult> results, HashSet<string> dbTypes)
        {
            if (tableRef.Subquery != null) return;

            var tableName = tableRef.TableName;

            // Skip temp tables and variables
            if (tableName.StartsWith("#") || tableName.StartsWith("@") || tableName.Equals("DUAL", StringComparison.OrdinalIgnoreCase)) return;

            // If it's already qualified, we're good
            if (!string.IsNullOrEmpty(tableRef.ConnectionName)) return;

            // Get the list of connections from the context metadata
            var connections = context.Metadata?.GetConnections().ToList() ?? new List<string>();
            if (connections.Count <= 1) return; // Only one connection, unqualified name is fine

            // If we have multiple connections, we look for the "default" connection or check types
            // In ETL-SQL, if not qualified, it often defaults to the first declared connection 
            // but for safety in multi-source scripts, we want to encourage qualification.

            bool isDbConnection = false;
            foreach (var conn in connections)
            {
                var type = context.Metadata?.GetConnectionType(conn);
                if (type != null && dbTypes.Contains(type))
                {
                    isDbConnection = true;
                    break;
                }
            }

            if (isDbConnection)
            {
                results.Add(new LintResult
                {
                    RuleName = Name,
                    Severity = LintSeverity.Warning,
                    Message = $"Reference to table '{tableName}' should be qualified with a connection name (e.g. 'conn.{tableName}') in scripts with multiple connections.",
                    LineNumber = tableRef.Line,
                    ColumnNumber = tableRef.Column
                });
            }
        }
    }
}
