using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core;

namespace ETL_SQL.Engine.Services
{
    public class LineageManager(ILineageTracker tracker)
    {
        private readonly ILineageTracker _tracker = tracker;

        public void RecordSelectIntoLineage(Statement statement, TableReference intoTable, IExecutionContext context)
        {
            string intoName = intoTable.ConnectionName ?? intoTable.TableName;

            if (statement is SelectStatement select)
            {
                var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (select.FromTable != null)
                {
                    var fromTable = (select.FromTable.Alias ?? select.FromTable.TableName);
                    aliases[fromTable] = select.FromTable.TableName;
                    if (select.FromTable.Metadata?.Any() == true)
                        _tracker.Record(select.FromTable.TableName, new[] { select.FromTable.TableName }, "TABLE_TAGS", metadata: select.FromTable.Metadata, line: select.FromTable.Line, column: select.FromTable.Column);
                }
                foreach (var j in select.Joins)
                {
                    var joinTable = (j.Table.Alias ?? j.Table.TableName);
                    aliases[joinTable] = j.Table.TableName;
                    if (j.Table.Metadata?.Any() == true)
                        _tracker.Record(j.Table.TableName, new[] { j.Table.TableName }, "TABLE_TAGS", metadata: j.Table.Metadata, line: j.Table.Line, column: j.Table.Column);
                }

                foreach (var col in select.Columns)
                {
                    string targetCol = col.Alias ?? (col.Expression is IdentifierExpression id ? id.Name.Split('.').Last() : $"Expr{select.Columns.IndexOf(col)}");
                    
                    var resolvedSources = col.Expression.GetSourceTables()
                        .Select(s => aliases.TryGetValue(s, out var real) ? real : s)
                        .ToList();

                    if (!resolvedSources.Any() && select.FromTable != null)
                    {
                        resolvedSources = select.GetSourceTables().ToList();
                    }

                    // Inherit descriptions and amalgamate
                    var sourceCols = col.Expression.GetSourceColumns().ToList();
                    var inherited = _tracker.InheritMetadata(resolvedSources, sourceCols, out var derived);
                    
                    col.DerivedFromDescriptions = derived;
                    foreach (var m in inherited)
                    {
                        if (!col.Metadata.ContainsKey(m.Key)) col.Metadata[m.Key] = m.Value;
                    }

                    _tracker.Record(
                        intoName, 
                        resolvedSources, 
                        "SELECT INTO", 
                        targetColumn: targetCol, 
                        sourceColumns: sourceCols,
                        metadata: col.Metadata,
                        derivedFromDescriptions: col.DerivedFromDescriptions,
                        line: select.Line,
                        column: select.Column);
                }
            }
            else if (statement is SetOperationStatement setOp)
            {
                // For set operations, we derive column lineage from the left-hand query
                if (setOp.Left is SelectStatement leftSelect)
                {
                    foreach (var col in leftSelect.Columns)
                    {
                        string targetCol = col.Alias ?? (col.Expression is IdentifierExpression id ? id.Name.Split('.').Last() : $"Expr{leftSelect.Columns.IndexOf(col)}");
                        _tracker.Record(
                            intoName, 
                            leftSelect.GetSourceTables(), 
                            $"SELECT INTO ({setOp.Operation})", 
                            targetColumn: targetCol, 
                            metadata: col.Metadata,
                            derivedFromDescriptions: col.DerivedFromDescriptions,
                            line: statement.Line,
                            column: statement.Column);
                    }
                }
                else
                {
                    _tracker.Record(intoName, statement.GetSourceTables(), "SELECT INTO", line: statement.Line, column: statement.Column);
                }
            }
            else
            {
                _tracker.Record(intoName, statement.GetSourceTables(), "SELECT INTO", line: statement.Line, column: statement.Column);
            }
        }

        public void RecordCreateDatasetLineage(CreateDatasetStatement statement)
        {
            var target = $"dataset:{statement.TempTableName}";

            _tracker.Record(
                target,
                statement.SourceQuery.GetSourceTables(),
                "CREATE DATASET",
                line: statement.Line,
                column: statement.Column,
                endLine: statement.EndLine,
                endColumn: statement.EndColumn);

            // Key the inner SELECT's column lineage to the dataset target (not the
            // generic "RESULTSET") so a column's source + inherited description
            // persists and can be resolved by dataset name from a separate script.
            if (statement.SourceQuery is SelectStatement select)
            {
                var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (select.FromTable != null)
                {
                    aliases[select.FromTable.Alias ?? select.FromTable.TableName] = select.FromTable.TableName;
                    if (select.FromTable.Metadata?.Any() == true)
                        _tracker.Record(select.FromTable.TableName, new[] { select.FromTable.TableName }, "TABLE_TAGS", metadata: select.FromTable.Metadata, line: select.FromTable.Line, column: select.FromTable.Column);
                }
                foreach (var j in select.Joins)
                {
                    aliases[j.Table.Alias ?? j.Table.TableName] = j.Table.TableName;
                    if (j.Table.Metadata?.Any() == true)
                        _tracker.Record(j.Table.TableName, new[] { j.Table.TableName }, "TABLE_TAGS", metadata: j.Table.Metadata, line: j.Table.Line, column: j.Table.Column);
                }

                foreach (var col in select.Columns)
                {
                    string targetCol = col.Alias ?? (col.Expression is IdentifierExpression id ? id.Name.Split('.').Last() : $"Expr{select.Columns.IndexOf(col)}");

                    var resolvedSources = col.Expression.GetSourceTables()
                        .Select(s => aliases.TryGetValue(s, out var real) ? real : s)
                        .ToList();
                    if (!resolvedSources.Any() && select.FromTable != null)
                        resolvedSources = select.GetSourceTables().ToList();

                    var sourceCols = col.Expression.GetSourceColumns().ToList();
                    var inherited = _tracker.InheritMetadata(resolvedSources, sourceCols, out var derived);
                    col.DerivedFromDescriptions = derived;
                    foreach (var m in inherited)
                        if (!col.Metadata.ContainsKey(m.Key)) col.Metadata[m.Key] = m.Value;

                    _tracker.Record(
                        target,
                        resolvedSources,
                        "CREATE DATASET",
                        targetColumn: targetCol,
                        sourceColumns: sourceCols,
                        metadata: col.Metadata,
                        derivedFromDescriptions: col.DerivedFromDescriptions,
                        line: col.Line,
                        column: col.Column);
                }
            }
        }

        public void RecordCreateVisualLineage(CreateVisualStatement statement)
        {
            var sources = statement.Source.IsInlineSelect && statement.Source.InlineSelect != null
                ? statement.Source.InlineSelect.GetSourceTables()
                : string.IsNullOrWhiteSpace(statement.Source.TempTableName)
                    ? Enumerable.Empty<string>()
                    : new[] { statement.Source.TempTableName };

            var sourceList = sources.ToList();

            _tracker.Record(
                $"report:{statement.Name}",
                sourceList,
                "CREATE VISUAL",
                line: statement.Line,
                column: statement.Column,
                endLine: statement.EndLine,
                endColumn: statement.EndColumn);

            foreach (var mapping in statement.Mappings)
            {
                _tracker.Record(
                    $"report:{statement.Name}",
                    sourceList,
                    "CREATE VISUAL",
                    targetColumn: mapping.Role,
                    sourceColumns: new[] { mapping.Column },
                    line: mapping.Line,
                    column: ((AstNode)mapping).Column);
            }
        }
    }
}
