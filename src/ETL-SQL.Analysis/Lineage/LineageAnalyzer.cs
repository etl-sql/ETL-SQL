using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Common;

namespace ETL_SQL.Analysis.Lineage
{
    public class LineageAnalyzer
    {
        public ILineageTracker Tracker { get; }
        
        public LineageAnalyzer(ILineageTracker tracker)
        {
            Tracker = tracker;
        }

        public void Analyze(Script script)
        {
            AnalyzeStatements(script.Statements);
        }

        private void AnalyzeStatements(IEnumerable<Statement> statements)
        {
            foreach (var stmt in statements)
            {
                AnalyzeStatement(stmt);
            }
        }

        private void AnalyzeStatement(Statement stmt)
        {
            if (stmt is BlockStatement block)
            {
                AnalyzeStatements(block.Statements);
            }
            else if (stmt is IfStatement ifStmt)
            {
                AnalyzeStatement(ifStmt.IfBody);
                if (ifStmt.ElseIfClauses != null)
                {
                    foreach (var ei in ifStmt.ElseIfClauses) AnalyzeStatement(ei.Body);
                }
                if (ifStmt.ElseBody != null) AnalyzeStatement(ifStmt.ElseBody);
            }
            else if (stmt is WhileStatement whileStmt)
            {
                AnalyzeStatement(whileStmt.Body);
            }
            else if (stmt is ForStatement forStmt)
            {
                // Record the loop counter variable so its source appears in the lineage graph
                var counterMeta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["loop_context"] = $"FOR {forStmt.VariableName} counter"
                };
                Tracker.Record("VARIABLE", Enumerable.Empty<string>(), "FOR_LOOP", targetColumn: forStmt.VariableName, metadata: counterMeta, line: forStmt.Line, column: forStmt.Column);
                AnalyzeStatement(forStmt.Body);
            }
            else if (stmt is ForeachStatement foreachStmt)
            {
                // Extract source tables — subquery ListExpressions don't implement GetSourceTables
                var sourceTables = foreachStmt.ListExpression is SubqueryExpression subq && subq.Query is SelectStatement innerSel
                    ? innerSel.GetSourceTables().ToList()
                    : foreachStmt.ListExpression.GetSourceTables().ToList();

                var loopMeta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["loop_context"] = $"FOREACH {foreachStmt.VariableName} iterates {(sourceTables.Any() ? string.Join(", ", sourceTables) : foreachStmt.ListExpression.ToSql())}"
                };
                Tracker.Record("VARIABLE", sourceTables, "FOREACH_LOOP", targetColumn: foreachStmt.VariableName, metadata: loopMeta, line: foreachStmt.Line, column: foreachStmt.Column);
                AnalyzeStatement(foreachStmt.Body);
            }
            else if (stmt is SetOperationStatement setOp)
            {
                AnalyzeStatement(setOp.Left);
                AnalyzeStatement(setOp.Right);
            }
            else if (stmt is DeclareStatement dec)
            {
                var sources = dec.InitialValue?.GetSourceTables() ?? Enumerable.Empty<string>();
                var sourceCols = dec.InitialValue?.GetSourceColumns() ?? Enumerable.Empty<string>();
                Tracker.Record("VARIABLE", sources, "DECLARE", targetColumn: dec.VariableName, sourceColumns: sourceCols, metadata: dec.Metadata, line: dec.Line, column: dec.Column, endLine: dec.EndLine, endColumn: dec.EndColumn);
            }
            else if (stmt is SelectStatement sel)
            {
                // Record table-level metadata
                if (sel.FromTable != null && sel.FromTable.Metadata.Count > 0)
                {
                    string tblName = (sel.FromTable.ConnectionName != null ? sel.FromTable.ConnectionName + "." : "") + sel.FromTable.TableName;
                    Tracker.Record(tblName, Enumerable.Empty<string>(), "TABLE_TAGS", metadata: sel.FromTable.Metadata, line: sel.FromTable.Line, column: sel.FromTable.Column);
                }
                foreach (var join in sel.Joins)
                {
                    if (join.Table.Metadata.Count > 0)
                    {
                        string tblName = (join.Table.ConnectionName != null ? join.Table.ConnectionName + "." : "") + join.Table.TableName;
                        Tracker.Record(tblName, Enumerable.Empty<string>(), "TABLE_TAGS", metadata: join.Table.Metadata, line: join.Table.Line, column: join.Table.Column);
                    }
                }

                string target = (sel.IntoTable?.ConnectionName != null ? sel.IntoTable.ConnectionName + "." + sel.IntoTable.TableName : sel.IntoTable?.TableName) ?? "RESULTSET";
                
                // Create table mapping for alias/unqualified resolution
                var tableMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (sel.FromTable != null) tableMapping[sel.FromTable.Alias ?? sel.FromTable.TableName] = sel.FromTable.TableName;
                foreach (var join in sel.Joins) tableMapping[join.Table.Alias ?? join.Table.TableName] = join.Table.TableName;

                for (int colIndex = 0; colIndex < sel.Columns.Count; colIndex++)
                {
                    var col = sel.Columns[colIndex];
                    var sourceCols = col.Expression.GetSourceColumns().ToList();
                    var rawSources = col.Expression.GetSourceTables().ToList();
                    var resolvedSources = rawSources.Select(s => tableMapping.TryGetValue(s, out var real) ? real : s).ToList();

                    if (!resolvedSources.Any() && (sel.FromTable != null || sel.Joins.Any()))
                    {
                        resolvedSources = sel.GetSourceTables().ToList();
                    }

                    var inherited = Tracker.InheritMetadata(resolvedSources, sourceCols, out var derived)
                        ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    // Merge static tags from the column itself (e.g. /* @d: ... */)
                    foreach (var m in col.Metadata) inherited[m.Key] = m.Value;

                    // @pii: true wins — if any source carries pii=true, propagate it
                    if (!inherited.ContainsKey("pii") || !inherited["pii"].Equals("true", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var src in resolvedSources)
                        {
                            foreach (var srcCol in sourceCols)
                            {
                                var srcMeta = Tracker.GetColumnMetadata(src, srcCol);
                                if (srcMeta != null && srcMeta.TryGetValue("pii", out var piiVal) && piiVal.Equals("true", StringComparison.OrdinalIgnoreCase))
                                {
                                    inherited["pii"] = "true";
                                    break;
                                }
                            }
                        }
                    }

                    string alias = col.Alias ?? (col.Expression is IdentifierExpression id ? id.Name.Split('.').Last() : $"expr_{colIndex + 1}");

                    // Update AST node for IDE hover persistence
                    col.Metadata = inherited;
                    col.DerivedFromDescriptions = derived;

                    var kind = ClassifyExpression(col.Expression);
                    var exprSql = kind != TransformationKind.PassThrough ? col.Expression.ToSql() : null;
                    var fns = CollectFunctions(col.Expression);

                    Tracker.Record(target, resolvedSources, "SELECT", targetColumn: alias, sourceColumns: sourceCols, metadata: inherited, derivedFromDescriptions: derived, line: col.Line, column: col.Column, endLine: col.EndLine, endColumn: col.EndColumn, transformationKind: kind, transformationExpression: exprSql, functionsApplied: fns.Count > 0 ? fns : null);
                }
            }
            else if (stmt is InsertStatement ins)
            {
                string target = (ins.TargetTable.ConnectionName != null ? ins.TargetTable.ConnectionName + "." + ins.TargetTable.TableName : ins.TargetTable.TableName);
                var sources = ins.GetSourceTables();
                Tracker.Record(target, sources, "INSERT", line: ins.Line, column: ins.Column, endLine: ins.EndLine, endColumn: ins.EndColumn);
                if (ins.SelectQuery != null) AnalyzeStatement(ins.SelectQuery);
            }
            else if (stmt is BulkInsertStatement bulk)
            {
                string t = (bulk.TargetTable.ConnectionName != null ? bulk.TargetTable.ConnectionName + "." + bulk.TargetTable.TableName : bulk.TargetTable.TableName);
                Tracker.Record(t, new[] { bulk.FilePath }, "BULK INSERT", metadata: bulk.Metadata, line: bulk.Line, column: bulk.Column, endLine: bulk.EndLine, endColumn: bulk.EndColumn);
            }
            else if (stmt is UpdateStatement upd)
            {
                string t = (upd.TargetTable.ConnectionName != null ? upd.TargetTable.ConnectionName + "." + upd.TargetTable.TableName : upd.TargetTable.TableName);
                var aliases = AliasScanner.Scan(upd.ToSql());
                
                // Ensure target table and from/join tables are in the alias map
                void AddToAliases(TableReference tbl)
                {
                    var alias = tbl.Alias ?? tbl.TableName;
                    if (!aliases.ContainsKey(alias))
                    {
                        aliases[alias] = new AliasInfo(tbl.TableName, tbl.Alias);
                    }
                }

                AddToAliases(upd.TargetTable);
                if (upd.FromTable != null) AddToAliases(upd.FromTable);
                if (upd.Joins != null)
                {
                    foreach (var join in upd.Joins) AddToAliases(join.Table);
                }
                
                Tracker.Record(t, Enumerable.Empty<string>(), "UPDATE", line: upd.Line, column: upd.Column, endLine: upd.EndLine, endColumn: upd.EndColumn);

                // Column-level lineage for assignments
                foreach (var a in upd.Assignments)
                {
                    var rawSrcTables = a.Value.GetSourceTables();
                    var srcTables = rawSrcTables.Select(s => aliases.TryGetValue(s, out var info) ? info.TableName : s).ToList();
                    // If no source tables found, default to target table
                    if (srcTables.Count == 0) srcTables.Add(t);

                    var srcCols = a.Value.GetSourceColumns();
                    var inherited = Tracker.InheritMetadata(srcTables, srcCols, out var derived)
                        ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    var kind = ClassifyExpression(a.Value);
                    var fns = CollectFunctions(a.Value);

                    Tracker.Record(
                        t,
                        srcTables,
                        "UPDATE COLUMN",
                        targetColumn: a.ColumnName,
                        sourceColumns: srcCols,
                        metadata: inherited,
                        derivedFromDescriptions: derived,
                        line: a.Line,
                        column: a.Column,
                        transformationKind: kind,
                        transformationExpression: kind != TransformationKind.PassThrough ? a.Value.ToSql() : null,
                        functionsApplied: fns.Count > 0 ? fns : null);
                }
            }
            else if (stmt is MergeStatement merge)
            {
                string t = (merge.TargetTable.ConnectionName != null ? merge.TargetTable.ConnectionName + "." + merge.TargetTable.TableName : merge.TargetTable.TableName);
                var aliases = AliasScanner.Scan(merge.ToSql());
                Tracker.Record(t, merge.GetSourceTables(), "MERGE", line: merge.Line, column: merge.Column, endLine: merge.EndLine, endColumn: merge.EndColumn);

                // Static column-level lineage for MERGE actions
                var sTable = merge.SourceTable.Alias ?? merge.SourceTable.TableName;
                
                var allClauses = merge.MatchedClauses.Cast<MergeActionClause>()
                    .Concat(merge.NotMatchedClauses);

                foreach (var clause in allClauses)
                {
                    if (clause is MergeUpdateClause mergeUpd && mergeUpd.Assignments != null)
                    {
                        foreach (var a in mergeUpd.Assignments)
                        {
                            var rawSrcTables = a.Value.GetSourceTables();
                            var srcTables = rawSrcTables.Select(s => s.Equals("S", StringComparison.OrdinalIgnoreCase) || s.Equals(merge.SourceTable.Alias, StringComparison.OrdinalIgnoreCase) ? sTable : (aliases.TryGetValue(s, out var info) ? info.TableName : s)).ToList();
                            var srcCols = a.Value.GetSourceColumns();
                            var inherited = Tracker.InheritMetadata(srcTables, srcCols, out var derivedUpdate)
                                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            var mkind = ClassifyExpression(a.Value);
                            var mfns = CollectFunctions(a.Value);

                            Tracker.Record(t, srcTables, "MERGE UPDATE", targetColumn: a.ColumnName, sourceColumns: srcCols, metadata: inherited, derivedFromDescriptions: derivedUpdate, line: a.Line, column: a.Column, transformationKind: mkind, transformationExpression: mkind != TransformationKind.PassThrough ? a.Value.ToSql() : null, functionsApplied: mfns.Count > 0 ? mfns : null);
                        }
                    }
                    else if (clause is MergeInsertClause mergeIns && mergeIns.Values != null)
                    {
                        for (int i = 0; i < (mergeIns.Columns?.Count ?? 0) && i < mergeIns.Values.Count; i++)
                        {
                            var val = mergeIns.Values[i];
                            var targetCol = mergeIns.Columns![i];
                            var rawSrcTables = val.GetSourceTables();
                            var srcTables = rawSrcTables.Select(s => s.Equals("S", StringComparison.OrdinalIgnoreCase) || s.Equals(merge.SourceTable.Alias, StringComparison.OrdinalIgnoreCase) ? sTable : (aliases.TryGetValue(s, out var info) ? info.TableName : s)).ToList();
                            var srcCols = val.GetSourceColumns();
                            var inherited = Tracker.InheritMetadata(srcTables, srcCols, out var derivedInsert)
                                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            var ikind = ClassifyExpression(val);
                            var ifns = CollectFunctions(val);

                            Tracker.Record(t, srcTables, "MERGE INSERT", targetColumn: targetCol, sourceColumns: srcCols, metadata: inherited, derivedFromDescriptions: derivedInsert, line: val.Line, column: val.Column, transformationKind: ikind, transformationExpression: ikind != TransformationKind.PassThrough ? val.ToSql() : null, functionsApplied: ifns.Count > 0 ? ifns : null);
                        }
                    }
                }
            }
            else if (stmt is ExecuteRemoteBlockStatement execBlock)
            {
                // Recursive analysis of the block
                AnalyzeStatements(execBlock.Body.Statements);
            }
            else if (stmt is ExecutePushdownStatement pushdown)
            {
                var sources = pushdown.GetSourceTables().ToList();
                string target = (pushdown.IntoTable?.ConnectionName != null ? pushdown.IntoTable.ConnectionName + "." + pushdown.IntoTable.TableName : pushdown.IntoTable?.TableName) ?? "RESULTSET";

                Tracker.Record(target, sources, "EXECUTE PUSHDOWN", line: pushdown.Line, column: pushdown.Column, endLine: pushdown.EndLine, endColumn: pushdown.EndColumn);
            }
            else if (stmt is CreateDatasetStatement dataset)
            {
                string target = $"dataset:{dataset.TempTableName}";
                var sources = dataset.SourceQuery.GetSourceTables().ToList();
                Tracker.Record(target, sources, "CREATE DATASET", line: dataset.Line, column: dataset.Column, endLine: dataset.EndLine, endColumn: dataset.EndColumn);
                AnalyzeStatement(dataset.SourceQuery);
            }
            else if (stmt is CreateVisualStatement visual)
            {
                string target = $"report:{visual.Name}";
                List<string> sources;
                if (visual.Source.IsInlineSelect && visual.Source.InlineSelect != null)
                {
                    sources = visual.Source.InlineSelect.GetSourceTables().ToList();
                    AnalyzeStatement(visual.Source.InlineSelect);
                }
                else if (visual.Source.TempTableName != null)
                {
                    sources = new List<string> { visual.Source.TempTableName };
                }
                else
                {
                    sources = new List<string>();
                }
                Tracker.Record(target, sources, "CREATE VISUAL", line: visual.Line, column: visual.Column, endLine: visual.EndLine, endColumn: visual.EndColumn);

                foreach (var mapping in visual.Mappings)
                {
                    Tracker.Record(target, sources, "CREATE VISUAL",
                        targetColumn: mapping.Role,
                        sourceColumns: new[] { mapping.Column },
                        line: mapping.Line,
                        column: ((AstNode)mapping).Column);
                }
            }
        }

        private static readonly HashSet<string> _aggregateFunctions = new(StringComparer.OrdinalIgnoreCase)
        {
            "SUM", "COUNT", "AVG", "MIN", "MAX", "STDEV", "STDEVP", "VAR", "VARP",
            "STRING_AGG", "LISTAGG", "GROUP_CONCAT", "ARRAY_AGG",
            "PERCENTILE_CONT", "PERCENTILE_DISC", "FIRST_VALUE", "LAST_VALUE",
            "MEDIAN", "MODE", "CORR", "COVAR_POP", "COVAR_SAMP"
        };

        private static readonly HashSet<string> _castFunctions = new(StringComparer.OrdinalIgnoreCase)
        {
            "CAST", "TRY_CAST", "CONVERT", "TRY_CONVERT", "SAFE_CAST", "TO_DATE",
            "TO_TIMESTAMP", "TO_NUMBER", "TO_CHAR", "TO_VARCHAR", "TO_DECIMAL"
        };

        private static readonly HashSet<string> _conditionalFunctions = new(StringComparer.OrdinalIgnoreCase)
        {
            "COALESCE", "ISNULL", "NULLIF", "IIF", "NVL", "NVL2", "IFNULL",
            "DECODE", "GREATEST", "LEAST", "ZEROIFNULL", "NULLIFZERO"
        };

        private static readonly HashSet<TokenType> _arithmeticOps = new()
        {
            TokenType.PLUS, TokenType.MINUS, TokenType.STAR, TokenType.SLASH, TokenType.MODULO
        };

        public static TransformationKind ClassifyExpression(Expression expr) => expr switch
        {
            LiteralExpression => TransformationKind.Literal,
            SubqueryExpression => TransformationKind.Subquery,
            CaseExpression => TransformationKind.CaseExpression,
            FunctionCallExpression fc when fc.Window != null => TransformationKind.WindowFunction,
            FunctionCallExpression fc when _aggregateFunctions.Contains(fc.FunctionName) => TransformationKind.Aggregation,
            FunctionCallExpression fc when _castFunctions.Contains(fc.FunctionName) => TransformationKind.Cast,
            FunctionCallExpression fc when _conditionalFunctions.Contains(fc.FunctionName) => TransformationKind.Conditional,
            FunctionCallExpression => TransformationKind.FunctionCall,
            BinaryExpression be when be.Operator == TokenType.CONCAT || (be.Operator == TokenType.PLUS && IsStringConcat(be)) => TransformationKind.StringOperation,
            BinaryExpression be when _arithmeticOps.Contains(be.Operator) => TransformationKind.Arithmetic,
            IdentifierExpression => TransformationKind.PassThrough,
            MemberAccessExpression => TransformationKind.PassThrough,
            _ => TransformationKind.Unknown
        };

        private static bool IsStringConcat(BinaryExpression be)
        {
            // Heuristic: if either side is a string literal, treat + as string concat
            return be.Left is LiteralExpression { Type: TokenType.STRING_LITERAL }
                || be.Right is LiteralExpression { Type: TokenType.STRING_LITERAL };
        }

        public static List<string> CollectFunctions(Expression expr)
        {
            var result = new List<string>();
            CollectFunctionsImpl(expr, result);
            return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static void CollectFunctionsImpl(Expression expr, List<string> result)
        {
            switch (expr)
            {
                case FunctionCallExpression fc:
                    result.Add(fc.FunctionName.ToUpperInvariant());
                    foreach (var arg in fc.Arguments) CollectFunctionsImpl(arg, result);
                    break;
                case BinaryExpression be:
                    CollectFunctionsImpl(be.Left, result);
                    CollectFunctionsImpl(be.Right, result);
                    break;
                case CaseExpression ce:
                    foreach (var (cond, res) in ce.WhenClauses)
                    {
                        CollectFunctionsImpl(cond, result);
                        CollectFunctionsImpl(res, result);
                    }
                    if (ce.ElseResult != null) CollectFunctionsImpl(ce.ElseResult, result);
                    break;
            }
        }
    }
}
