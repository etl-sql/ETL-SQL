using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;

namespace ETL_SQL.Analysis.Lineage;

public class LineageAnalyzer
{
    public ILineageTracker Tracker { get; }

    // When set, the next analyzed SELECT records its column lineage against
    // this target instead of the generic "RESULTSET" — used so a dataset's
    // inner SELECT is keyed to the dataset name (and thus persists/queryable
    // across scripts) rather than the ambiguous "RESULTSET".
    private string? _selectTargetOverride;
    private List<string>? _selectTargetColumnsOverride;

    // Operation recorded for the next SELECT's column entries. An INSERT ... SELECT is one
    // movement, so its column lineage is labelled INSERT rather than SELECT — otherwise the same
    // write shows up twice in eng.lineage, once per subsystem that observed it.
    private string? _selectOperationOverride;

    public LineageAnalyzer(ILineageTracker tracker)
    {
        Tracker = tracker;
    }

    public void Analyze(Script script)
    {
        RegisterConnectionsFromScript(script.Statements);
        AnalyzeStatements(script.Statements);
    }

    /// <summary>
    /// Builds the tracker's connection resolver from the script's own <c>CREATE CONNECTION</c>
    /// statements. The IDE hover path analyses text statically and never opens a connection, so
    /// without this pre-pass lineage could only ever show script-local aliases ("pats", "hospital"),
    /// which mean nothing once the reader leaves the script. Only credential-free fields are read:
    /// encrypted (<c>ENC:</c>) connection strings are deliberately left unresolved.
    /// </summary>
    private void RegisterConnectionsFromScript(IEnumerable<Statement> statements)
    {
        var descriptors = new Dictionary<string, LineageSourceDescriptor>(StringComparer.OrdinalIgnoreCase);
        CollectConnections(statements, descriptors);

        if (descriptors.Count == 0) return;

        // A resolver already installed by the engine describes live connections and is
        // strictly better informed than anything parsed from text — do not displace it.
        Tracker.ConnectionResolver ??= alias =>
            descriptors.TryGetValue(alias, out var d) ? d : LineageSourceDescriptor.Unknown;
    }

    private void CollectConnections(IEnumerable<Statement> statements, Dictionary<string, LineageSourceDescriptor> into)
    {
        foreach (var stmt in statements)
        {
            switch (stmt)
            {
                case CreateConnectionStatement conn:
                    var descriptor = DescribeConnection(conn);
                    if (!descriptor.IsUnknown) into[conn.ConnectionName] = descriptor;
                    break;

                // NO_SAVE_CONNECTION suppresses the server in every physical identifier.
                case SetNoSaveConnectionStatement noSave:
                    Tracker.NoSaveConnection = noSave.Enabled;
                    break;

                case BlockStatement block:
                    CollectConnections(block.Statements, into);
                    break;
                case IfStatement ifStmt:
                    CollectConnections(new[] { ifStmt.IfBody }, into);
                    if (ifStmt.ElseIfClauses != null)
                        CollectConnections(ifStmt.ElseIfClauses.Select(e => e.Body), into);
                    if (ifStmt.ElseBody != null) CollectConnections(new[] { ifStmt.ElseBody }, into);
                    break;
                case WhileStatement w:
                    CollectConnections(new[] { w.Body }, into);
                    break;
                case ForStatement f:
                    CollectConnections(new[] { f.Body }, into);
                    break;
                case ForeachStatement fe:
                    CollectConnections(new[] { fe.Body }, into);
                    break;
            }
        }
    }

    private static LineageSourceDescriptor DescribeConnection(CreateConnectionStatement conn)
    {
        var type = conn.ConnectionType;
        if (string.IsNullOrEmpty(type)) return LineageSourceDescriptor.Unknown;

        // Option-bag form: FLATFILE(PATH="...") / MSSQL(SERVER='...', DATABASE='...')
        string? path = null, server = null, database = null;
        if (conn.Options != null)
        {
            foreach (var kv in conn.Options)
            {
                var literal = AsLiteralString(kv.Value);
                if (literal == null) continue;
                switch (kv.Key.ToUpperInvariant())
                {
                    case "PATH":
                    case "FILE":
                        path = literal; break;
                    case "SERVER":
                    case "HOST":
                    case "DATASOURCE":
                    case "DATA SOURCE":
                    case "DATA_SOURCE":
                        server = literal; break;
                    case "DATABASE":
                    case "DB":
                    case "INITIAL CATALOG":
                    case "INITIAL_CATALOG":
                        database = literal; break;
                }
            }
        }

        // Bare-string form: MSSQL('Server=localhost;Database=EDW;...')
        var target = AsLiteralString(conn.TargetExpression);
        if (target != null && !target.StartsWith("ENC:", StringComparison.OrdinalIgnoreCase))
        {
            if (LineageTracker.IsFileConnector(type))
            {
                path ??= target;
            }
            else if (target.Contains('='))
            {
                foreach (var pair in target.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    var eq = pair.IndexOf('=');
                    if (eq <= 0) continue;
                    var key = pair[..eq].Trim().ToUpperInvariant();
                    var value = pair[(eq + 1)..].Trim();
                    if (value.Length == 0) continue;
                    switch (key)
                    {
                        case "SERVER":
                        case "HOST":
                        case "DATA SOURCE":
                        case "DATASOURCE":
                        case "ADDR":
                        case "ADDRESS":
                            server ??= value; break;
                        case "DATABASE":
                        case "INITIAL CATALOG":
                            database ??= value; break;
                    }
                }
            }
        }

        return new LineageSourceDescriptor(type, server, database, path);
    }

    private static string? AsLiteralString(Expression? expr) => expr switch
    {
        LiteralExpression lit => lit.Value?.ToString(),
        IdentifierExpression id => id.Name,
        _ => null
    };

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
                string tblName = sel.FromTable.FullyQualifiedName;
                Tracker.Record(tblName, Enumerable.Empty<string>(), "TABLE_TAGS", metadata: sel.FromTable.Metadata, line: sel.FromTable.Line, column: sel.FromTable.Column);
            }
            foreach (var join in sel.Joins)
            {
                if (join.Table.Metadata.Count > 0)
                {
                    string tblName = join.Table.FullyQualifiedName;
                    Tracker.Record(tblName, Enumerable.Empty<string>(), "TABLE_TAGS", metadata: join.Table.Metadata, line: join.Table.Line, column: join.Table.Column);
                }
            }

            string target = _selectTargetOverride
                ?? sel.IntoTable?.FullyQualifiedName
                ?? "RESULTSET";
            var targetCols = _selectTargetColumnsOverride;
            // Match the operation the engine records at runtime, so the analysis-time and
            // execution-time views of one movement describe it the same way.
            var operation = _selectOperationOverride
                ?? (sel.IntoTable != null ? "SELECT INTO" : "SELECT");
            _selectTargetOverride = null;   // applies only to this immediate SELECT
            _selectTargetColumnsOverride = null;
            _selectOperationOverride = null;

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
                    var allSources = sel.GetSourceTables().ToList();
                    if (sourceCols.Count > 0)
                    {
                        var narrowed = allSources
                            .Where(t => sourceCols.Any(c => Tracker.GetColumnMetadata(t, c) != null))
                            .ToList();
                        resolvedSources = narrowed.Count > 0 ? narrowed : allSources;
                    }
                    else
                    {
                        resolvedSources = allSources;
                    }
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

                string alias = (targetCols != null && colIndex < targetCols.Count)
                    ? targetCols[colIndex]
                    : (col.Alias ?? (col.Expression is IdentifierExpression id ? id.Name.Split('.').Last() : $"expr_{colIndex + 1}"));

                // Update AST node for IDE hover persistence
                col.Metadata = inherited;
                col.DerivedFromDescriptions = derived;

                var kind = ClassifyExpression(col.Expression);
                var exprSql = kind != TransformationKind.PassThrough ? col.Expression.ToSql() : null;
                var fns = CollectFunctions(col.Expression);

                Tracker.Record(target, resolvedSources, operation, targetColumn: alias, sourceColumns: sourceCols, metadata: inherited, derivedFromDescriptions: derived, line: col.Line, column: col.Column, endLine: col.EndLine, endColumn: col.EndColumn, transformationKind: kind, transformationExpression: exprSql, functionsApplied: fns.Count > 0 ? fns : null);
            }
        }
        else if (stmt is InsertStatement ins)
        {
            string target = ins.TargetTable.FullyQualifiedName;
            var sources = ins.GetSourceTables();
            Tracker.Record(target, sources, "INSERT", line: ins.Line, column: ins.Column, endLine: ins.EndLine, endColumn: ins.EndColumn);
            if (ins.SelectQuery != null)
            {
                _selectTargetOverride = target;
                _selectTargetColumnsOverride = ins.Columns;
                _selectOperationOverride = "INSERT";
                AnalyzeStatement(ins.SelectQuery);
                _selectTargetOverride = null;
                _selectTargetColumnsOverride = null;
                _selectOperationOverride = null;
            }
        }
        else if (stmt is BulkInsertStatement bulk)
        {
            string t = bulk.TargetTable.FullyQualifiedName;
            Tracker.Record(t, new[] { bulk.FilePath }, "BULK INSERT", metadata: bulk.Metadata, line: bulk.Line, column: bulk.Column, endLine: bulk.EndLine, endColumn: bulk.EndColumn);
        }
        else if (stmt is UpdateStatement upd)
        {
            string t = upd.TargetTable.FullyQualifiedName;
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
            string t = merge.TargetTable.FullyQualifiedName;
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
            string target = pushdown.IntoTable?.FullyQualifiedName ?? "RESULTSET";

            Tracker.Record(target, sources, "EXECUTE PUSHDOWN", line: pushdown.Line, column: pushdown.Column, endLine: pushdown.EndLine, endColumn: pushdown.EndColumn);
        }
        else if (stmt is CreateDatasetStatement dataset)
        {
            string target = $"dataset:{dataset.TempTableName}";
            var sources = dataset.SourceQuery.GetSourceTables().ToList();
            Tracker.Record(target, sources, "CREATE DATASET", line: dataset.Line, column: dataset.Column, endLine: dataset.EndLine, endColumn: dataset.EndColumn);
            // Key the inner SELECT's column lineage to the dataset target so it
            // survives persistence and can be resolved by name from another script.
            _selectTargetOverride = target;
            AnalyzeStatement(dataset.SourceQuery);
            _selectTargetOverride = null;
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

            if (visual.AdvancedChart is { } chart)
            {
                foreach (var encoding in chart.Encodings)
                    RecordChartBinding(encoding, "CHART");
                foreach (var layer in chart.Layers)
                {
                    foreach (var encoding in layer.Encodings)
                        RecordChartBinding(encoding, layer.Name);
                    foreach (var condition in layer.Conditions)
                        Tracker.Record(target, sources, "CREATE VISUAL CHART CONDITION",
                            targetColumn: $"{layer.Name}.{condition.Channel.ToString().ToUpperInvariant()}",
                            sourceColumns: condition.Predicate.GetSourceColumns(), line: condition.Line, column: condition.Column);
                }
                if (chart.Facet?.RowField is { } rowField)
                    Tracker.Record(target, sources, "CREATE VISUAL CHART FACET", targetColumn: "FACET.ROW",
                        sourceColumns: new[] { rowField }, line: chart.Facet.Line, column: chart.Facet.Column);
                if (chart.Facet?.ColumnField is { } columnField)
                    Tracker.Record(target, sources, "CREATE VISUAL CHART FACET", targetColumn: "FACET.COLUMN",
                        sourceColumns: new[] { columnField }, line: chart.Facet.Line, column: chart.Facet.Column);
                if (chart.Facet?.WrapField is { } wrapField)
                    Tracker.Record(target, sources, "CREATE VISUAL CHART FACET", targetColumn: "FACET.WRAP",
                        sourceColumns: new[] { wrapField }, line: chart.Facet.Line, column: chart.Facet.Column);

                void RecordChartBinding(AdvancedChartEncoding encoding, string scope)
                {
                    var targetColumn = $"{scope}.{encoding.Channel.ToString().ToUpperInvariant()}";
                    if (encoding.Source.Kind == AdvancedChartBindingSourceKind.Field)
                    {
                        Tracker.Record(target, sources, "CREATE VISUAL CHART", targetColumn,
                            sourceColumns: new[] { encoding.Source.Field! }, line: encoding.Line, column: encoding.Column);
                        return;
                    }
                    if (encoding.Source.Constant is VariableExpression parameter)
                        Tracker.Record(target, sources, "CREATE VISUAL CHART PARAMETER", targetColumn,
                            metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["parameter-dependency"] = parameter.Name
                            }, line: encoding.Line, column: encoding.Column);
                }
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

    /// <summary>
    /// Heuristic: a <c>+</c> chain containing a string literal anywhere is concatenation.
    /// <c>+</c> is overloaded and the classifier has no types, so the literal is the only evidence
    /// available. It has to be looked for through the whole chain rather than at the immediate
    /// operands: <c>first + ' ' + last</c> parses left-associatively as
    /// <c>(first + ' ') + last</c>, whose top-level operands are a binary expression and an
    /// identifier — no literal in sight — so checking only the immediate children classified every
    /// multi-part name concatenation as arithmetic.
    /// </summary>
    private static bool IsStringConcat(BinaryExpression be) =>
        ContainsStringLiteral(be.Left) || ContainsStringLiteral(be.Right);

    private static bool ContainsStringLiteral(Expression expr) => expr switch
    {
        LiteralExpression { Type: TokenType.STRING_LITERAL } => true,
        BinaryExpression inner when inner.Operator == TokenType.PLUS || inner.Operator == TokenType.CONCAT =>
            ContainsStringLiteral(inner.Left) || ContainsStringLiteral(inner.Right),
        _ => false
    };

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
