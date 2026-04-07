using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using ETL_SQL.Core.Parser;

namespace ETL_SQL.Core.Linting.Rules
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
            if (tableName.StartsWith("#") || tableName.StartsWith("@")) return;

            // If it's already qualified, we're good
            if (!string.IsNullOrEmpty(tableRef.ConnectionName)) return;

            // Check if the default connection (or the only connection) is a database type
            var connections = context.Metadata!.GetConnections().ToList();
            if (connections.Count <= 1) return; // Only one connection, unqualified name is fine

            // For now, if there's only one connection and it's NOT qualified, we warn 
            // IF we can determine it's a DB. 
            // A safer approach for this rule: If it's a one-part name, and the "DEFAULT" or first connection 
            // is known to be a database (which we'll assume for now if it's not a known file type), warn.


            results.Add(new LintResult
            {
                RuleName = Name,
                Severity = LintSeverity.Warning,
                Message = $"Reference to table '{tableName}' should be qualified with a connection name (e.g. 'conn.{tableName}').",
                LineNumber = tableRef.Line,
                ColumnNumber = tableRef.Column
            });
        }
    }
}
