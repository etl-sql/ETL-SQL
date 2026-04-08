using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core.Parser;

namespace ETL_SQL.Core
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
                AnalyzeStatement(forStmt.Body);
            }
            else if (stmt is ForeachStatement foreachStmt)
            {
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

                foreach (var col in sel.Columns)
                {
                    var sourceCols = col.Expression.GetSourceColumns().ToList();
                    var rawSources = col.Expression.GetSourceTables().ToList();
                    var resolvedSources = rawSources.Select(s => tableMapping.TryGetValue(s, out var real) ? real : s).ToList();

                    if (!resolvedSources.Any() && (sel.FromTable != null || sel.Joins.Any()))
                    {
                        resolvedSources = sel.GetSourceTables().ToList();
                    }

                    var inherited = Tracker.InheritMetadata(resolvedSources, sourceCols, out var derived);
                    
                    // Merge static tags from the column itself (e.g. /* @d: ... */)
                    foreach (var m in col.Metadata) inherited[m.Key] = m.Value;
                    
                    string alias = col.Alias ?? (col.Expression is IdentifierExpression id ? id.Name.Split('.').Last() : "expr");
                    
                    // Update AST node for IDE hover persistence
                    col.Metadata = inherited;
                    col.DerivedFromDescriptions = derived;
                    
                    Tracker.Record(target, resolvedSources, "SELECT", targetColumn: alias, sourceColumns: sourceCols, metadata: inherited, derivedFromDescriptions: derived, line: col.Line, column: col.Column, endLine: col.EndLine, endColumn: col.EndColumn);
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
                    var inherited = Tracker.InheritMetadata(srcTables, srcCols, out var derived);

                    Tracker.Record(
                        t,
                        srcTables,
                        "UPDATE COLUMN",
                        targetColumn: a.ColumnName,
                        sourceColumns: srcCols,
                        metadata: inherited,
                        derivedFromDescriptions: derived,
                        line: a.Line,
                        column: a.Column);
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
                            var inherited = Tracker.InheritMetadata(srcTables, srcCols, out var derivedUpdate);

                            Tracker.Record(t, srcTables, "MERGE UPDATE", targetColumn: a.ColumnName, sourceColumns: srcCols, metadata: inherited, derivedFromDescriptions: derivedUpdate, line: a.Line, column: a.Column);
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
                            var inherited = Tracker.InheritMetadata(srcTables, srcCols, out var derivedInsert);

                            Tracker.Record(t, srcTables, "MERGE INSERT", targetColumn: targetCol, sourceColumns: srcCols, metadata: inherited, derivedFromDescriptions: derivedInsert, line: val.Line, column: val.Column);
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
        }
    }
}
