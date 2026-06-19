using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;

namespace ETL_SQL.Core.Planning
{
    public static class SemiJoinPushdownOptimizer
    {
        public static async Task<SelectStatement> OptimizeAsync(SelectStatement stmt, IExecutionContext context)
        {
            if (stmt.Joins == null || stmt.Joins.Count == 0) return stmt;
            if (stmt.FromTable == null) return stmt;

            var leftTableName = stmt.FromTable.TableName;
            // Left side must be a local #temp table
            if (!leftTableName.StartsWith("#") || !context.Connections.TryGetValue(leftTableName, out var localSource))
            {
                return stmt;
            }

            // Materialize the local rows (since it's small, this is cheap)
            List<Row> localRows;
            try
            {
                localRows = new List<Row>();
                await foreach (var batch in localSource.ReadBatches())
                {
                    localRows.AddRange(batch.Rows);
                }
            }
            catch
            {
                // If we fail to read the local table, fall back
                return stmt;
            }

            // Conservative limit: 1 to 1000 rows
            if (localRows.Count == 0 || localRows.Count > 1000)
            {
                return stmt;
            }

            var leftAlias = stmt.FromTable.Alias ?? stmt.FromTable.TableName;
            var newJoins = new List<JoinClause>();
            bool modified = false;

            foreach (var join in stmt.Joins)
            {
                if (join.IsApply || join.Table.ConnectionName == null || !context.IsSqlPushdown(join.Table.ConnectionName))
                {
                    newJoins.Add(join);
                    continue;
                }

                var rightAlias = join.Table.Alias ?? join.Table.TableName;
                
                // Extract equijoin keys
                if (TryGetEquijoinKeys(join.Condition, leftAlias, rightAlias, out var leftKey, out var rightKey))
                {
                    // Collect non-null, unique key values
                    var keyValues = new HashSet<object>();
                    foreach (var row in localRows)
                    {
                        object? val = null;
                        if (row.HasColumn(leftKey!)) val = row[leftKey!];
                        else if (row.HasColumn($"{leftAlias}.{leftKey}")) val = row[$"{leftAlias}.{leftKey}"];

                        if (val != null && val != DBNull.Value)
                        {
                            keyValues.Add(val);
                        }
                    }

                    // A bounded set of keys (e.g. 1 to 1000 keys)
                    if (keyValues.Count > 0 && keyValues.Count <= 1000)
                    {
                        // Build the rewritten subquery:
                        // SELECT * FROM remote.Table WHERE rightKey IN (keyValues)
                        var columns = new List<SelectColumn> { new SelectColumn(new IdentifierExpression("*"), null) };
                        var fromTableRef = new TableReference(
                            tableName: join.Table.TableName,
                            schemaName: join.Table.SchemaName,
                            databaseName: join.Table.DatabaseName,
                            connectionName: join.Table.ConnectionName
                        );

                        var inList = keyValues.Select(v => (Expression)new LiteralExpression(v, GetLiteralTokenType(v))).ToList();
                        var inExpr = new InExpression(new IdentifierExpression(rightKey!), new ListExpression(inList), false);

                        var subquery = new SelectStatement(columns, null, fromTableRef, new List<JoinClause>(), inExpr);

                        var newTableRef = new TableReference(
                            tableName: join.Table.TableName,
                            schemaName: join.Table.SchemaName,
                            databaseName: join.Table.DatabaseName,
                            connectionName: join.Table.ConnectionName,
                            alias: join.Table.Alias ?? join.Table.TableName,
                            subquery: subquery
                        );

                        // Attach Explain visibility metadata
                        newTableRef.Metadata["SEMI_JOIN_PUSHDOWN"] = $"[SEMI-JOIN PUSHDOWN ON {leftAlias}.{leftKey} ({keyValues.Count} keys)]";

                        newJoins.Add(new JoinClause(join.JoinType, newTableRef, join.Condition, join.Hint, join.KeepBest));
                        modified = true;
                        continue;
                    }
                }

                newJoins.Add(join);
            }

            if (!modified) return stmt;

            return stmt with { Joins = newJoins };
        }

        private static bool TryGetEquijoinKeys(Expression? cond, string leftAlias, string rightAlias, out string? leftKey, out string? rightKey)
        {
            leftKey = null;
            rightKey = null;
            if (cond is BinaryExpression bin && bin.Operator == TokenType.EQUALS)
            {
                if (bin.Left is IdentifierExpression lid && bin.Right is IdentifierExpression rid)
                {
                    string lName = lid.Name;
                    string rName = rid.Name;

                    var lParts = lName.Split('.');
                    string? lAlias = lParts.Length >= 2 ? lParts[lParts.Length - 2] : null;
                    string lBare = lParts[lParts.Length - 1];

                    var rParts = rName.Split('.');
                    string? rAlias = rParts.Length >= 2 ? rParts[rParts.Length - 2] : null;
                    string rBare = rParts[rParts.Length - 1];

                    if ((lAlias == null || lAlias.Equals(leftAlias, StringComparison.OrdinalIgnoreCase)) &&
                        (rAlias == null || rAlias.Equals(rightAlias, StringComparison.OrdinalIgnoreCase)))
                    {
                        leftKey = lBare;
                        rightKey = rBare;
                        return true;
                    }

                    if ((rAlias == null || rAlias.Equals(leftAlias, StringComparison.OrdinalIgnoreCase)) &&
                        (lAlias == null || lAlias.Equals(rightAlias, StringComparison.OrdinalIgnoreCase)))
                    {
                        leftKey = rBare;
                        rightKey = lBare;
                        return true;
                    }
                }
            }
            return false;
        }

        private static TokenType GetLiteralTokenType(object val)
        {
            if (val is int || val is long || val is decimal || val is double || val is float)
            {
                return TokenType.NUMBER;
            }
            return TokenType.STRING_LITERAL;
        }
    }
}
