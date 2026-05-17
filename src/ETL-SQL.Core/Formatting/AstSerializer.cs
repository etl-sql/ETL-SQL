using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core.Parser;

namespace ETL_SQL.Core.Formatting
{
    /// <summary>
    /// Central AST-to-SQL serializer. Each AST node's <c>ToSql()</c> delegates here
    /// instead of containing logic inline. This keeps <c>Ast.cs</c> as pure data models.
    /// </summary>
    public static class AstSerializer
    {
        // ── Public dispatch for AstNode-derived types ─────────────────────

        public static string Format(AstNode node) => node switch
        {
            // ── Trivial statements ──
            NoOpStatement          _ => ";",
            BreakStatement         _ => "BREAK;",
            ContinueStatement      _ => "CONTINUE;",
            ClearSessionStatement  _ => "CLEAR SESSION;",
            TryCatchStatement      _ => "TRY ... CATCH ... END",
            BlockStatement         _ => "BEGIN ... END",

            // ── DML ──
            SelectStatement            s => FormatSelect(s),
            SetOperationStatement      s => FormatSetOperation(s),
            InsertStatement            s => FormatInsert(s),
            UpdateStatement            s => FormatUpdate(s),
            DeleteStatement            s => FormatDelete(s),
            MergeStatement             s => FormatMerge(s),

            // ── Execution ──
            ExecStatement                  s => FormatExec(s),
            ExecuteRemoteBlockStatement    s => $"EXECUTE ({s.ConnectionName.ToSql()}) BEGIN ... END",
            ExecutePushdownStatement       s => FormatExecutePushdown(s),
            ExecuteStatement               s => FormatExecuteProcedure(s),

            // ── DDL ──
            CreateConnectionStatement      s => FormatCreateConnection(s),
            AlterConnectionStatement       s => FormatAlterConnection(s),
            CreateSshKeyPairStatement      s => FormatCreateSshKeyPair(s),
            CreatePgpKeyPairStatement      s => FormatCreatePgpKeyPair(s),
            CreateTableStatement           s => FormatCreateTable(s),
            AlterTableStatement            s => FormatAlterTable(s),
            DropTableStatement             s => $"DROP TABLE {(s.IfExists ? "IF EXISTS " : "")}{s.TargetTable.ToSql()};",
            DropConnectionStatement        s => $"DROP CONNECTION {(s.IfExists ? "IF EXISTS " : "")}{s.ConnectionName};",
            DropProcedureStatement         s => $"DROP PROCEDURE {(s.IfExists ? "IF EXISTS " : "")}{s.ProcedureName};",
            DropFunctionStatement          s => $"DROP FUNCTION {(s.IfExists ? "IF EXISTS " : "")}{s.FunctionName};",
            DropIndexStatement             s => $"DROP INDEX {(s.IfExists ? "IF EXISTS " : "")}{s.IndexName}{(s.Table != null ? " ON " + s.Table.ToSql() : "")};",
            TruncateTableStatement         s => $"TRUNCATE TABLE {s.TargetTable.ToSql()};",
            CreateIndexStatement           s => FormatCreateIndex(s),
            CreateProcedureStatement       s => FormatCreateProcedure(s),
            CreateFunctionStatement        s => FormatCreateFunction(s),

            // ── Variables & flow ──
            DeclareStatement               s => FormatDeclare(s),
            SetVariableStatement           s => $"SET {s.Target.ToSql()} = {s.Value.ToSql()};",
            WhileStatement                 s => $"WHILE {s.Condition.ToSql()} BEGIN ... END",
            ForStatement                   s => $"FOR {s.VariableName} = {s.StartValue.ToSql()} TO {s.EndValue.ToSql()} BEGIN ... END",
            ForeachStatement               s => $"FOREACH {s.VariableName} IN {s.ListExpression.ToSql()} BEGIN ... END",
            IfStatement                    s => FormatIf(s),
            ParallelStatement              s => "PARALLEL " + (s.ConcurrencyLimit > 0 ? $"({s.ConcurrencyLimit}) " : "") + s.Body.ToSql(),

            // ── Transactions ──
            BeginTransactionStatement      s => s.Name != null ? $"BEGIN TRANSACTION {s.Name};" : "BEGIN TRANSACTION;",
            CommitTransactionStatement     s => s.Name != null ? $"COMMIT TRANSACTION {s.Name};" : "COMMIT TRANSACTION;",
            RollbackTransactionStatement   s => s.Name != null ? $"ROLLBACK TRANSACTION {s.Name};" : "ROLLBACK TRANSACTION;",

            // ── Error handling ──
            ThrowStatement                 s => s.Message != null ? $"THROW {s.Message.ToSql()};" : "THROW;",
            ReturnStatement                s => s.ReturnValue != null ? $"RETURN {s.ReturnValue.ToSql()};" : "RETURN;",
            RaiseErrorStatement            s => FormatRaiseError(s),

            // ── I/O & messaging ──
            PrintStatement                 s => FormatPrint(s),
            BulkInsertStatement            s => FormatBulkInsert(s),
            ExportStatement                s => FormatExport(s),
            EmailStatement                 s => FormatEmail(s),
            FileOperationStatement         s => FormatFileOperation(s),
            DirectoryOperationStatement    s => FormatDirectoryOperation(s),
            FileTransferStatement          s => FormatFileTransfer(s),

            // ── Docker ──
            DockerStatement                s => s.Alias != null ? $"USE DOCKER({s.ImageName.ToSql()}) AS {s.Alias};" : $"USE DOCKER({s.ImageName.ToSql()});",
            DockerActionStatement          s => FormatDockerAction(s),

            // ── Jobs & scheduling ──
            CreateJobStatement             s => $"CREATE JOB {s.JobName} ON SCHEDULE {s.Schedule.ToSql()} AS {s.Script.ToSql()}",
            ShowJobHistoryStatement        s => (s.JobName != null ? $"SHOW JOB HISTORY {s.JobName}" : "SHOW JOB HISTORY") + (s.IntoTable != null ? $" INTO {s.IntoTable};" : ";"),
            ShowJobsStatement              s => "SHOW JOBS" + (s.IntoTable != null ? $" INTO {s.IntoTable};" : ";"),

            // ── SHOW ──
            ShowConnectionsStatement       s => "SHOW CONNECTIONS" + (s.IntoTable != null ? $" INTO {s.IntoTable};" : ";"),
            ShowConnectionConfigStatement  s => $"SHOW CONNECTION {s.ConnectionName} CONFIG" + (s.IntoTable != null ? $" INTO {s.IntoTable};" : ";"),
            ShowTablesStatement            s => (s.ConnectionName != null ? $"SHOW TABLES ON {s.ConnectionName}" : "SHOW TABLES") + (s.IntoTable != null ? $" INTO {s.IntoTable};" : ";"),
            ShowColumnsStatement           s => $"SHOW COLUMNS FOR {s.Table.ToSql()}" + (s.IntoTable != null ? $" INTO {s.IntoTable};" : ";"),
            ShowVariablesStatement         s => (s.IsLocalOnly ? "SHOW LOCAL VARIABLES" : "SHOW VARIABLES") + (s.IntoTable != null ? $" INTO {s.IntoTable}" : "") + ";",
            ShowTagsStatement              s => $"SHOW TAGS FOR TABLE {s.TableName}" + (s.ColumnName != null ? $" COLUMN {s.ColumnName}" : "") + (s.IntoTable != null ? $" INTO {s.IntoTable};" : ";"),
            ShowTagValueStatement          s => $"SHOW TAG VALUE FOR TABLE {s.TableName}" + (s.ColumnName != null ? $" COLUMN {s.ColumnName}" : "") + $" WITH TAG {s.TagName}" + (s.IntoTable != null ? $" INTO {s.IntoTable};" : ";"),

            // ── Misc statements ──
            RunScriptStatement             s => FormatRunScript(s),
            WaitForStatement               s => $"WAITFOR {s.Type.ToString().ToUpper()} {s.Expression.ToSql()};",
            ExplainStatement               s => (s.IsAnalyze ? "EXPLAIN ANALYZE " : "EXPLAIN ") + s.Query.ToSql() + (s.IntoTable != null ? " INTO " + s.IntoTable.ToSql() : ""),
            CreateVisualStatement          s => FormatCreateVisual(s),
            LineageStatement               s => FormatLineage(s),
            LintStatement                  s => s.ScriptPath != null ? $"LINT '{s.ScriptPath}';" : "LINT;",
            HelpStatement                  s => $"HELP {(s.Topic != null ? s.Topic + (s.SubTopic != null ? " " + s.SubTopic : "") : "")}",
            DropReportObjectStatement      s => $"DROP {s.ObjectType.ToString().ToUpper()} {(s.IfExists ? "IF EXISTS " : "")}{s.Name};",
            AlterReportObjectStatement     s => $"ALTER {s.ObjectType.ToString().ToUpper()} {s.Name} ...", // Summarized
            CreateTemplateStatement        s => FormatCreateTemplate(s),
            CreateThemeStatement           s => FormatCreateTheme(s),
            SetTemplatePathStatement       s => s.ToSql(),

            // ── SETS ──
            CreateSetsStatement            s => FormatCreateSets(s),
            DropSetsStatement              s => s.IfExists ? $"DROP SETS IF EXISTS !{s.Name};" : $"DROP SETS !{s.Name};",
            UseSetsStatement               s => $"USE SETS !{s.Name};",

            // ── Security ──
            UsePasswordStatement           s => $"USE PASSWORD = '********';",
            SetShowPasswordStatement       s => $"SET SHOW_PASSWORD {(s.Enabled ? "ON" : "OFF")};",

            // ── Profiling / what-if ──
            SetProfilingStatement          s => $"SET PROFILING {(s.Enabled ? "ON" : "OFF")}",
            SetWhatIfStatement             s => $"SET WHAT_IF {(s.Enabled ? "ON" : "OFF")}",
            SetThresholdStatement          s => FormatSetThreshold(s),
            ShowProfileStatement           s => "SHOW PROFILE" + (s.IntoTable != null ? $" INTO {s.IntoTable}" : ""),
            ShowVersionStatement           s => "SHOW VERSION" + (s.IntoTable != null ? $" INTO {s.IntoTable};" : ";"),

            // ── Expressions — more-derived before less-derived ──
            SubstringExpression    e => $"SUBSTRING({e.String.ToSql()} FROM {e.Start.ToSql()}{(e.Length != null ? $" FOR {e.Length.ToSql()}" : "")})",
            PositionExpression     e => $"POSITION({e.Substring.ToSql()} IN {e.String.ToSql()})",
            ExtractExpression      e => $"EXTRACT({e.Field} FROM {e.Source.ToSql()})",
            OverlayExpression      e => $"OVERLAY({e.String.ToSql()} PLACING {e.Overlay.ToSql()} FROM {e.Start.ToSql()}{(e.Length != null ? $" FOR {e.Length.ToSql()}" : "")})",
            TrimExpression         e => FormatTrim(e),
            FunctionCallExpression e => FormatFunctionCall(e),
            UnaryExpression        e => FormatUnary(e),
            BinaryExpression       e => FormatBinary(e),
            LiteralExpression      e => FormatLiteral(e),
            IdentifierExpression   e => e.Name,
            MemberAccessExpression e => $"{e.Expression.ToSql()}.{e.MemberName}",
            SubqueryExpression     e => $"({e.Query.ToSql().TrimEnd(';')})",
            VariableExpression     e => e.Name,
            ListExpression         e => "(" + string.Join(", ", e.Items.Select(i => i.ToSql())) + ")",
            IsNullExpression       e => $"{e.Expression.ToSql()} IS {(e.Not ? "NOT " : "")}NULL",
            InExpression           e => $"{e.Left.ToSql()} {(e.IsNot ? "NOT " : "")}IN {e.Right.ToSql()}",
            BetweenExpression      e => FormatBetween(e),
            LikeExpression         e => FormatLike(e),
            ExistsExpression       e => $"{(e.IsNot ? "NOT " : "")}EXISTS ({e.Subquery.ToSql()})",
            CaseExpression         e => FormatCase(e),
            AtTimeZoneExpression   e => $"{e.Left.ToSql()} AT TIME ZONE {e.TimeZone.ToSql()}",

            // ── AstNode helpers ──
            SelectColumn               n => n.Alias != null ? $"{n.Expression.ToSql()} AS {n.Alias}" : n.Expression.ToSql(),
            TableReference             n => FormatTableReference(n),
            PivotClause                n => $"PIVOT ({n.AggregateFunction}({n.AggregateColumn}) FOR {n.PivotColumn} IN ({string.Join(", ", n.PivotValues.Select(v => v.ToSql()))}))" + (n.Alias != null ? $" AS {n.Alias}" : ""),
            UnpivotClause              n => $"UNPIVOT ({n.ValueColumn} FOR {n.NameColumn} IN ({string.Join(", ", n.UnpivotColumns)}))" + (n.Alias != null ? $" AS {n.Alias}" : ""),
            OutputClause               n => $"OUTPUT {string.Join(", ", n.Columns.Select(c => c.ToSql()))}{(n.IntoTable != null ? $" INTO {n.IntoTable.ToSql()}" : "")}",
            ForClause                  n => FormatForClause(n),
            ForeignKeyReference        n => $"REFERENCES {n.Table.ToSql()}({string.Join(", ", n.Columns)})",
            TablePrimaryKeyConstraint  n => $"{(n.ConstraintName != null ? $"CONSTRAINT {n.ConstraintName} " : "")}PRIMARY KEY ({string.Join(", ", n.Columns)})",
            TableUniqueConstraint      n => $"{(n.ConstraintName != null ? $"CONSTRAINT {n.ConstraintName} " : "")}UNIQUE ({string.Join(", ", n.Columns)})",
            TableForeignKeyConstraint  n => $"{(n.ConstraintName != null ? $"CONSTRAINT {n.ConstraintName} " : "")}FOREIGN KEY ({string.Join(", ", n.Columns)}) {n.Reference.ToSql()}",
            TableCheckConstraint       n => $"{(n.ConstraintName != null ? $"CONSTRAINT {n.ConstraintName} " : "")}CHECK ({n.Expression.ToSql()})",
            ColumnDefinition           n => FormatColumnDefinition(n),
            OrderByClause              n => n.Expression.ToSql() + (n.Descending ? " DESC" : " ASC"),
            JoinClause                 n => FormatJoin(n),
            GroupingSetClause          n => FormatGroupingSet(n),
            Assignment                 n => $"{n.ColumnName} = {n.Value.ToSql()}",
            ParameterDefinition        n => $"{n.Name} {n.DataType}",
            ElseIfClause               n => $"ELSE IF {n.Condition.ToSql()} BEGIN ... END",
            WindowFrame                n => FormatWindowFrame(n),
            WindowClause               n => FormatWindowClause(n),
            ScheduleInfo               n => $"EVERY {n.Interval} {n.Unit}{(n.AtTime != null ? $" AT '{n.AtTime}'" : "")}",
            MergeUpdateClause          n => $"THEN UPDATE SET {string.Join(", ", n.Assignments.Select(a => a.ToSql()))}",
            MergeDeleteClause          _ => "THEN DELETE",
            MergeInsertClause          n => FormatMergeInsert(n),
            MergeActionClause          n => FormatMergeAction(n),
            DrillDownAction            n => $"DRILL_DOWN(Target = {n.TargetVisual}, Key = ({string.Join(", ", n.KeyColumns)}))",
            DrillInAction              n => $"DRILL_IN(HIERARCHY = ({string.Join(", ", n.Hierarchy)}))",
            RunScriptAction            n => $"RUN_SCRIPT('{n.ScriptPath}'{FormatActionParameters(n.Parameters)})",
            ClearFiltersAction         _ => "CLEAR_FILTERS",
            ApplyParametersAction      _ => "APPLY_PARAMETERS",
            ReportCommandAction        n => n.Command == "REFRESH" ? "REFRESH_REPORT" : n.Command,
            DrillReportAction          n => $"DRILL_REPORT('{n.TargetReport}'{FormatActionParameters(n.Parameters)})",
            NavigatePageAction         n => $"NAVIGATE_PAGE({n.TargetPage})",
            RefreshVisualsAction       n => $"REFRESH_VISUALS({string.Join(", ", n.Targets)})",
            SetUiStateAction           n => $"SET_UI_STATE({FormatActionTargets(n.Targets)}, {n.Key}, {n.Value})",

            _ => node is Statement ? "UNKNOWN STATEMENT" : node.GetType().Name
        };

        // ── Public overloads for non-AstNode types ──────────────────────────

        public static string Format(JoinClause join)       => FormatJoin(join);
        public static string Format(OrderByClause o)       => o.Expression.ToSql() + (o.Descending ? " DESC" : " ASC");
        public static string Format(GroupingSetClause g)   => FormatGroupingSet(g);
        public static string Format(ColumnDefinition col)  => FormatColumnDefinition(col);
        public static string Format(ExecuteParameter p)    => p.Expression.ToSql() + (p.IsOutput ? " OUTPUT" : "") + (p.IsInput ? " INPUT" : "");

        // ── Statement formatters ─────────────────────────────────────────────

        private static string FormatActionParameters(Dictionary<string, string> parameters)
        {
            if (parameters.Count == 0) return "";
            return ", " + string.Join(", ", parameters.Select(p => $"{p.Key} = {p.Value}"));
        }

        private static string FormatActionTargets(IReadOnlyCollection<string> targets)
        {
            return targets.Count == 1
                ? targets.First()
                : "(" + string.Join(", ", targets) + ")";
        }

        private static string FormatSelect(SelectStatement s)
        {
            var recursive = s.IsRecursive ? "RECURSIVE " : "";
            var with = (s.Ctes != null && s.Ctes.Count > 0)
                ? $"WITH {recursive}" + string.Join(", ", s.Ctes.Select(c => $"{c.Name} AS ({c.Query.ToSql().TrimEnd(';')})")) + " "
                : "";
            var distinct = s.IsDistinct ? "DISTINCT " : "";
            var top = "";
            if (s.TopCount != null)
            {
                var percent  = s.IsTopPercent ? " PERCENT" : "";
                var ties     = s.WithTies     ? " WITH TIES" : "";
                top = $"TOP ({s.TopCount.ToSql()}){percent}{ties} ";
            }
            var cols     = string.Join(", ", s.Columns.Select(c => c.ToSql()));
            var into     = s.IntoTable    != null ? $" INTO {s.IntoTable.ToSql()}"   : "";
            var from     = $" FROM {s.FromTable.ToSql()}";
            var joins    = s.Joins.Count  > 0    ? " " + string.Join(" ", s.Joins.Select(j => j.ToSql())) : "";
            var where    = s.WhereClause  != null ? $" WHERE {s.WhereClause.ToSql()}"  : "";
            var group    = s.GroupingSet  != null ? $" GROUP BY {s.GroupingSet.ToSql()}"
                         : s.GroupBy      != null && s.GroupBy.Count > 0
                               ? $" GROUP BY {string.Join(", ", s.GroupBy.Select(g => g.ToSql()))}"
                               : "";
            var having   = s.HavingClause != null ? $" HAVING {s.HavingClause.ToSql()}"  : "";
            var order    = s.OrderBy      != null && s.OrderBy.Count > 0
                               ? $" ORDER BY {string.Join(", ", s.OrderBy.Select(o => o.ToSql()))}" : "";
            var limit    = s.LimitCount   != null ? $" LIMIT {s.LimitCount.ToSql()}"     : "";
            var offset   = s.Offset       != null ? $" OFFSET {s.Offset.ToSql()} ROWS"   : "";
            var forCl    = s.ForClause    != null ? $" {s.ForClause.ToSql()}"             : "";
            return $"{with}SELECT {distinct}{top}{cols}{into}{from}{joins}{where}{group}{having}{order}{limit}{offset}{forCl};";
        }

        private static string FormatSetOperation(SetOperationStatement s)
        {
            string op = s.Operation switch {
                SetOpType.UNION     => "UNION",
                SetOpType.UNION_ALL => "UNION ALL",
                SetOpType.EXCEPT    => "EXCEPT",
                SetOpType.INTERSECT => "INTERSECT",
                _                   => "UNION"
            };
            return $"({s.Left.ToSql().TrimEnd(';')}) {op} ({s.Right.ToSql().TrimEnd(';')});";
        }

        private static string FormatInsert(InsertStatement s)
        {
            var with   = (s.Ctes != null && s.Ctes.Count > 0) ? "WITH " + string.Join(", ", s.Ctes.Select(c => $"{c.Name} AS ({c.Query.ToSql().TrimEnd(';')})")) + " " : "";
            var cols   = s.Columns  != null && s.Columns.Count > 0 ? "(" + string.Join(", ", s.Columns) + ") " : "";
            var output = s.Output   != null ? " " + s.Output.ToSql() : "";
            if (s.SelectQuery != null)
                return $"{with}INSERT INTO {s.TargetTable.ToSql()} {cols}{output}{s.SelectQuery.ToSql()}";
            var vals = s.Values != null ? string.Join(", ", s.Values.Select(row => "(" + string.Join(", ", row.Select(v => v.ToSql())) + ")")) : "";
            return $"{with}INSERT INTO {s.TargetTable.ToSql()} {cols}{output}VALUES {vals};";
        }

        private static string FormatUpdate(UpdateStatement s)
        {
            var with  = (s.Ctes != null && s.Ctes.Count > 0) ? "WITH " + string.Join(", ", s.Ctes.Select(c => $"{c.Name} AS ({c.Query.ToSql().TrimEnd(';')})")) + " " : "";
            var sets  = string.Join(", ", s.Assignments.Select(a => a.ToSql()));
            var from  = s.FromTable != null ? $" FROM {s.FromTable.ToSql()}" : "";
            var joins = s.Joins     != null && s.Joins.Count > 0 ? " " + string.Join(" ", s.Joins.Select(j => j.ToSql())) : "";
            var out_  = s.Output    != null ? " " + s.Output.ToSql() : "";
            var where = s.WhereClause != null ? $" WHERE {s.WhereClause.ToSql()}" : "";
            return $"{with}UPDATE {s.TargetTable.ToSql()} SET {sets}{from}{joins}{out_}{where};";
        }

        private static string FormatDelete(DeleteStatement s)
        {
            var with  = (s.Ctes != null && s.Ctes.Count > 0) ? "WITH " + string.Join(", ", s.Ctes.Select(c => $"{c.Name} AS ({c.Query.ToSql().TrimEnd(';')})")) + " " : "";
            var out_  = s.Output      != null ? " " + s.Output.ToSql() : "";
            var where = s.WhereClause != null ? $" WHERE {s.WhereClause.ToSql()}" : "";
            return $"{with}DELETE FROM {s.TargetTable.ToSql()}{out_}{where};";
        }

        private static string FormatMerge(MergeStatement s)
        {
            var with = (s.Ctes != null && s.Ctes.Count > 0) ? "WITH " + string.Join(", ", s.Ctes.Select(c => $"{c.Name} AS ({c.Query.ToSql().TrimEnd(';')})")) + " " : "";
            var sb   = new System.Text.StringBuilder();
            sb.Append(with);
            sb.AppendLine($"MERGE INTO {s.TargetTable.ToSql()}{(s.TargetAlias != null ? " AS " + s.TargetAlias : "")}");
            sb.AppendLine($"USING {s.SourceTable.ToSql()}{(s.SourceAlias != null ? " AS " + s.SourceAlias : "")}");
            sb.AppendLine($"ON {s.OnCondition.ToSql()}");
            foreach (var c in s.MatchedClauses)    sb.AppendLine($"WHEN MATCHED{(c.Condition != null ? " AND " + c.Condition.ToSql() : "")} {c.ToSql()}");
            foreach (var c in s.NotMatchedClauses) sb.AppendLine($"WHEN NOT MATCHED{(c.Condition != null ? " AND " + c.Condition.ToSql() : "")} {c.ToSql()}");
            sb.Append(";");
            return sb.ToString();
        }

        private static string FormatExec(ExecStatement s)
        {
            var sql        = $"EXEC ({s.SqlExpression.ToSql()})";
            if (s.ConnectionName != null) sql += $" AT {s.ConnectionName.ToSql()}";
            if (s.IntoTable      != null) sql += $" INTO {s.IntoTable.ToSql()}";
            if (s.Parameters.Count > 0)  sql += " WITH (" + string.Join(", ", s.Parameters.Select(p => p.ToSql())) + ")";
            return sql + ";";
        }

        private static string FormatExecutePushdown(ExecutePushdownStatement s)
        {
            var into       = s.IntoTable   != null ? $" INTO {s.IntoTable.ToSql()}"   : "";
            var parameters = s.Parameters.Count > 0 ? " WITH (" + string.Join(", ", s.Parameters.Select(p => p.ToSql())) + ")" : "";
            return $"EXECUTE {s.ConnectionName.ToSql()}{into}{parameters} BEGIN\n{s.SqlText}\nEND;";
        }

        private static string FormatExecuteProcedure(ExecuteStatement s)
        {
            var paramsStr = s.Parameters.Count > 0 ? " " + string.Join(", ", s.Parameters.Select(p => p.ToSql())) : "";
            return $"EXECUTE {s.ProcedureName}{paramsStr};";
        }

        private static string FormatCreateConnection(CreateConnectionStatement s)
        {
            var modeStr = s.Mode switch {
                ObjectCreationMode.Alter         => "ALTER",
                ObjectCreationMode.CreateOrAlter => "CREATE OR ALTER",
                _                                => "CREATE"
            };
            var onStr      = (s.ConnectionType != null && s.TargetExpression != null) ? $" ON {s.ConnectionType}({s.TargetExpression.ToSql()})" : "";
            var optionsStr = s.Options != null && s.Options.Count > 0
                ? " WITH (" + string.Join(", ", s.Options.Select(o => $"{o.Key}={o.Value.ToSql()}")) + ")"
                : "";
            return $"{modeStr} CONNECTION {s.ConnectionName}{onStr}{optionsStr};";
        }

        private static string FormatAlterConnection(AlterConnectionStatement s)
        {
            var onStr      = (s.ConnectionType != null && s.TargetExpression != null) ? $" ON {s.ConnectionType}({s.TargetExpression.ToSql()})" : "";
            var optionsStr = s.Options != null && s.Options.Count > 0
                ? " WITH (" + string.Join(", ", s.Options.Select(o => $"{o.Key}={o.Value.ToSql()}")) + ")"
                : "";
            return $"ALTER CONNECTION {s.ConnectionName}{onStr}{optionsStr};";
        }

        private static string FormatCreateSshKeyPair(CreateSshKeyPairStatement s)
        {
            var args = new List<string> { s.Path.ToSql() };
            if (s.Bits       != null) args.Add($"BITS={s.Bits.ToSql()}");
            if (s.Algorithm  != null) args.Add($"ALGORITHM={s.Algorithm.ToSql()}");
            if (s.Passphrase != null) args.Add($"PASSPHRASE={s.Passphrase.ToSql()}");
            if (s.Comment    != null) args.Add($"COMMENT={s.Comment.ToSql()}");
            return $"CREATE SSH_KEY_PAIR({string.Join(", ", args)});";
        }

        private static string FormatCreatePgpKeyPair(CreatePgpKeyPairStatement s)
        {
            var args = new List<string> { s.Path.ToSql() };
            if (s.Bits       != null) args.Add($"BITS={s.Bits.ToSql()}");
            if (s.Identity   != null) args.Add($"IDENTITY={s.Identity.ToSql()}");
            if (s.Passphrase != null) args.Add($"PASSPHRASE={s.Passphrase.ToSql()}");
            return $"CREATE PGP_KEY_PAIR({string.Join(", ", args)});";
        }

        private static string FormatCreateTable(CreateTableStatement s)
        {
            var ifNot = s.IfNotExists ? "IF NOT EXISTS " : "";
            var items = new List<string>(s.Columns.Select(c => c.ToSql()));
            items.AddRange(s.TableConstraints.Select(tc => tc.ToSql()));
            return $"{ifNot}CREATE TABLE {s.TargetTable.ToSql()} ({string.Join(", ", items)});";
        }

        private static string FormatAlterTable(AlterTableStatement s)
        {
            var sql = $"ALTER TABLE {s.TargetTable.ToSql()} ";
            switch (s.Action)
            {
                case AlterTableActionType.ADD:
                    sql += $"ADD {s.NewColumn!.ToSql()}"; break;
                case AlterTableActionType.DROP_COLUMN:
                    sql += $"DROP COLUMN {s.ColumnToDelete}"; break;
                case AlterTableActionType.RENAME_COLUMN:
                    sql += $"RENAME COLUMN {s.OldColumnName} TO {s.NewColumnName}"; break;
            }
            return sql + ";";
        }

        private static string FormatCreateIndex(CreateIndexStatement s)
        {
            var unique = s.IsUnique ? "UNIQUE " : "";
            return $"CREATE {unique}INDEX {s.IndexName} ON {s.TargetTable.ToSql()} ({string.Join(", ", s.Columns)});";
        }

        private static string FormatCreateProcedure(CreateProcedureStatement s)
        {
            var modeStr = s.Mode switch {
                ObjectCreationMode.Alter         => "ALTER",
                ObjectCreationMode.CreateOrAlter => "CREATE OR ALTER",
                _                                => "CREATE"
            };
            var paramsStr = string.Join(", ", s.Parameters.Select(p => p.ToSql()));
            return $"{modeStr} PROCEDURE {s.ProcedureName} ({paramsStr}) AS BEGIN ... END;";
        }

        private static string FormatCreateFunction(CreateFunctionStatement s)
        {
            var modeStr = s.Mode switch {
                ObjectCreationMode.Alter         => "ALTER",
                ObjectCreationMode.CreateOrAlter => "CREATE OR ALTER",
                _                                => "CREATE"
            };
            var paramsStr = string.Join(", ", s.Parameters.Select(p => p.ToSql()));
            return $"{modeStr} FUNCTION {s.FunctionName} ({paramsStr}) RETURNS {s.ReturnType} AS BEGIN ... END;";
        }

        private static string FormatDeclare(DeclareStatement s)
        {
            var init   = s.InitialValue != null ? $" = {s.InitialValue.ToSql()}" : "";
            var pass   = s.IsSensitive  ? " PASSWORD" : "";
            var input  = s.IsInput      ? " INPUT"    : "";
            var output = s.IsOutput     ? " OUTPUT"   : "";
            return $"DECLARE {s.VariableName} {s.DataType}{init}{pass}{input}{output};";
        }

        private static string FormatIf(IfStatement s)
        {
            var sql = $"IF {s.Condition.ToSql()} BEGIN ... END";
            if (s.ElseIfClauses != null)
                foreach (var ei in s.ElseIfClauses) sql += " " + ei.ToSql();
            if (s.ElseBody != null) sql += " ELSE BEGIN ... END";
            return sql;
        }

        private static string FormatPrint(PrintStatement s)
        {
            var msg = string.Join(", ", s.Arguments.Select(e => e.ToSql()));
            var ts  = s.ShowTimestamp   != null ? $", {s.ShowTimestamp.ToSql()}"   : "";
            var fmt = s.TimestampFormat != null ? $", {s.TimestampFormat.ToSql()}" : "";
            return $"PRINT({msg}{ts}{fmt});";
        }

        private static string FormatRaiseError(RaiseErrorStatement s)
        {
            var loc       = s.CodeLocation != null ? $", {s.CodeLocation.ToSql()}" : "";
            var paramsStr = s.Parameters.Count > 0 ? ", " + string.Join(", ", s.Parameters.Select(p => p.ToSql())) : "";
            return $"RAISEERROR({s.Message.ToSql()}, {s.Severity.ToSql()}{loc}{paramsStr});";
        }

        private static string FormatBulkInsert(BulkInsertStatement s)
        {
            var cols       = s.Columns != null && s.Columns.Count > 0 ? "(" + string.Join(", ", s.Columns) + ") " : "";
            var optionsStr = s.Options.Count > 0
                ? $" WITH ({string.Join(", ", s.Options.Select(o => $"{o.Key} = {o.Value.ToSql()}"))})"
                : "";
            return $"BULK INSERT {s.TargetTable.ToSql()} {cols}FROM '{s.FilePath}'{optionsStr};";
        }

        private static string FormatExport(ExportStatement s)
            => $"EXPORT {s.Source.ToSql()} TO '{s.TargetPath}'" + (s.Options != null ? " WITH (...)" : "");

        private static string FormatEmail(EmailStatement s)
        {
            if (s.IsSqlStyle)
            {
                var sql = $"SEND EMAIL TO {s.To.ToSql()}\nFROM {s.From.ToSql()}\nSUBJECT {s.Subject.ToSql()}\nBODY {s.Body.ToSql()}";
                if (s.Cc          != null && s.Cc.Count          > 0) sql += "\nCC "     + string.Join(", ", s.Cc.Select(e => e.ToSql()));
                if (s.Bcc         != null && s.Bcc.Count         > 0) sql += "\nBCC "    + string.Join(", ", s.Bcc.Select(e => e.ToSql()));
                if (s.Attachments != null && s.Attachments.Count > 0) sql += "\nATTACH " + string.Join(", ", s.Attachments.Select(e => e.ToSql()));
                if (s.ConnectionName != null) sql += $"\nAT {s.ConnectionName.ToSql()}";
                return sql + ";";
            }
            else
            {
                var args = new List<string> { s.ConnectionName?.ToSql() ?? "NULL", s.To.ToSql(), s.From.ToSql(), s.Subject.ToSql(), s.Body.ToSql() };
                if (s.Cc != null || s.Bcc != null || s.Attachments != null)
                {
                    args.Add(s.Cc          != null ? "[" + string.Join(", ", s.Cc.Select(e => e.ToSql()))          + "]" : "NULL");
                    args.Add(s.Bcc         != null ? "[" + string.Join(", ", s.Bcc.Select(e => e.ToSql()))         + "]" : "NULL");
                    args.Add(s.Attachments != null ? "[" + string.Join(", ", s.Attachments.Select(e => e.ToSql())) + "]" : "NULL");
                }
                return $"SEND_EMAIL({string.Join(", ", args)});";
            }
        }

        private static string FormatFileOperation(FileOperationStatement s)
        {
            var op      = s.Type.ToString().ToUpper() + " FILE";
            var dest    = s.Destination != null ? " TO " + s.Destination.ToSql() : "";
            var opts    = new List<string>();
            if (s.Overwrite != null) opts.Add($"OVERWRITE={s.Overwrite.ToSql()}");
            if (s.Password != null)  opts.Add($"PASSWORD={s.Password.ToSql()}");
            if (s.KeyFile != null)   opts.Add($"KEYFILE={s.KeyFile.ToSql()}");
            if (s.PgpKey != null)    opts.Add($"PGP_KEY={s.PgpKey.ToSql()}");
            var options = opts.Count > 0 ? " WITH(" + string.Join(", ", opts) + ")" : "";
            return $"{op} {s.Source.ToSql()}{dest}{options};";
        }

        private static string FormatDirectoryOperation(DirectoryOperationStatement s)
        {
            var op    = s.Type.ToString().ToUpper() + " DIRECTORY" + (s.Type == DirectoryOpType.DeleteContents ? "_CONTENTS" : "");
            var extra = s.Destination != null ? " TO " + s.Destination.ToSql() : "";
            var opts  = new List<string>();
            if (s.Overwrite != null) opts.Add($"OVERWRITE={s.Overwrite.ToSql()}");
            if (s.Recursive != null) opts.Add($"RECURSIVE={s.Recursive.ToSql()}");
            if (s.Password != null)  opts.Add($"PASSWORD={s.Password.ToSql()}");
            if (s.KeyFile != null)   opts.Add($"KEYFILE={s.KeyFile.ToSql()}");
            if (s.PgpKey != null)    opts.Add($"PGP_KEY={s.PgpKey.ToSql()}");
            var with  = opts.Count > 0 ? " WITH(" + string.Join(", ", opts) + ")" : "";
            return $"{op} {s.Path.ToSql()}{extra}{with};";
        }

        private static string FormatFileTransfer(FileTransferStatement s)
        {
            if (s.IsSqlStyle)
            {
                var op     = s.Type == FileTransferType.Send ? "SEND FILE" : "RECEIVE FILE";
                var fromTo = s.Type == FileTransferType.Send
                    ? $"{s.LocalPath.ToSql()} TO {s.RemotePath.ToSql()}"
                    : $"FROM {s.RemotePath.ToSql()} TO {s.LocalPath.ToSql()}";
                var opts   = s.Overwrite != null ? $" WITH(OVERWRITE={s.Overwrite.ToSql()})" : "";
                return $"{op} {fromTo} AT {s.ConnectionName}{opts};";
            }
            else
            {
                var op   = s.Type == FileTransferType.Send ? "SEND_FILE" : "RECEIVE_FILE";
                var args = s.Type == FileTransferType.Send
                    ? new List<string> { s.LocalPath.ToSql(), s.ConnectionName, s.RemotePath.ToSql() }
                    : new List<string> { s.ConnectionName, s.RemotePath.ToSql(), s.LocalPath.ToSql() };
                if (s.Overwrite != null) args.Add(s.Overwrite.ToSql());
                return $"{op}({string.Join(", ", args)});";
            }
        }

        private static string FormatDockerAction(DockerActionStatement s)
        {
            string verb = s.Action.ToString().ToUpper();
            if (s.TargetMode == DockerTargetMode.All) return $"{verb} ALL DOCKER;";
            if (s.TargetMode == DockerTargetMode.LastStarted) return $"{verb} DOCKER;";
            return $"{verb} DOCKER {s.Alias};";
        }

        private static string FormatRunScript(RunScriptStatement s)
        {
            var paramsStr = s.Parameters.Count > 0
                ? " WITH (" + string.Join(", ", s.Parameters.Select(p => $"{p.Name} = {p.Value.ToSql()}{(p.IsOutput ? " OUTPUT" : "")}")) + ")"
                : "";
            return $"RUN SCRIPT {s.PathExpression.ToSql()}{paramsStr};";
        }

        private static string FormatLineage(LineageStatement s)
        {
            if (s.ExportAsOpenLineage && s.TargetTable == null)
                return $"SHOW LINEAGE EXPORT AS OPENLINEAGE TO '{s.ExportPath}';";
            var sql = s.TargetTable != null ? $"SHOW LINEAGE FOR {FormatLineageTarget(s.TargetTable)}" : "SHOW LINEAGE";
            if (s.ColumnName       != null) sql += $" COLUMN {s.ColumnName}";
            if (s.ExportAsOpenLineage && s.ExportPath != null)
                sql += $" EXPORT AS OPENLINEAGE TO '{s.ExportPath}'";
            else if (s.ExportPath  != null) sql += $" TO '{s.ExportPath}'";
            if (s.IntoTable != null) sql += $" INTO {s.IntoTable}";
            return sql + ";";
        }

        private static string FormatLineageTarget(TableReference target)
        {
            if (target.TableName.StartsWith("report:", StringComparison.OrdinalIgnoreCase))
                return "REPORT " + target.TableName["report:".Length..];
            if (target.TableName.StartsWith("dataset:", StringComparison.OrdinalIgnoreCase))
                return "DATASET &" + target.TableName["dataset:".Length..];
            return target.ToSql();
        }

        private static string FormatCreateSets(CreateSetsStatement s)
        {
            var assignments = string.Join(",\n    ", s.Assignments.Select(a => $"@{a.VariableName} = {a.Value.ToSql()}"));
            var prompt      = s.WithPrompt ? "\n    SET WITH_PROMPT ON;" : "";
            return $"CREATE SETS !{s.Name}\nBEGIN\n    {assignments};{prompt}\nEND";
        }

        // ── Merge action helpers ─────────────────────────────────────────────

        private static string FormatMergeAction(MergeActionClause c)
        {
            switch (c.ActionType)
            {
                case MergeActionType.UPDATE:
                    return $"THEN UPDATE SET {string.Join(", ", c.UpdateAssignments!.Select(a => a.ToSql()))}";
                case MergeActionType.INSERT:
                    var cols = c.InsertColumns != null && c.InsertColumns.Count > 0 ? "(" + string.Join(", ", c.InsertColumns) + ") " : "";
                    var vals = string.Join(", ", c.InsertValues!.Select(v => v.ToSql()));
                    return $"THEN INSERT {cols}VALUES ({vals})";
                case MergeActionType.DELETE:
                    return "THEN DELETE";
                default:
                    return "";
            }
        }

        private static string FormatMergeInsert(MergeInsertClause n)
        {
            var cols = n.Columns != null && n.Columns.Count > 0 ? "(" + string.Join(", ", n.Columns) + ") " : "";
            var vals = string.Join(", ", n.Values.Select(v => v.ToSql()));
            return $"THEN INSERT {cols}VALUES ({vals})";
        }

        // ── Expression formatters ────────────────────────────────────────────

        private static string FormatUnary(UnaryExpression e)
        {
            string op = e.Operator switch {
                TokenType.NOT   => "NOT ",
                TokenType.MINUS => "-",
                TokenType.PLUS  => "+",
                _               => e.Operator.ToString()
            };
            return $"{op}{e.Expression.ToSql()}";
        }

        private static string FormatBinary(BinaryExpression e)
        {
            string op = e.Operator switch {
                TokenType.PLUS          => "+",
                TokenType.MINUS         => "-",
                TokenType.STAR          => "*",
                TokenType.SLASH         => "/",
                TokenType.MODULO        => "%",
                TokenType.EQUALS        => "=",
                TokenType.NOT_EQUALS    => "<>",
                TokenType.LESS_THAN     => "<",
                TokenType.LESS_EQUALS   => "<=",
                TokenType.GREATER_THAN  => ">",
                TokenType.GREATER_EQUALS=> ">=",
                TokenType.REGEX_MATCH   => "~",
                TokenType.REGEX_IMATCH  => "~*",
                TokenType.AND           => "AND",
                TokenType.OR            => "OR",
                _                       => e.Operator.ToString()
            };
            return $"({e.Left.ToSql()} {op} {e.Right.ToSql()})";
        }

        private static string FormatLiteral(LiteralExpression e)
        {
            if (e.Value == null) return "NULL";
            var valStr = e.Value.ToString() ?? "";
            if (e.Type == TokenType.TRUE)   return "TRUE";
            if (e.Type == TokenType.FALSE)  return "FALSE";
            if (e.Type == TokenType.STRING_LITERAL) return $"'{valStr.Replace("'", "''")}'";
            return valStr;
        }

        private static string FormatFunctionCall(FunctionCallExpression e)
        {
            var distinct = e.IsDistinct ? "DISTINCT " : "";
            var args     = string.Join(", ", e.Arguments.Select(a => a.ToSql()));
            var sql      = $"{e.FunctionName}({distinct}{args})";
            if (e.WithinGroupOrderBy != null)
                sql += $" WITHIN GROUP (ORDER BY {string.Join(", ", e.WithinGroupOrderBy.Select(o => o.ToSql()))})";
            if (e.Window != null)
                sql += $" OVER ({e.Window.ToSql()})";
            return sql;
        }

        private static string FormatLike(LikeExpression e)
            => $"{e.Left.ToSql()} {(e.IsNot ? "NOT " : "")}{(e.IsCaseInsensitive ? "ILIKE" : "LIKE")} {e.Pattern.ToSql()}{(e.EscapeChar != null ? " ESCAPE " + e.EscapeChar.ToSql() : "")}";

        private static string FormatBetween(BetweenExpression e)
            => $"{e.Left.ToSql()} {(e.IsNot ? "NOT " : "")}BETWEEN {e.Start.ToSql()} AND {e.End.ToSql()}";

        private static string FormatCase(CaseExpression e)
        {
            var sql = "CASE ";
            if (e.InputExpression != null) sql += e.InputExpression.ToSql() + " ";
            foreach (var clause in e.WhenClauses)
                sql += $"WHEN {clause.Condition.ToSql()} THEN {clause.Result.ToSql()} ";
            if (e.ElseResult != null) sql += $"ELSE {e.ElseResult.ToSql()} ";
            return sql + "END";
        }

        private static string FormatTrim(TrimExpression e)
        {
            var typeStr = e.Type.ToString();
            var chars   = e.Characters != null ? $"{e.Characters.ToSql()} FROM " : "";
            return $"TRIM({typeStr} {chars}{e.String.ToSql()})";
        }

        // ── AstNode helper formatters ────────────────────────────────────────

        private static string FormatTableReference(TableReference n)
        {
            string sql;
            if (n.Subquery != null)
                sql = $"({n.Subquery.ToSql().TrimEnd(';')})";
            else if (n.ValuesRows != null)
                sql = $"(VALUES {string.Join(", ", n.ValuesRows.Select(row => $"({string.Join(", ", row.Select(v => v.ToSql()))})"))})";
            else if (n.FunctionCall != null)
                sql = n.FunctionCall.ToSql();
            else
            {
                var parts = new List<string>();
                if (n.ConnectionName != null) parts.Add(n.ConnectionName);
                if (n.DatabaseName   != null) parts.Add(n.DatabaseName);
                if (n.SchemaName     != null) parts.Add(n.SchemaName);
                parts.Add(n.TableName);
                sql = string.Join(".", parts);
            }
            if (n.Alias != null) sql += " AS " + n.Alias;
            if (n.ColumnAliases != null && n.ColumnAliases.Count > 0) sql += $"({string.Join(", ", n.ColumnAliases)})";
            foreach (var op in n.TableOperators)
            {
                if (op is PivotClause   p) sql += " " + p.ToSql();
                else if (op is UnpivotClause u) sql += " " + u.ToSql();
            }
            return sql;
        }

        private static string FormatForClause(ForClause n)
        {
            var options = new List<string>();
            if (n.RootName          != null) options.Add($"ROOT('{n.RootName}')");
            if (n.IncludeNullValues)          options.Add("INCLUDE_NULL_VALUES");
            if (n.WithoutArrayWrapper)        options.Add("WITHOUT_ARRAY_WRAPPER");
            var optStr = options.Count > 0
                ? (n.Type == ForType.JSON ? ", " : " ") + string.Join(", ", options)
                : "";
            return $"FOR {n.Type} {n.Mode}{optStr}";
        }

        private static string FormatWindowFrame(WindowFrame n)
        {
            string start = BoundToSql(n.StartBound, n.StartValue);
            if (n.EndBound == null) return $"{n.Type} {start}";
            return $"{n.Type} BETWEEN {start} AND {BoundToSql(n.EndBound.Value, n.EndValue)}";
        }

        private static string BoundToSql(WindowFrameBoundType bound, Expression? value) => bound switch
        {
            WindowFrameBoundType.PRECEDING            => $"{value?.ToSql()} PRECEDING",
            WindowFrameBoundType.FOLLOWING            => $"{value?.ToSql()} FOLLOWING",
            WindowFrameBoundType.CURRENT_ROW          => "CURRENT ROW",
            WindowFrameBoundType.UNBOUNDED_PRECEDING  => "UNBOUNDED PRECEDING",
            WindowFrameBoundType.UNBOUNDED_FOLLOWING  => "UNBOUNDED FOLLOWING",
            _                                         => ""
        };

        private static string FormatWindowClause(WindowClause n)
        {
            var parts = new List<string>();
            if (n.PartitionBy.Count > 0)
                parts.Add("PARTITION BY " + string.Join(", ", n.PartitionBy.Select(p => p.ToSql())));
            if (n.OrderBy.Count > 0)
                parts.Add("ORDER BY " + string.Join(", ", n.OrderBy.Select(o => o.ToSql())));
            if (n.Frame != null)
                parts.Add(n.Frame.ToSql());
            return string.Join(" ", parts);
        }

        // ── Non-AstNode type formatters ──────────────────────────────────────

        private static string FormatJoin(JoinClause join)
        {
            var hintStr = join.Hint switch {
                JoinHint.Hash  => "HASH ",
                JoinHint.Loop  => "LOOP ",
                JoinHint.Merge => "MERGE ",
                _              => ""
            };
            if (join.JoinType == "INNER" && join.Hint != JoinHint.None) return $"INNER {hintStr}JOIN {join.Table.ToSql()} ON {join.Condition.ToSql()}";
            if (join.JoinType == "LEFT"  && join.Hint != JoinHint.None) return $"LEFT {hintStr}OUTER JOIN {join.Table.ToSql()} ON {join.Condition.ToSql()}";
            if (join.JoinType == "RIGHT" && join.Hint != JoinHint.None) return $"RIGHT {hintStr}OUTER JOIN {join.Table.ToSql()} ON {join.Condition.ToSql()}";
            if (join.JoinType == "FULL"  && join.Hint != JoinHint.None) return $"FULL {hintStr}OUTER JOIN {join.Table.ToSql()} ON {join.Condition.ToSql()}";
            return join.IsApply ? $"{join.JoinType} {join.Table.ToSql()}" : $"{join.JoinType} JOIN {join.Table.ToSql()} ON {join.Condition.ToSql()}";
        }

        private static string FormatGroupingSet(GroupingSetClause g)
        {
            string FmtSet(List<Expression> set) =>
                set.Count == 0 ? "()" : $"({string.Join(", ", set.Select(e => e.ToSql()))})";

            return g.Type switch
            {
                GroupingSetType.Rollup       => $"ROLLUP({string.Join(", ", g.GroupSets[0].Select(e => e.ToSql()))})",
                GroupingSetType.Cube         => $"CUBE({string.Join(", ", g.GroupSets[0].Select(e => e.ToSql()))})",
                GroupingSetType.GroupingSets => $"GROUPING SETS({string.Join(", ", g.GroupSets.Select(FmtSet))})",
                _                            => string.Join(", ", g.GroupSets[0].Select(e => e.ToSql()))
            };
        }

        private static string FormatColumnDefinition(ColumnDefinition col)
        {
            var pk      = col.IsPrimaryKey    ? " PRIMARY KEY" : "";
            var unq     = col.IsUnique        ? " UNIQUE"      : "";
            var nullable= !col.IsNullable     ? " NOT NULL"    : "";
            var identity= col.IsIdentity      ? " IDENTITY"    : "";
            var def     = col.DefaultExpression != null ? $" DEFAULT {col.DefaultExpression.ToSql()}" : "";
            var check   = col.CheckConstraint   != null ? $" CHECK ({col.CheckConstraint.ToSql()})"   : "";
            var fk      = col.ForeignKey         != null ? $" {col.ForeignKey.ToSql()}"               : "";
            var tags    = col.Metadata.Count > 0
                ? " /* " + string.Join(" ", col.Metadata.Select(kv => $"@{kv.Key}: {kv.Value}")) + " */"
                : "";
            return $"{col.ColumnName} {col.DataType}{pk}{unq}{nullable}{identity}{def}{check}{fk}{tags}";
        }
        private static string FormatCreateVisual(CreateVisualStatement s)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"CREATE VISUAL {s.Name} AS {s.VisualType.ToString().ToUpper()} (");
            if (s.Title != null) sb.AppendLine($"    TITLE = {s.Title.ToSql()},");
            if (s.Subtitle != null) sb.AppendLine($"    SUBTITLE = {s.Subtitle.ToSql()},");
            // TEXT visuals use CONTENT; controls use DEFAULT; both map to DefaultValue on the AST node
            if (s.DefaultValue != null && s.VisualType == VisualType.Text)
                sb.AppendLine($"    CONTENT = {s.DefaultValue.ToSql()},");
            // Only emit SOURCE when actually set (TEXT/controls without a query have an empty source)
            if (s.Source.InlineSelect != null || s.Source.TempTableName != null)
                sb.AppendLine($"    SOURCE = {s.Source.ToSql()},");
            if (s.Mappings.Count > 0)
                sb.AppendLine($"    MAPPINGS ( {string.Join(", ", s.Mappings.Select(m => $"{m.Role} = {m.Column}"))} ),");
            if (s.Options.Count > 0)
                sb.AppendLine($"    OPTIONS ( {string.Join(", ", s.Options.Select(o => $"{o.Key} = '{o.Value.Replace("'", "''")}'"))} ),");
            foreach (var axis in s.AxisOptions)
                sb.AppendLine($"    {axis.Axis}_AXIS ( {string.Join(", ", axis.Options.Select(o => $"{o.Key} = '{o.Value.Replace("'", "''")}'"))} ),");
            if (s.Actions.Count > 0)
                sb.AppendLine($"    ACTIONS ( {string.Join(", ", s.Actions.Select(a => a.ToSql()))} ),");
            if (s.DefaultValue != null && s.VisualType != VisualType.Text)
                sb.AppendLine($"    DEFAULT = {s.DefaultValue.ToSql()},");

            var result = sb.ToString().TrimEnd().TrimEnd(',');
            return result + "\n);";
        }

        private static string FormatSetThreshold(SetThresholdStatement s)
        {
            string name = s.Type switch
            {
                ThresholdType.JoinSpill => "JOIN_SPILL_THRESHOLD",
                ThresholdType.ExternalHashPartitions => "EXTERNAL_HASH_PARTITIONS",
                ThresholdType.ExternalSortChunkSize => "EXTERNAL_SORT_CHUNK_SIZE",
                ThresholdType.CaseSensitive => "CASE_SENSITIVE",
                _ => "UNKNOWN"
            };
            return $"SET {name} = {s.Value.ToSql()};";
        }

        private static string FormatCreateTemplate(CreateTemplateStatement s)
        {
            var options = string.Join(", ", s.Options.Select(o => $"{o.Key} = '{o.Value.Replace("'", "''")}'"));
            var modeStr = s.Mode switch
            {
                ObjectCreationMode.Alter => "ALTER",
                ObjectCreationMode.CreateOrAlter => "CREATE OR ALTER",
                _ => "CREATE"
            };
            return $"{modeStr} TEMPLATE {s.Name} AS ({options});";
        }

        private static string FormatCreateTheme(CreateThemeStatement s)
        {
            var props = string.Join(", ", s.Properties.Select(p => $"{p.Key} = '{p.Value.Replace("'", "''")}'"));
            var modeStr = s.Mode switch
            {
                ObjectCreationMode.Alter => "ALTER",
                ObjectCreationMode.CreateOrAlter => "CREATE OR ALTER",
                _ => "CREATE"
            };
            return $"{modeStr} THEME {s.Name} AS ({props});";
        }
    }
}
