using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Quality;

namespace ETL_SQL.Core.Formatting;
/// <summary>
/// Central AST-to-SQL serializer. Each AST node's <c>ToSql()</c> delegates here
/// instead of containing logic inline. This keeps <c>Ast.cs</c> as pure data models.
/// </summary>
public static class AstSerializer
{
    // ── Public dispatch for AstNode-derived types ─────────────────────

    public static string Format(AstNode node) => node switch
    {
        Script s => string.Join(Environment.NewLine, s.Statements.Select(stmt => stmt.ToSql())),

        // ── Trivial statements ──
        NoOpStatement _ => ";",
        BreakStatement _ => "BREAK;",
        ContinueStatement _ => "CONTINUE;",
        SectionLabelStatement s => $"{s.LabelName}:",
        GotoStatement s => $"GOTO {s.LabelName};",
        ClearSessionStatement _ => "CLEAR SESSION;",
        TryCatchStatement _ => "TRY ... CATCH ... END",
        BlockStatement _ => "BEGIN ... END",

        // ── DML ──
        SelectStatement s => FormatSelect(s),
        SetOperationStatement s => FormatSetOperation(s),
        InsertStatement s => FormatInsert(s),
        UpdateStatement s => FormatUpdate(s),
        DeleteStatement s => FormatDelete(s),
        ReplayQuarantineStatement s => $"REPLAY QUARANTINE {s.QuarantineTable.ToSql()};",
        MergeStatement s => FormatMerge(s),

        // ── Execution ──
        ExecStatement s => FormatExec(s),
        ExecuteRemoteBlockStatement s => $"EXECUTE ({s.ConnectionName.ToSql()}) BEGIN ... END",
        ExecuteToolStatement s => FormatExecuteTool(s),
        ExecutePushdownStatement s => FormatExecutePushdown(s),
        ExecuteStatement s => FormatExecuteProcedure(s),

        // ── DDL ──
        CreateBindingStatement s => FormatCreateBinding(s),
        GrantBindingStatement s => FormatGrantBinding(s),
        RevokeBindingStatement s => FormatRevokeBinding(s),
        CreateConnectionStatement s => FormatCreateConnection(s),

        AlterConnectionStatement s => FormatAlterConnection(s),
        CreateSshKeyPairStatement s => FormatCreateSshKeyPair(s),
        CreatePgpKeyPairStatement s => FormatCreatePgpKeyPair(s),
        CreateTableStatement s => FormatCreateTable(s),
        AlterTableStatement s => FormatAlterTable(s),
        DropTableStatement s => $"DROP TABLE {(s.IfExists ? "IF EXISTS " : "")}{s.TargetTable.ToSql()};",
        DropConnectionStatement s => $"DROP CONNECTION {(s.IfExists ? "IF EXISTS " : "")}{s.ConnectionName};",
        DropProcedureStatement s => $"DROP PROCEDURE {(s.IfExists ? "IF EXISTS " : "")}{s.ProcedureName};",
        DropFunctionStatement s => $"DROP FUNCTION {(s.IfExists ? "IF EXISTS " : "")}{s.FunctionName};",
        DropViewStatement s => $"DROP VIEW {(s.IfExists ? "IF EXISTS " : "")}{s.ViewName};",
        DropIndexStatement s => $"DROP INDEX {(s.IfExists ? "IF EXISTS " : "")}{s.IndexName}{(s.Table != null ? " ON " + s.Table.ToSql() : "")};",
        TruncateTableStatement s => $"TRUNCATE TABLE {s.TargetTable.ToSql()};",
        CreateIndexStatement s => FormatCreateIndex(s),
        CreateProcedureStatement s => FormatCreateProcedure(s),
        CreateFunctionStatement s => FormatCreateFunction(s),
        CreateViewStatement s => FormatCreateView(s),

        // ── Variables & flow ──
        DeclareStatement s => FormatDeclare(s),
        SetVariableStatement s => $"SET {s.Target.ToSql()} = {s.Value.ToSql()};",
        WhileStatement s => $"WHILE {s.Condition.ToSql()} BEGIN ... END",
        ForStatement s => $"FOR {s.VariableName} = {s.StartValue.ToSql()} TO {s.EndValue.ToSql()} BEGIN ... END",
        ForeachStatement s => $"FOREACH {s.VariableName} IN {s.ListExpression.ToSql()} BEGIN ... END",
        IfStatement s => FormatIf(s),
        ParallelStatement s => "PARALLEL " + (s.ConcurrencyLimit > 0 ? $"({s.ConcurrencyLimit}) " : "") + s.Body.ToSql(),
        ParallelForStatement s => $"PARALLEL FOR {s.VariableName} = {s.StartValue.ToSql()} TO {s.EndValue.ToSql()}{(s.StepValue != null ? $" STEP {s.StepValue.ToSql()}" : "")}{(s.ConcurrencyLimit > 0 ? $" CONCURRENCY {s.ConcurrencyLimit}" : "")} BEGIN ... END",

        // ── Transactions ──
        BeginTransactionStatement s => s.Name != null ? $"BEGIN TRANSACTION {s.Name};" : "BEGIN TRANSACTION;",
        CommitTransactionStatement s => s.Name != null ? $"COMMIT TRANSACTION {s.Name};" : "COMMIT TRANSACTION;",
        RollbackTransactionStatement s => s.Name != null ? $"ROLLBACK TRANSACTION {s.Name};" : "ROLLBACK TRANSACTION;",

        // ── Error handling ──
        ThrowStatement s => s.Message != null ? $"THROW {s.Message.ToSql()};" : "THROW;",
        ReturnStatement s => s.ReturnValue != null ? $"RETURN {s.ReturnValue.ToSql()};" : "RETURN;",
        RaiseErrorStatement s => FormatRaiseError(s),

        // ── I/O & messaging ──
        PrintStatement s => FormatPrint(s),
        BulkInsertStatement s => FormatBulkInsert(s),
        ExportStatement s => FormatExport(s),
        ExportReportStatement s => FormatExportReport(s),
        EmailStatement s => FormatEmail(s),
        FileOperationStatement s => FormatFileOperation(s),
        DirectoryOperationStatement s => FormatDirectoryOperation(s),
        FileTransferStatement s => FormatFileTransfer(s),
        WaitForFileStatement s => $"WAITFOR FILE UNLOCKED {s.Path.ToSql()}" + (s.Timeout != null ? $" TIMEOUT {s.Timeout.ToSql()}" : "") + (s.PollInterval != null ? $" POLL_INTERVAL_MS {s.PollInterval.ToSql()}" : "") + ";",
        ConvertFileEncodingStatement s => $"CONVERT FILE ENCODING {s.Source.ToSql()} TO {s.Destination.ToSql()} FROM_ENCODING {s.FromEncoding.ToSql()} TO_ENCODING {s.ToEncoding.ToSql()}" + (s.Overwrite != null ? $" WITH(OVERWRITE={s.Overwrite.ToSql()})" : "") + ";",
        SplitFileStatement s => $"SPLIT FILE {s.Source.ToSql()} TO {s.DestinationDir.ToSql()} WITH(LIMIT_TYPE={s.LimitType.ToSql()}, LIMIT_VALUE={s.LimitValue.ToSql()}" + (s.Prefix != null ? $", PREFIX={s.Prefix.ToSql()}" : "") + (s.Overwrite != null ? $", OVERWRITE={s.Overwrite.ToSql()}" : "") + ");",
        MergeFilesStatement s => $"MERGE FILES {s.Source.ToSql()} TO {s.Destination.ToSql()}" + (s.Header != null || s.Overwrite != null ? " WITH(" + string.Join(", ", new[] { s.Header != null ? $"HEADER={s.Header.ToSql()}" : null, s.Overwrite != null ? $"OVERWRITE={s.Overwrite.ToSql()}" : null }.Where(x => x != null)) + ")" : "") + ";",
        SyncDirectoryStatement s => $"SYNC DIRECTORY {s.Source.ToSql()} TO {s.Destination.ToSql()}" + (s.DeleteExtra != null || s.Overwrite != null || s.Recursive != null ? " WITH(" + string.Join(", ", new[] { s.DeleteExtra != null ? $"DELETE_EXTRA={s.DeleteExtra.ToSql()}" : null, s.Overwrite != null ? $"OVERWRITE={s.Overwrite.ToSql()}" : null, s.Recursive != null ? $"RECURSIVE={s.Recursive.ToSql()}" : null }.Where(x => x != null)) + ")" : "") + ";",
        VerifyFileIntegrityStatement s => $"VERIFY FILE INTEGRITY {s.Source.ToSql()}" + (s.HashFile != null || s.ExpectedHash != null || s.Algorithm != null ? " WITH(" + string.Join(", ", new[] { s.HashFile != null ? $"HASH_FILE={s.HashFile.ToSql()}" : null, s.ExpectedHash != null ? $"EXPECTED_HASH={s.ExpectedHash.ToSql()}" : null, s.Algorithm != null ? $"ALGORITHM={s.Algorithm.ToSql()}" : null }.Where(x => x != null)) + ")" : "") + ";",
        ExpectSchemaStatement s => FormatExpectSchema(s),

        // ── Docker ──
        DockerStatement s => s.Alias != null ? $"USE DOCKER({s.ImageName.ToSql()}) AS {s.Alias};" : $"USE DOCKER({s.ImageName.ToSql()});",
        DockerActionStatement s => FormatDockerAction(s),

        // ── Jobs & scheduling ──
        CreateJobStatement s =>
            $"{CreationVerb(s.Mode)} JOB {s.JobName} FOR {s.TargetKind.ToString().ToUpperInvariant()} {Quote(s.TargetPath)}"
            + FormatJobOptions(s) + ";",
        DropJobStatement s => $"DROP JOB {(s.IfExists ? "IF EXISTS " : "")}{s.Name};",
        AlterJobStatement s => FormatAlterJob(s),
        EnableJobStatement s => $"ENABLE JOB {s.Name}" + (s.At != null ? $" AT {s.At}" : "") + ";",
        DisableJobStatement s => $"DISABLE JOB {s.Name}" + (s.At != null ? $" AT {s.At}" : "") + ";",
        TriggerJobStatement s => $"TRIGGER JOB {s.Name}" + (s.At != null ? $" AT {s.At}" : "") + ";",
        KillJobStatement s => $"KILL JOB {s.JobIdExpr.ToSql()};",

        // ── Scheduler catalog ──
        // These must round-trip: ConfigurationExportService emits them and the export has to replay
        // into exactly what it describes.
        CreateScheduleStatement s =>
            $"{CreationVerb(s.Mode)} SCHEDULE {s.Name} ON {Quote(s.Cron)}"
            + (s.TimeZone != null ? $" AT TIME ZONE {Quote(s.TimeZone)}" : "")
            + FormatCatalogMetadata(s.Metadata) + ";",
        CreateNotificationStatement s =>
            $"{CreationVerb(s.Mode)} NOTIFICATION {s.Name} USING {s.ConnectionName}"
            + (s.Recipient != null ? $" TO {Quote(s.Recipient)}" : "")
            + FormatCatalogMetadata(s.Metadata) + ";",
        AlterCatalogObjectStatement s => FormatAlterCatalogObject(s),
        DropCatalogObjectStatement s =>
            $"DROP {s.Kind.ToString().ToUpperInvariant()} {(s.IfExists ? "IF EXISTS " : "")}{s.Name};",
        SetCatalogObjectEnabledStatement s =>
            $"{(s.IsEnabled ? "ENABLE" : "DISABLE")} {s.Kind.ToString().ToUpperInvariant()} {s.Name};",
        AlterJobAttachmentStatement s =>
            $"ALTER JOB {s.JobName} {s.Action.ToString().ToUpperInvariant()} "
            + $"{s.Kind.ToString().ToUpperInvariant()} {s.TargetName}"
            + (s.Trigger != null ? $" ON {s.Trigger.ToUpperInvariant()}" : "") + ";",

        // ── Portal alerts ──
        CreatePortalToolStatement s =>
            $"{CreationVerb(s.Mode)} TOOL {s.ToolName} AS {s.ToolType}"
            + (s.Options != null && s.Options.Count > 0 ? $"({string.Join(", ", s.Options.Select(kv => $"{kv.Key} = {kv.Value.ToSql()}"))})" : "")
            + ";",
        DropPortalToolStatement s => $"DROP TOOL {(s.IfExists ? "IF EXISTS " : "")}{s.ToolName};",
        CreatePortalAlertStatement s =>
            $"{CreationVerb(s.Mode)} ALERT {s.Name} FOR REPORT {Quote(s.ReportName)} "
            + $"WHEN VISUAL {s.VisualName} {s.Operator} {s.Threshold}"
            + FormatCatalogMetadata(s.Metadata) + ";",
        AlterPortalAlertNotificationStatement s =>
            $"ALTER ALERT {s.AlertName} {s.Action.ToString().ToUpperInvariant()} NOTIFICATION {s.Notification};",
        AlterPortalAlertStatement s =>
            $"ALTER ALERT {s.Name} SET" + FormatCatalogMetadata(s.Metadata).Replace(" WITH", "") + ";",
        DropPortalAlertStatement s => $"DROP ALERT {(s.IfExists ? "IF EXISTS " : "")}{s.Name};",
        SetPortalAlertEnabledStatement s => $"{(s.IsEnabled ? "ENABLE" : "DISABLE")} ALERT {s.Name};",
        CreatePortalShareLinkStatement s =>
            $"CREATE SHARE LINK {Quote(s.Name)} FOR REPORT {Quote(s.ReportName)}"
            + (s.ExpiresAt != null ? $" EXPIRES {Quote(s.ExpiresAt)}" : "")
            + (s.IntoTable != null ? $" INTO {s.IntoTable}" : "")
            + ";",
        RevokePortalShareLinkStatement s =>
            $"REVOKE SHARE LINK {Quote(s.Name)}"
            + (s.ReportName != null ? $" FOR REPORT {Quote(s.ReportName)}" : "")
            + ";",
        CreatePortalEmbedTokenStatement s =>
            $"CREATE EMBED TOKEN {Quote(s.Name)} FOR REPORT {Quote(s.ReportName)}"
            + (s.ExpiresAt != null ? $" EXPIRES {Quote(s.ExpiresAt)}" : "")
            + (s.IntoTable != null ? $" INTO {s.IntoTable}" : "")
            + ";",
        RevokePortalEmbedTokenStatement s =>
            $"REVOKE EMBED TOKEN {Quote(s.Name)}"
            + (s.ReportName != null ? $" FOR REPORT {Quote(s.ReportName)}" : "")
            + ";",
        ShowPortalShareLinksStatement s =>
            $"SHOW SHARE LINKS FOR REPORT {Quote(s.ReportName)}"
            + (s.IntoTable != null ? $" INTO {s.IntoTable}" : "")
            + ";",
        ShowPortalEmbedTokensStatement s =>
            $"SHOW EMBED TOKENS FOR REPORT {Quote(s.ReportName)}"
            + (s.IntoTable != null ? $" INTO {s.IntoTable}" : "")
            + ";",
        CreatePortalUserStatement s => $"CREATE USER {s.Username} ROLE {s.Role} EMAIL {s.Email};",
        AlterPortalUserStatement s => $"ALTER USER {s.Username} ...;",
        DropPortalUserStatement s => $"DROP USER {s.Username}{(s.Cascade ? " CASCADE" : "")};",
        CreatePortalGroupStatement s => $"CREATE GROUP {s.Name};",
        DropPortalGroupStatement s => $"DROP GROUP {s.Name}{(s.Cascade ? " CASCADE" : "")};",
        AddUserToPortalGroupStatement s => $"ADD USER {s.Username} TO GROUP {s.GroupName};",
        CreatePortalFolderStatement s => $"CREATE FOLDER '{s.Path}'"
            + (s.CatalogOwner != null ? $" WITH (CATALOG_OWNER = {Quote(s.CatalogOwner)})" : "") + ";",
        AlterPortalFolderStatement s => $"ALTER FOLDER '{s.Path}' ...;",
        DropPortalFolderStatement s => $"DROP FOLDER '{s.Path}'{(s.Cascade ? " CASCADE" : "")};",
        GrantPortalPermissionStatement s => $"GRANT {s.Permission.ToString().ToUpper()} ON FOLDER '{s.FolderPath}' TO GROUP {s.GroupName};",
        RevokePortalPermissionStatement s => $"REVOKE {s.Permission.ToString().ToUpper()} ON FOLDER '{s.FolderPath}' FROM GROUP {s.GroupName};",
        AlterPortalDatasetStatement s => $"ALTER DATASET {s.DatasetName} ...;",
        RefreshPortalDatasetStatement s => $"REFRESH DATASET {s.DatasetName} ON FOLDER '{s.FolderPath}';",
        DropPortalDatasetStatement s => $"DROP DATASET {s.DatasetName} ON FOLDER '{s.FolderPath}';",
        GrantPortalDatasetPermissionStatement s => $"GRANT {s.Permission.ToString().ToUpper()} ON DATASET {s.DatasetName} ON FOLDER '{s.FolderPath}' TO GROUP {s.GroupName};",
        RevokePortalDatasetPermissionStatement s => $"REVOKE {s.Permission.ToString().ToUpper()} ON DATASET {s.DatasetName} ON FOLDER '{s.FolderPath}' FROM GROUP {s.GroupName};",
        PublishPortalReportStatement s => $"PUBLISH REPORT {Quote(s.ReportName)} FROM {Quote(s.ScriptPath)} IN FOLDER {Quote(s.FolderPath)}"
            + (s.Description != null || s.CatalogOwner != null
                ? " WITH (" + string.Join(", ", new[]
                {
                    s.Description != null ? $"DESCRIPTION = {Quote(s.Description)}" : null,
                    s.CatalogOwner != null ? $"CATALOG_OWNER = {Quote(s.CatalogOwner)}" : null
                }.Where(value => value != null)) + ")"
                : "") + ";",
        AlterPortalReportStatement s => $"ALTER REPORT {s.ReportName} ...;",
        DropPortalReportStatement s => $"DROP REPORT {s.ReportName}{(s.Cascade ? " CASCADE" : "")};",
        FavoritePortalReportStatement s => $"FAVORITE REPORT {s.ReportName}" + (s.Username != null ? $" FOR {s.Username}" : "") + ";",
        UnfavoritePortalReportStatement s => $"UNFAVORITE REPORT {s.ReportName}" + (s.Username != null ? $" FOR {s.Username}" : "") + ";",
        CreatePortalSavedViewStatement s => $"CREATE SAVED VIEW {s.Name} FOR REPORT {s.ReportName} ...;",
        DropPortalSavedViewStatement s => $"DROP SAVED VIEW {s.Name} FOR REPORT {s.ReportName};",
        CreatePortalRefreshJobStatement s => $"CREATE REFRESH JOB FOR REPORT {s.ReportName} ON '{s.Schedule}' USING {s.OrchestratorAlias};",
        RefreshPortalReportStatement s => $"REFRESH REPORT {s.ReportName};",
        DropPortalRefreshJobStatement s => $"DROP REFRESH JOB FOR REPORT {s.ReportName};",
        DropPortalSnapshotStatement s => $"DROP SNAPSHOT FOR REPORT {s.ReportName};",
        RebuildPortalSnapshotStatement s => $"REBUILD SNAPSHOT FOR REPORT {s.ReportName};",
        CreatePortalSubscriptionStatement s => $"CREATE SUBSCRIPTION FOR REPORT {s.ReportPath} TO {s.Recipient} ...;",
        AlterPortalSubscriptionStatement s => $"ALTER SUBSCRIPTION {s.SubscriptionId} SET ...;",
        DropPortalSubscriptionStatement s => $"DROP SUBSCRIPTION {s.SubscriptionId};",
        DisconnectPortalUserStatement s => $"DISCONNECT USER {s.Username};",
        RevokePortalTokensStatement s => $"REVOKE TOKENS FOR USER {s.Username};",
        RestartPortalStatement _ => "RESTART PORTAL;",
        ExportPortalConfigurationStatement s => $"EXPORT PORTAL CONFIGURATION TO '{s.TargetPath}';",
        ShutdownPortalStatement _ => "SHUTDOWN PORTAL;",
        ShowPortalUsersStatement _ => "SHOW PORTAL USAGE USERS;",
        ShowPortalReportsStatement s => $"SHOW PORTAL REPORTS" + (s.FolderPath != null ? $" ON FOLDER '{s.FolderPath}'" : "") + (s.IntoTable != null ? $" INTO {s.IntoTable};" : ";"),
        ShowPortalReportStatement s => $"SHOW PORTAL REPORT {s.ReportName}" + (s.IntoTable != null ? $" INTO {s.IntoTable};" : ";"),
        ShowPortalReportHistoryStatement s => $"SHOW PORTAL REPORT HISTORY FOR {s.ReportName}" + (s.IntoTable != null ? $" INTO {s.IntoTable};" : ";"),
        ShowPortalReportDependenciesStatement s => $"SHOW PORTAL REPORT DEPENDENCIES FOR {s.ReportName}" + (s.IntoTable != null ? $" INTO {s.IntoTable};" : ";"),
        ShowPortalSavedViewsStatement s => $"SHOW PORTAL SAVED VIEWS FOR {s.ReportName}" + (s.IntoTable != null ? $" INTO {s.IntoTable};" : ";"),
        ShowPortalAlertsStatement s => $"SHOW PORTAL ALERTS FOR {s.ReportName}" + (s.IntoTable != null ? $" INTO {s.IntoTable};" : ";"),
        ShowPortalFavoritesStatement s => $"SHOW PORTAL FAVORITES" + (s.Username != null ? $" FOR {s.Username}" : "") + (s.IntoTable != null ? $" INTO {s.IntoTable};" : ";"),
        ShowPortalRecentReportsStatement s => $"SHOW PORTAL RECENT REPORTS" + (s.Limit != null ? $" LIMIT {s.Limit}" : "") + (s.IntoTable != null ? $" INTO {s.IntoTable};" : ";"),
        SearchPortalCatalogStatement s => $"SEARCH PORTAL CATALOG FOR '{s.Query}'" + (s.Limit != null ? $" LIMIT {s.Limit}" : "") + (s.IntoTable != null ? $" INTO {s.IntoTable};" : ";"),
        ShowEffectivePortalPermissionsStatement s => $"SHOW EFFECTIVE PORTAL PERMISSIONS FOR {s.TargetType} {s.Target}" + (s.IntoTable != null ? $" INTO {s.IntoTable};" : ";"),
        ShowPortalUsageMetricsStatement s => $"SHOW PORTAL USAGE METRICS" + (s.Days != null ? $" FOR {s.Days} DAYS" : "") + (s.IntoTable != null ? $" INTO {s.IntoTable};" : ";"),
        ShowPortalOperationalMetricsStatement s => $"SHOW PORTAL OPERATIONAL METRICS" + (s.IntoTable != null ? $" INTO {s.IntoTable};" : ";"),
        ShowPortalAuditStatement s => "SHOW PORTAL AUDIT" + (s.Action != null ? $" FOR ACTION {s.Action}" : "") + (s.Limit != null ? $" LIMIT {s.Limit}" : "") + (s.IntoTable != null ? $" INTO {s.IntoTable};" : ";"),
        ShowActivePortalSessionsStatement s => "SHOW ACTIVE PORTAL SESSIONS" + (s.IntoTable != null ? $" INTO {s.IntoTable};" : ";"),
        ValidatePortalReportStatement s => $"VALIDATE REPORT SCRIPT '{s.ScriptPath}'" + (s.IntoTable != null ? $" INTO {s.IntoTable};" : ";"),
        ShowJobHistoryStatement s => (s.JobName != null ? $"SHOW JOB HISTORY {s.JobName}" : "SHOW JOB HISTORY") + (s.At != null ? $" AT {s.At}" : "") + (s.IntoTable != null ? $" INTO {s.IntoTable};" : ";"),
        ShowJobsStatement s => "SHOW JOBS" + (s.At != null ? $" AT {s.At}" : "") + (s.IntoTable != null ? $" INTO {s.IntoTable};" : ";"),

        // ── Bundles & scripts ──
        PublishBundleStatement s => FormatPublishBundle(s),
        ValidateBundleStatement s => FormatValidateBundle(s),
        ExportScriptStatement s => FormatExportScript(s),

        // ── SHOW ──
        ShowPublishedBundlesStatement s => (s.IsAlias ? "SHOW BUNDLES" : "SHOW PUBLISHED BUNDLES") + (s.At != null ? $" AT {s.At}" : "") + (s.IntoTable != null ? $" INTO {s.IntoTable};" : ";"),
        ShowBundleVersionsStatement s => $"SHOW BUNDLE VERSIONS '{s.BundleName.Replace("'", "''")}'" + (s.At != null ? $" AT {s.At}" : "") + (s.IntoTable != null ? $" INTO {s.IntoTable};" : ";"),
        ShowBundleFilesStatement s => $"SHOW BUNDLE FILES '{s.BundleName.Replace("'", "''")}' VERSION {s.Version}" + (s.At != null ? $" AT {s.At}" : "") + (s.IntoTable != null ? $" INTO {s.IntoTable};" : ";"),
        ShowBundleDependenciesStatement s => $"SHOW BUNDLE DEPENDENCIES '{s.BundleName.Replace("'", "''")}' VERSION {s.Version}" + (s.At != null ? $" AT {s.At}" : "") + (s.IntoTable != null ? $" INTO {s.IntoTable};" : ";"),
        ShowConnectionsStatement s => "SHOW CONNECTIONS" + (s.IntoTable != null ? $" INTO {s.IntoTable};" : ";"),
        ShowConnectionConfigStatement s => $"SHOW CONNECTION {s.ConnectionName} CONFIG" + (s.IntoTable != null ? $" INTO {s.IntoTable};" : ";"),
        ShowTablesStatement s => (s.ConnectionName != null ? $"SHOW TABLES ON {s.ConnectionName}" : "SHOW TABLES") + (s.IntoTable != null ? $" INTO {s.IntoTable};" : ";"),
        ShowVariablesStatement s => (s.IsLocalOnly ? "SHOW LOCAL VARIABLES" : "SHOW VARIABLES") + (s.IntoTable != null ? $" INTO {s.IntoTable}" : "") + ";",
        ShowSessionsStatement s => "SHOW SESSIONS" + (s.IntoTable != null ? $" INTO {s.IntoTable};" : ";"),
        ShowLocksStatement s => "SHOW LOCKS" + (s.IntoTable != null ? $" INTO {s.IntoTable};" : ";"),
        ShowSafeZonesStatement s => "SHOW SAFE ZONES" + (s.IntoTable != null ? $" INTO {s.IntoTable};" : ";"),
        ShowDataQualityRulesStatement s => "SHOW DATA QUALITY RULES" + (s.TargetTable != null ? $" FOR TABLE {s.TargetTable.ToSql()}" : "") + (s.ColumnName != null ? $" COLUMN {s.ColumnName}" : "") + (s.IntoTable != null ? $" INTO {s.IntoTable};" : ";"),
        ShowLineageHistoryForTableStatement s => $"SHOW LINEAGE HISTORY FOR TABLE {s.TableName}" + (s.Limit != null ? $" LIMIT {s.Limit}" : "") + (s.IntoTable != null ? $" INTO {s.IntoTable};" : ";"),
        ShowLineageHistoryForTagStatement s => $"SHOW LINEAGE HISTORY FOR TAG {s.TagKey}" + (s.TagValue != null ? $" = '{s.TagValue}'" : "") + (s.Limit != null ? $" LIMIT {s.Limit}" : "") + (s.IntoTable != null ? $" INTO {s.IntoTable};" : ";"),
        ShowLineageHistoryForMissingTagsStatement s => "SHOW LINEAGE HISTORY FOR MISSING TAGS" + (s.Limit != null ? $" LIMIT {s.Limit}" : "") + (s.IntoTable != null ? $" INTO {s.IntoTable};" : ";"),
        ShowLineageHistoryForJobStatement s => $"SHOW LINEAGE HISTORY FOR JOB {s.JobName}" + (s.Limit != null ? $" LIMIT {s.Limit}" : "") + (s.IntoTable != null ? $" INTO {s.IntoTable};" : ";"),
        ShowProtectedDataStatement s => "SHOW PROTECTED DATA" + (s.Suggestions ? " SUGGESTIONS" : "") + (s.Limit != null ? $" LIMIT {s.Limit}" : "") + (s.IntoTable != null ? $" INTO {s.IntoTable};" : ";"),
        ShowHostMetricsStatement s => "SHOW HOST METRICS" + (s.NodeId != null ? $" FOR '{s.NodeId}'" : "") + (s.IntoTable != null ? $" INTO {s.IntoTable};" : ";"),
        ShowJobStateStatement s => "SHOW JOB STATE" + (s.JobName != null ? $" '{s.JobName}'" : "") + (s.IntoTable != null ? $" INTO {s.IntoTable};" : ";"),
        ShowViewsStatement s => "SHOW VIEWS" + (s.IntoTable != null ? $" INTO {s.IntoTable};" : ";"),
        TestConnectionStatement s => $"TEST CONNECTION {s.ConnectionName}" + (s.IntoTable != null ? $" INTO {s.IntoTable};" : ";"),
        // ── Misc statements ──
        RunScriptStatement s => FormatRunScript(s),
        WaitForStatement s => $"WAITFOR {s.Type.ToString().ToUpper()} {s.Expression.ToSql()};",
        ExplainStatement s => (s.IsAnalyze ? "EXPLAIN ANALYZE " : "EXPLAIN ") + s.Query.ToSql() + (s.IntoTable != null ? " INTO " + s.IntoTable.ToSql() : ""),
        CreateVisualStatement s => FormatCreateVisual(s),
        TitleDefinition s => FormatTitleDefinition(s),
        CascadeDefinition s => FormatCascade(s),
        AdvancedChartDefinition s => FormatAdvancedChart(s),
        HtmlTemplateDefinition s => FormatHtmlTemplate(s),
        CreatePageStatement s => FormatCreatePage(s),
        CreateDatasetStatement s => FormatCreateDataset(s),
        CreateContainerStatement s => FormatCreateContainer(s),
        CreateNavigationStatement s => FormatCreateNavigation(s),
        CreateButtonStatement s => FormatCreateButton(s),
        CreateBookmarkStatement s => FormatCreateBookmark(s),
        CreateStyleStatement s => FormatCreateStyle(s),
        CreateTagStatement s => FormatCreateTag(s),
        DeleteTagStatement s => FormatDeleteTag(s),
        CreateLineageStatement s => FormatCreateLineageImport(s),
        DeleteLineageStatement s => $"DELETE LINEAGE FOR TABLE {FormatMetadataNameExpression(s.TableName)};",
        LineageStatement s => FormatLineage(s),
        TransformStatement s => FormatTransform(s),
        LintStatement s => s.ScriptPath != null ? $"LINT '{s.ScriptPath}';" : "LINT;",
        GoStatement s => "GO" + (s.Count > 1 ? $" {s.Count}" : "") + ";",
        GenerateJwtSecretStatement _ => "GENERATE JWT_SECRET;",
        RequireVersionStatement s => $"REQUIRE VERSION {s.Operator} '{s.Version}';",
        GenerateStatement s => $"GENERATE {s.RowCount.ToSql()} ROWS INTO {s.Target.ToSql()} ({string.Join(", ", s.Rules.Select(r => $"{r.ColumnName} = {r.Rule}"))})" + (s.Options != null && s.Options.Count > 0 ? " WITH (" + string.Join(", ", s.Options.Select(kv => $"{kv.Key} = {kv.Value.ToSql()}")) + ")" : "") + ";",
        GenerateCalendarStatement s => $"GENERATE CALENDAR FROM {s.StartDate.ToSql()} TO {s.EndDate.ToSql()} INTO {s.Target.ToSql()};",
        CompareDatasetsStatement s => $"COMPARE DATASETS {s.SourceTable.ToSql()} WITH {s.BaselineTable.ToSql()} KEY ({string.Join(", ", s.KeyColumns)})" + (s.ExcludeColumns != null && s.ExcludeColumns.Count > 0 ? $" EXCLUDE ({string.Join(", ", s.ExcludeColumns)})" : "") + $" INTO {s.TargetTable.ToSql()};",
        HelpStatement s => $"HELP {(s.Topic != null ? s.Topic + (s.SubTopic != null ? " " + s.SubTopic : "") : "")}",
        DropReportObjectStatement s => $"DROP {s.ObjectType.ToString().ToUpper()} {(s.IfExists ? "IF EXISTS " : "")}{s.Name};",
        AlterReportObjectStatement s => $"ALTER {s.ObjectType.ToString().ToUpper()} {s.Name} ...", // Summarized
        CreateTemplateStatement s => FormatCreateTemplate(s),
        CreateThemeStatement s => FormatCreateTheme(s),
        SetTemplatePathStatement s => s.ToSql(),
        AssertStatement s => $"ASSERT {s.Condition.ToSql()}{(s.Message != null ? $", {s.Message.ToSql()}" : "")};",
        AssertTableStatement s => FormatAssertTable(s),
        AssertJobStatement s => $"ASSERT JOB {s.JobName} ({string.Join(", ", s.Predicates.Select(p => p.Describe()))})"
            + FormatOnFailureClauses(s.OnFailureActions) + ";",
        SetReportMetadataStatement s => $"SET REPORT {s.Key} = '{s.Value.Replace("'", "''")}';",
        UseDatasetStatement s => $"USE DATASET {s.DatasetName};",
        ShowDatasetsStatement s => "SHOW DATASETS" + (s.IntoTable != null ? $" INTO {s.IntoTable};" : ";"),
        RefreshDatasetStatement s => $"REFRESH DATASET {s.DatasetName};",
        ExportDatasetStatement s => $"EXPORT DATASET {s.DatasetName} TO '{s.TargetPath}';",
        PublishDatasetStatement s => $"PUBLISH DATASET {s.DatasetName} FROM '{s.SourcePath}'" + (s.TargetFolder != null ? $" INTO '{s.TargetFolder}'" : "") + ";",

        // ── SETS ──
        CreateSetsStatement s => FormatCreateSets(s),
        DropSetsStatement s => s.IfExists ? $"DROP SETS IF EXISTS !{s.Name};" : $"DROP SETS !{s.Name};",
        UseSetsStatement s => $"USE SETS !{s.Name};",

        // ── Security ──
        UsePasswordStatement s => $"USE PASSWORD = '********';",
        SetShowPasswordStatement s => $"SET SHOW_SECRETS {(s.Enabled ? "ON" : "OFF")};",
        SetAllowPlaintextSecretsStatement s => $"SET ALLOW_PLAINTEXT_SECRETS {(s.Enabled ? "ON" : "OFF")};",
        SetNoSaveSensitiveStatement s => $"SET NO_SAVE_SENSITIVE {(s.Enabled ? "ON" : "OFF")};",
        SetNoSaveConnectionStatement s => $"SET NO_SAVE_CONNECTION {(s.Enabled ? "ON" : "OFF")};",
        SetConnectionEncryptionStatement s => $"SET CONNECTION_ENCRYPTION {(s.Enabled ? "ON" : "OFF")};",
        SetWeekStartDayStatement s => $"SET WEEK_START_DAY = '{s.DayName}';",
        SetScriptHashPolicyStatement s => $"SET SCRIPT_HASH_POLICY = '{s.Policy}';",
        SetPersistStatement s => $"SET PERSIST = {(s.Enabled ? "ON" : "OFF")};",
        SetSpillOptionStatement s => s.ToSql(),
        SetSecurityOverrideStatement s => FormatSecurityOverride(s),

        // ── Profiling / what-if ──
        SetProfilingStatement s => $"SET PROFILING {(s.Enabled ? "ON" : "OFF")}",
        SetWhatIfStatement s => $"SET WHAT_IF {(s.Enabled ? "ON" : "OFF")}",
        SetThresholdStatement s => FormatSetThreshold(s),
        ShowProfileStatement s => "SHOW PROFILE" + (s.IntoTable != null ? $" INTO {s.IntoTable}" : ""),
        ShowVersionStatement s => "SHOW VERSION" + (s.IntoTable != null ? $" INTO {s.IntoTable};" : ";"),

        // ── Expressions — more-derived before less-derived ──
        SubstringExpression e => $"SUBSTRING({e.String.ToSql()} FROM {e.Start.ToSql()}{(e.Length != null ? $" FOR {e.Length.ToSql()}" : "")})",
        PositionExpression e => $"POSITION({e.Substring.ToSql()} IN {e.String.ToSql()})",
        ExtractExpression e => $"EXTRACT({e.Field} FROM {e.Source.ToSql()})",
        OverlayExpression e => $"OVERLAY({e.String.ToSql()} PLACING {e.Overlay.ToSql()} FROM {e.Start.ToSql()}{(e.Length != null ? $" FOR {e.Length.ToSql()}" : "")})",
        TrimExpression e => FormatTrim(e),
        FunctionCallExpression e => FormatFunctionCall(e),
        UnaryExpression e => FormatUnary(e),
        BinaryExpression e => FormatBinary(e),
        LiteralExpression e => FormatLiteral(e),
        IdentifierExpression e => e.Name,
        StarExpression e => FormatStar(e),
        MemberAccessExpression e => $"{e.Expression.ToSql()}.{e.MemberName}",
        SubqueryExpression e => $"({e.Query.ToSql().TrimEnd(';')})",
        VariableExpression e => e.Name,
        ListExpression e => "(" + string.Join(", ", e.Items.Select(i => i.ToSql())) + ")",
        IsNullExpression e => $"{e.Expression.ToSql()} IS {(e.Not ? "NOT " : "")}NULL",
        IsDistinctFromExpression e => $"{e.Left.ToSql()} IS {(e.Not ? "NOT " : "")}DISTINCT FROM {e.Right.ToSql()}",
        InExpression e => $"{e.Left.ToSql()} {(e.IsNot ? "NOT " : "")}IN {e.Right.ToSql()}",
        BetweenExpression e => FormatBetween(e),
        LikeExpression e => FormatLike(e),
        ExistsExpression e => $"{(e.IsNot ? "NOT " : "")}EXISTS ({e.Subquery.ToSql()})",
        CaseExpression e => FormatCase(e),
        AtTimeZoneExpression e => $"{e.Left.ToSql()} AT TIME ZONE {e.TimeZone.ToSql()}",

        // ── AstNode helpers ──
        SelectColumn n => (n.Alias != null ? $"{n.Expression.ToSql()} AS {n.Alias}" : n.Expression.ToSql())
            + FormatExpectClauses(n.Expectations),
        TableReference n => FormatTableReference(n),
        PivotClause n => $"PIVOT ({n.AggregateFunction}({n.AggregateColumn}) FOR {n.PivotColumn} IN ({string.Join(", ", n.PivotValues.Select(v => v.ToSql()))}))" + (n.Alias != null ? $" AS {n.Alias}" : ""),
        UnpivotClause n => n.AllColumnsExcept
            ? $"UNPIVOT ({n.ValueColumn} FOR {n.NameColumn} IN (COLUMNS(* EXCLUDE ({string.Join(", ", n.ExcludeColumns ?? new List<string>())}))))" + (n.Alias != null ? $" AS {n.Alias}" : "")
            : $"UNPIVOT ({n.ValueColumn} FOR {n.NameColumn} IN ({string.Join(", ", n.UnpivotColumns)}))" + (n.Alias != null ? $" AS {n.Alias}" : ""),
        DuckPivotClause n => $"PIVOT ON {string.Join(", ", n.OnColumns)}"
            + (n.InValues != null ? $" IN ({string.Join(", ", n.InValues.Select(v => v.ToSql()))})" : "")
            + $" USING {string.Join(", ", n.Aggregates.Select(a => $"{a.Function}({a.Column ?? "*"})" + (a.Alias != null ? $" AS {a.Alias}" : "")))}"
            + (n.GroupByColumns != null ? $" GROUP BY {string.Join(", ", n.GroupByColumns)}" : ""),
        MatchRecognizeClause n => FormatMatchRecognize(n),
        OutputClause n => $"OUTPUT {string.Join(", ", n.Columns.Select(c => c.ToSql()))}{(n.IntoTable != null ? $" INTO {n.IntoTable.ToSql()}" : "")}",
        ForClause n => FormatForClause(n),
        ForeignKeyReference n => $"REFERENCES {n.Table.ToSql()}({string.Join(", ", n.Columns)})",
        TablePrimaryKeyConstraint n => $"{(n.ConstraintName != null ? $"CONSTRAINT {n.ConstraintName} " : "")}PRIMARY KEY ({string.Join(", ", n.Columns)})",
        TableUniqueConstraint n => $"{(n.ConstraintName != null ? $"CONSTRAINT {n.ConstraintName} " : "")}UNIQUE ({string.Join(", ", n.Columns)})",
        TableForeignKeyConstraint n => $"{(n.ConstraintName != null ? $"CONSTRAINT {n.ConstraintName} " : "")}FOREIGN KEY ({string.Join(", ", n.Columns)}) {n.Reference.ToSql()}",
        TableCheckConstraint n => $"{(n.ConstraintName != null ? $"CONSTRAINT {n.ConstraintName} " : "")}CHECK ({n.Expression.ToSql()})",
        ColumnDefinition n => FormatColumnDefinition(n),
        OrderByClause n => n.Expression.ToSql() + (n.Descending ? " DESC" : " ASC"),
        JoinClause n => FormatJoin(n),
        GroupingSetClause n => FormatGroupingSet(n),
        Assignment n => $"{n.ColumnName} = {n.Value.ToSql()}",
        ParameterDefinition n => $"{n.Name} {n.DataType}",
        ElseIfClause n => $"ELSE IF {n.Condition.ToSql()} BEGIN ... END",
        WindowFrame n => FormatWindowFrame(n),
        WindowClause n => FormatWindowClause(n),
        ScheduleInfo n => $"EVERY {n.Interval} {n.Unit}{(n.AtTime != null ? $" AT '{n.AtTime}'" : "")}",
        MergeUpdateClause n => $"THEN UPDATE SET {string.Join(", ", n.Assignments.Select(a => a.ToSql()))}",
        MergeDeleteClause _ => "THEN DELETE",
        MergeInsertClause n => FormatMergeInsert(n),
        MergeActionClause n => FormatMergeAction(n),
        SetParameterAction n => $"SET_PARAMETER({n.ParameterName}, {n.ValueExpression})",
        DrillDownAction n => $"DRILL_DOWN(Target = {n.TargetVisual}, Key = ({string.Join(", ", n.KeyColumns)}))",
        DrillInAction n => $"DRILL_IN(HIERARCHY = ({string.Join(", ", n.Hierarchy)}))",
        RunScriptAction n => $"RUN_SCRIPT('{n.ScriptPath}'{FormatActionParameters(n.Parameters)})",
        ClearFiltersAction _ => "CLEAR_FILTERS",
        ApplyParametersAction _ => "APPLY_PARAMETERS",
        ReportCommandAction n => n.Command == "REFRESH" ? "REFRESH_REPORT" : n.Command,
        DrillReportAction n => $"DRILL_REPORT('{n.TargetReport}'{FormatActionParameters(n.Parameters)})",
        NavigatePageAction n => $"NAVIGATE_PAGE({n.TargetPage})",
        RefreshVisualsAction n => $"REFRESH_VISUALS({string.Join(", ", n.Targets)})",
        SetUiStateAction n => $"SET_UI_STATE({FormatActionTargets(n.Targets)}, {n.Key}, {n.Value})",
        ApplyBookmarkAction n => $"APPLY_BOOKMARK({n.BookmarkName})",
        VisualMapping m => FormatMapping(m),

        _ => node is Statement ? "UNKNOWN STATEMENT" : node.GetType().Name
    };

    // ── Public overloads for non-AstNode types ──────────────────────────

    public static string Format(JoinClause join) => FormatJoin(join);
    public static string Format(OrderByClause o) => o.Expression.ToSql() + (o.Descending ? " DESC" : " ASC");
    public static string Format(GroupingSetClause g) => FormatGroupingSet(g);
    public static string Format(ColumnDefinition col) => FormatColumnDefinition(col);
    public static string Format(ExecuteParameter p) => p.Expression.ToSql() + (p.IsOutput ? " OUTPUT" : "") + (p.IsInput ? " INPUT" : "");

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
            var percent = s.IsTopPercent ? " PERCENT" : "";
            var ties = s.WithTies ? " WITH TIES" : "";
            top = $"TOP ({s.TopCount.ToSql()}){percent}{ties} ";
        }
        var cols = string.Join(", ", s.Columns.Select(c => c.ToSql()));
        var into = s.IntoTable != null ? $" INTO {s.IntoTable.ToSql()}" : "";
        var from = $" FROM {s.FromTable.ToSql()}";
        var joins = s.Joins.Count > 0 ? " " + string.Join(" ", s.Joins.Select(j => j.ToSql())) : "";
        var where = s.WhereClause != null ? $" WHERE {s.WhereClause.ToSql()}" : "";
        var group = s.GroupingSet != null ? $" GROUP BY {s.GroupingSet.ToSql()}"
                     : s.GroupByAll ? " GROUP BY ALL"
                     : s.GroupBy != null && s.GroupBy.Count > 0
                           ? $" GROUP BY {string.Join(", ", s.GroupBy.Select(g => g.ToSql()))}"
                           : "";
        var having = s.HavingClause != null ? $" HAVING {s.HavingClause.ToSql()}" : "";
        var window = s.WindowDefinitions != null && s.WindowDefinitions.Count > 0
            ? $" WINDOW {string.Join(", ", s.WindowDefinitions.Select(w => $"{w.Name} AS ({w.Clause.ToSql()})"))}"
            : "";
        var qualify = s.QualifyClause != null ? $" QUALIFY {s.QualifyClause.ToSql()}" : "";
        var order = s.OrderByAll ? $" ORDER BY ALL{(s.OrderByAllDescending ? " DESC" : "")}"
                     : s.OrderBy != null && s.OrderBy.Count > 0
                           ? $" ORDER BY {string.Join(", ", s.OrderBy.Select(o => o.ToSql()))}" : "";
        var limit = s.LimitCount != null ? $" LIMIT {s.LimitCount.ToSql()}" : "";
        var offset = s.Offset != null ? $" OFFSET {s.Offset.ToSql()} ROWS" : "";
        var forCl = s.ForClause != null ? $" {s.ForClause.ToSql()}" : "";
        var sample = s.Sample != null
            ? $" USING SAMPLE {s.Sample.Count}{(s.Sample.IsPercent ? " PERCENT" : " ROWS")}" + (s.Sample.Seed.HasValue ? $" REPEATABLE ({s.Sample.Seed})" : "")
            : "";
        var onFailure = FormatOnFailureClauses(s.OnFailureActions);
        return $"{with}SELECT {distinct}{top}{cols}{into}{from}{joins}{where}{group}{having}{window}{qualify}{order}{limit}{offset}{sample}{forCl}{onFailure};";
    }

    /// <summary>
    /// Re-emits the trailing <c>ON FAILURE</c> blocks — row routing on a SELECT, declared actions on
    /// an <c>ASSERT JOB</c>. Dropping them would produce a script whose columns elect an action with
    /// nowhere to route, or a job assertion that quietly stopped failing the run.
    /// </summary>
    private static string FormatOnFailureClauses(IReadOnlyList<FailureActionClause>? clauses)
    {
        if (clauses is not { Count: > 0 }) return "";

        var parts = new List<string>();
        foreach (var clause in clauses)
        {
            var text = $" ON FAILURE {clause.Action.ToString().ToUpperInvariant()}";
            // NOTIFY names its notification directly; TO introduces a row-routing target, which a
            // job-level action never has.
            if (clause.Target != null)
                text += clause.Action == FailAction.Notify ? $" {clause.Target}" : $" TO {clause.Target}";

            var options = new List<string>();
            if (clause.Retention != null) options.Add($"RETENTION = '{clause.Retention}'");
            if (clause.Handling != QuarantineHandling.Steward)
                options.Add($"HANDLING = {clause.Handling.ToString().ToUpperInvariant()}");
            if (options.Count > 0) text += $" WITH ({string.Join(", ", options)})";

            parts.Add(text);
        }
        return string.Concat(parts);
    }

    /// <summary>
    /// Re-emits a column's <c>EXPECT &lt;rule&gt; [ON FAILURE &lt;action&gt;]</c> clauses. Rules are
    /// grammar now, so the formatter owns them: a serializer that dropped them would silently
    /// disable enforcement in every tool-formatted script — the failure mode moving rules out of
    /// comments was meant to end. The rule text is re-emitted as written.
    /// </summary>
    private static string FormatExpectClauses(IReadOnlyList<ColumnExpectClause>? clauses)
    {
        if (clauses is not { Count: > 0 }) return "";

        var parts = new List<string>();
        foreach (var clause in clauses)
        {
            var text = $" EXPECT {clause.Text}";
            // An omitted action means WARN. Re-emitting it would be harmless but noisy, and it
            // would make a formatter pass rewrite scripts it had no reason to touch.
            if (clause.ActionExplicit)
                text += $" ON FAILURE {clause.Action.ToString().ToUpperInvariant()}";
            parts.Add(text);
        }
        return string.Concat(parts);
    }

    private static string FormatSetOperation(SetOperationStatement s)
    {
        string op = s.Operation switch
        {
            SetOpType.UNION => "UNION",
            SetOpType.UNION_ALL => "UNION ALL",
            SetOpType.EXCEPT => "EXCEPT",
            SetOpType.INTERSECT => "INTERSECT",
            _ => "UNION"
        };
        if (s.ByName) op += " BY NAME";
        return $"({s.Left.ToSql().TrimEnd(';')}) {op} ({s.Right.ToSql().TrimEnd(';')});";
    }

    private static string FormatInsert(InsertStatement s)
    {
        var with = (s.Ctes != null && s.Ctes.Count > 0) ? "WITH " + string.Join(", ", s.Ctes.Select(c => $"{c.Name} AS ({c.Query.ToSql().TrimEnd(';')})")) + " " : "";
        var cols = s.Columns != null && s.Columns.Count > 0 ? "(" + string.Join(", ", s.Columns) + ") " : "";
        var output = s.Output != null ? " " + s.Output.ToSql() : "";
        if (s.SelectQuery != null)
            return $"{with}INSERT INTO {s.TargetTable.ToSql()} {cols}{output}{s.SelectQuery.ToSql()}";
        var vals = s.Values != null ? string.Join(", ", s.Values.Select(row => "(" + string.Join(", ", row.Select(v => v.ToSql())) + ")")) : "";
        return $"{with}INSERT INTO {s.TargetTable.ToSql()} {cols}{output}VALUES {vals};";
    }

    private static string FormatUpdate(UpdateStatement s)
    {
        var with = (s.Ctes != null && s.Ctes.Count > 0) ? "WITH " + string.Join(", ", s.Ctes.Select(c => $"{c.Name} AS ({c.Query.ToSql().TrimEnd(';')})")) + " " : "";
        var sets = string.Join(", ", s.Assignments.Select(a => a.ToSql()));
        var from = s.FromTable != null ? $" FROM {s.FromTable.ToSql()}" : "";
        var joins = s.Joins != null && s.Joins.Count > 0 ? " " + string.Join(" ", s.Joins.Select(j => j.ToSql())) : "";
        var out_ = s.Output != null ? " " + s.Output.ToSql() : "";
        var where = s.WhereClause != null ? $" WHERE {s.WhereClause.ToSql()}" : "";
        return $"{with}UPDATE {s.TargetTable.ToSql()} SET {sets}{from}{joins}{out_}{where};";
    }

    private static string FormatDelete(DeleteStatement s)
    {
        var with = (s.Ctes != null && s.Ctes.Count > 0) ? "WITH " + string.Join(", ", s.Ctes.Select(c => $"{c.Name} AS ({c.Query.ToSql().TrimEnd(';')})")) + " " : "";
        var out_ = s.Output != null ? " " + s.Output.ToSql() : "";
        var where = s.WhereClause != null ? $" WHERE {s.WhereClause.ToSql()}" : "";
        return $"{with}DELETE FROM {s.TargetTable.ToSql()}{out_}{where};";
    }

    private static string FormatMerge(MergeStatement s)
    {
        var with = (s.Ctes != null && s.Ctes.Count > 0) ? "WITH " + string.Join(", ", s.Ctes.Select(c => $"{c.Name} AS ({c.Query.ToSql().TrimEnd(';')})")) + " " : "";
        var sb = new System.Text.StringBuilder();
        sb.Append(with);
        sb.AppendLine($"MERGE INTO {s.TargetTable.ToSql()}{(s.TargetAlias != null ? " AS " + s.TargetAlias : "")}");
        sb.AppendLine($"USING {s.SourceTable.ToSql()}{(s.SourceAlias != null ? " AS " + s.SourceAlias : "")}");
        sb.AppendLine($"ON {s.OnCondition.ToSql()}");
        foreach (var c in s.MatchedClauses) sb.AppendLine($"WHEN MATCHED{(c.Condition != null ? " AND " + c.Condition.ToSql() : "")} {c.ToSql()}");
        foreach (var c in s.NotMatchedClauses) sb.AppendLine($"WHEN NOT MATCHED{(c.Condition != null ? " AND " + c.Condition.ToSql() : "")} {c.ToSql()}");
        sb.Append(";");
        return sb.ToString();
    }

    private static string FormatExec(ExecStatement s)
    {
        var sql = $"EXEC ({s.SqlExpression.ToSql()})";
        if (s.ConnectionName != null) sql += $" AT {s.ConnectionName.ToSql()}";
        if (s.IntoTable != null) sql += $" INTO {s.IntoTable.ToSql()}";
        if (s.Parameters.Count > 0) sql += " WITH (" + string.Join(", ", s.Parameters.Select(p => p.ToSql())) + ")";
        return sql + ";";
    }

    private static string FormatExecutePushdown(ExecutePushdownStatement s)
    {
        var into = s.IntoTable != null ? $" INTO {s.IntoTable.ToSql()}" : "";
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
        var modeStr = CreationVerb(s.Mode);
        string body;
        if (s.TargetExpression != null && s.Options != null && s.Options.Count > 0)
            body = "(" + s.TargetExpression.ToSql() + ", " + string.Join(", ", s.Options.Select(o => $"{o.Key}={o.Value.ToSql()}")) + ")";
        else if (s.TargetExpression != null)
            body = $"({s.TargetExpression.ToSql()})";
        else if (s.Options != null && s.Options.Count > 0)
            body = "(" + string.Join(", ", s.Options.Select(o => $"{o.Key}={o.Value.ToSql()}")) + ")";
        else
            body = "()";
        return $"{modeStr} CONNECTION {s.ConnectionName} AS {s.ConnectionType}{body};";
    }

    private static string FormatAlterConnection(AlterConnectionStatement s)
    {
        if (s.ConnectionType != null)
        {
            string body;
            if (s.TargetExpression != null && s.Options != null && s.Options.Count > 0)
                body = "(" + s.TargetExpression.ToSql() + ", " + string.Join(", ", s.Options.Select(o => $"{o.Key}={o.Value.ToSql()}")) + ")";
            else if (s.TargetExpression != null)
                body = $"({s.TargetExpression.ToSql()})";
            else if (s.Options != null && s.Options.Count > 0)
                body = "(" + string.Join(", ", s.Options.Select(o => $"{o.Key}={o.Value.ToSql()}")) + ")";
            else
                body = "()";
            return $"ALTER CONNECTION {s.ConnectionName} AS {s.ConnectionType}{body};";
        }
        var optionsStr = s.Options != null && s.Options.Count > 0
            ? " WITH (" + string.Join(", ", s.Options.Select(o => $"{o.Key}={o.Value.ToSql()}")) + ")"
            : "";
        return $"ALTER CONNECTION {s.ConnectionName}{optionsStr};";
    }

    private static string FormatCreateSshKeyPair(CreateSshKeyPairStatement s)
    {
        var args = new List<string> { s.Path.ToSql() };
        if (s.Bits != null) args.Add($"BITS={s.Bits.ToSql()}");
        if (s.Algorithm != null) args.Add($"ALGORITHM={s.Algorithm.ToSql()}");
        if (s.Passphrase != null) args.Add($"PASSPHRASE={s.Passphrase.ToSql()}");
        if (s.Comment != null) args.Add($"COMMENT={s.Comment.ToSql()}");
        return $"CREATE SSH_KEY_PAIR({string.Join(", ", args)});";
    }

    private static string FormatCreatePgpKeyPair(CreatePgpKeyPairStatement s)
    {
        var args = new List<string> { s.Path.ToSql() };
        if (s.Bits != null) args.Add($"BITS={s.Bits.ToSql()}");
        if (s.Identity != null) args.Add($"IDENTITY={s.Identity.ToSql()}");
        if (s.Passphrase != null) args.Add($"PASSPHRASE={s.Passphrase.ToSql()}");
        return $"CREATE PGP_KEY_PAIR({string.Join(", ", args)});";
    }

    private static string FormatCreateTable(CreateTableStatement s)
    {
        var ifNot = s.IfNotExists ? "IF NOT EXISTS " : "";
        var orReplace = s.OrReplace ? "OR REPLACE " : "";
        var items = new List<string>(s.Columns.Select(c => c.ToSql()));
        items.AddRange(s.TableConstraints.Select(tc => tc.ToSql()));
        return $"CREATE {orReplace}TABLE {ifNot}{s.TargetTable.ToSql()} ({string.Join(", ", items)});";
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
        var modeStr = CreationVerb(s.Mode);
        var paramsStr = string.Join(", ", s.Parameters.Select(p => p.ToSql()));
        return $"{modeStr} PROCEDURE {s.ProcedureName} ({paramsStr}) AS BEGIN ... END;";
    }

    private static string FormatCreateFunction(CreateFunctionStatement s)
    {
        var modeStr = CreationVerb(s.Mode);
        var paramsStr = string.Join(", ", s.Parameters.Select(p => p.ToSql()));
        return $"{modeStr} FUNCTION {s.FunctionName} ({paramsStr}) RETURNS {s.ReturnType} AS BEGIN ... END;";
    }

    private static string FormatCreateView(CreateViewStatement s)
    {
        var modeStr = CreationVerb(s.Mode);
        return $"{modeStr} VIEW {s.ViewName} AS {s.Query.ToSql()}";
    }

    private static string FormatDeclare(DeclareStatement s)
    {
        var init = s.InitialValue != null ? $" = {s.InitialValue.ToSql()}" : "";
        var pass = s.IsSensitive ? " PASSWORD" : "";
        var input = s.IsInput ? " INPUT" : "";
        var output = s.IsOutput ? " OUTPUT" : "";
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
        var ts = s.ShowTimestamp != null ? $", {s.ShowTimestamp.ToSql()}" : "";
        var fmt = s.TimestampFormat != null ? $", {s.TimestampFormat.ToSql()}" : "";
        return $"PRINT({msg}{ts}{fmt});";
    }

    private static string FormatRaiseError(RaiseErrorStatement s)
    {
        var loc = s.CodeLocation != null ? $", {s.CodeLocation.ToSql()}" : "";
        var paramsStr = s.Parameters.Count > 0 ? ", " + string.Join(", ", s.Parameters.Select(p => p.ToSql())) : "";
        return $"RAISEERROR({s.Message.ToSql()}, {s.Severity.ToSql()}{loc}{paramsStr});";
    }

    private static string FormatBulkInsert(BulkInsertStatement s)
    {
        var cols = s.Columns != null && s.Columns.Count > 0 ? "(" + string.Join(", ", s.Columns) + ") " : "";
        var optionsStr = s.Options.Count > 0
            ? $" WITH ({string.Join(", ", s.Options.Select(o => $"{o.Key} = {o.Value.ToSql()}"))})"
            : "";
        return $"BULK INSERT {s.TargetTable.ToSql()} {cols}FROM '{s.FilePath}'{optionsStr};";
    }

    private static string FormatExport(ExportStatement s)
        => $"EXPORT {s.Source.ToSql()} TO '{s.TargetPath}'" + (s.Options != null ? " WITH (...)" : "");

    private static string FormatExportReport(ExportReportStatement s)
    {
        var sql = $"EXPORT REPORT {s.ReportPath.ToSql()} FORMAT {s.Format.ToUpperInvariant()} TO {s.OutputPath.ToSql()}";
        var options = new List<string>();
        if (!string.IsNullOrWhiteSpace(s.PdfMode))
            options.Add($"PDF_MODE = {s.PdfMode.ToUpperInvariant()}");
        if (s.Host != null)
            options.Add($"HOST = {s.Host.ToSql()}");
        if (s.BrowserPath != null)
            options.Add($"BROWSER_PATH = {s.BrowserPath.ToSql()}");
        if (options.Count > 0)
            sql += $" WITH ({string.Join(", ", options)})";
        return sql + ";";
    }

    private static string FormatEmail(EmailStatement s)
    {
        var sql = $"SEND EMAIL TO {s.To.ToSql()}\nFROM {s.From.ToSql()}\nSUBJECT {s.Subject.ToSql()}\nBODY {s.Body.ToSql()}";
        if (s.Cc != null && s.Cc.Count > 0) sql += "\nCC " + string.Join(", ", s.Cc.Select(e => e.ToSql()));
        if (s.Bcc != null && s.Bcc.Count > 0) sql += "\nBCC " + string.Join(", ", s.Bcc.Select(e => e.ToSql()));
        if (s.Attachments != null && s.Attachments.Count > 0) sql += "\nATTACH " + string.Join(", ", s.Attachments.Select(e => e.ToSql()));
        if (s.ConnectionName != null) sql += $"\nAT {s.ConnectionName.ToSql()}";
        return sql + ";";
    }

    private static string FormatFileOperation(FileOperationStatement s)
    {
        var op = s.Type.ToString().ToUpper() + " FILE";
        var dest = s.Destination != null ? " TO " + (s.DestinationIsDirectory ? "DIRECTORY " : "") + s.Destination.ToSql() : "";
        var opts = new List<string>();
        if (s.Overwrite != null) opts.Add($"OVERWRITE={s.Overwrite.ToSql()}");
        if (s.Password != null) opts.Add($"PASSWORD={s.Password.ToSql()}");
        if (s.KeyFile != null) opts.Add($"KEYFILE={s.KeyFile.ToSql()}");
        if (s.PgpKey != null) opts.Add($"PGP_KEY={s.PgpKey.ToSql()}");
        if (s.DateSuffix != null) opts.Add($"DATE_SUFFIX={s.DateSuffix.ToSql()}");
        if (s.SuffixSeparator != null) opts.Add($"SUFFIX_SEPARATOR={s.SuffixSeparator.ToSql()}");
        var options = opts.Count > 0 ? " WITH(" + string.Join(", ", opts) + ")" : "";
        return $"{op} {s.Source.ToSql()}{dest}{options};";
    }

    private static string FormatDirectoryOperation(DirectoryOperationStatement s)
    {
        var op = s.Type == DirectoryOpType.DeleteContents
            ? "DELETE DIRECTORY_CONTENTS"
            : s.Type.ToString().ToUpper() + " DIRECTORY";
        var extra = s.Destination != null ? " TO " + s.Destination.ToSql() : "";
        var opts = new List<string>();
        if (s.Overwrite != null) opts.Add($"OVERWRITE={s.Overwrite.ToSql()}");
        if (s.Recursive != null) opts.Add($"RECURSIVE={s.Recursive.ToSql()}");
        if (s.Password != null) opts.Add($"PASSWORD={s.Password.ToSql()}");
        if (s.KeyFile != null) opts.Add($"KEYFILE={s.KeyFile.ToSql()}");
        if (s.PgpKey != null) opts.Add($"PGP_KEY={s.PgpKey.ToSql()}");
        var with = opts.Count > 0 ? " WITH(" + string.Join(", ", opts) + ")" : "";
        return $"{op} {s.Path.ToSql()}{extra}{with};";
    }

    private static string FormatFileTransfer(FileTransferStatement s)
    {
        var op = s.Type == FileTransferType.Send ? "SEND FILE" : "RECEIVE FILE";
        var fromTo = s.Type == FileTransferType.Send
            ? $"{s.LocalPath.ToSql()} TO {s.RemotePath.ToSql()}"
            : $"FROM {s.RemotePath.ToSql()} TO {s.LocalPath.ToSql()}";
        var opts = s.Overwrite != null ? $" WITH(OVERWRITE={s.Overwrite.ToSql()})" : "";
        return $"{op} {fromTo} AT {s.ConnectionName}{opts};";
    }

    /// <summary>Single-quoted SQL string literal, doubling any embedded quote.</summary>
    private static string Quote(string value) => "'" + value.Replace("'", "''") + "'";

    /// <summary>
    /// The lifecycle verb for a mode-aware object. Catalog objects give CREATE OR REPLACE
    /// full-redefinition semantics; non-catalog objects use the same AST mode so formatters must
    /// still preserve the caller's requested verb.
    /// </summary>
    private static string CreationVerb(ObjectCreationMode mode) => mode switch
    {
        ObjectCreationMode.Alter => "ALTER",
        ObjectCreationMode.CreateOrAlter => "CREATE OR ALTER",
        ObjectCreationMode.CreateOrReplace => "CREATE OR REPLACE",
        _ => "CREATE"
    };

    private static string FormatCatalogMetadata(CatalogObjectOptions metadata)
    {
        var parts = new List<string>();
        if (metadata.DisplayName != null) parts.Add($"DISPLAY_NAME = {Quote(metadata.DisplayName)}");
        if (metadata.Description != null) parts.Add($"DESCRIPTION = {Quote(metadata.Description)}");
        if (metadata.Options != null)
            foreach (var (key, value) in metadata.Options.OrderBy(o => o.Key, StringComparer.OrdinalIgnoreCase))
                parts.Add($"{key.ToUpperInvariant()} = {Quote(value)}");

        return parts.Count == 0 ? "" : $" WITH ({string.Join(", ", parts)})";
    }

    private static string FormatJobOptions(CreateJobStatement statement)
    {
        var parts = new List<string>();
        if (statement.MaxRetries.HasValue) parts.Add($"MAX_RETRIES = {statement.MaxRetries.Value}");
        if (statement.RetryDelaySeconds.HasValue) parts.Add($"RETRY_DELAY = {statement.RetryDelaySeconds.Value}");
        if (statement.Metadata.DisplayName != null)
            parts.Add($"DISPLAY_NAME = {Quote(statement.Metadata.DisplayName)}");
        if (statement.Metadata.Description != null)
            parts.Add($"DESCRIPTION = {Quote(statement.Metadata.Description)}");
        if (statement.Metadata.Options != null)
            foreach (var (key, value) in statement.Metadata.Options.OrderBy(o => o.Key, StringComparer.OrdinalIgnoreCase))
                parts.Add($"{key.ToUpperInvariant()} = {Quote(value)}");
        return parts.Count == 0 ? "" : $" WITH ({string.Join(", ", parts)})";
    }

    private static string FormatAlterJob(AlterJobStatement statement)
    {
        if (statement.TargetPath != null)
            return $"ALTER JOB {statement.JobName} SET TARGET = {Quote(statement.TargetPath)};";

        var parts = new List<string>();
        if (statement.MaxRetries.HasValue) parts.Add($"MAX_RETRIES = {statement.MaxRetries.Value}");
        if (statement.RetryDelaySeconds.HasValue) parts.Add($"RETRY_DELAY = {statement.RetryDelaySeconds.Value}");
        if (statement.Metadata.DisplayName != null)
            parts.Add($"DISPLAY_NAME = {Quote(statement.Metadata.DisplayName)}");
        if (statement.Metadata.Description != null)
            parts.Add($"DESCRIPTION = {Quote(statement.Metadata.Description)}");
        if (statement.Metadata.Options != null)
            foreach (var (key, value) in statement.Metadata.Options.OrderBy(o => o.Key, StringComparer.OrdinalIgnoreCase))
                parts.Add($"{key.ToUpperInvariant()} = {Quote(value)}");
        return $"ALTER JOB {statement.JobName} SET ({string.Join(", ", parts)});";
    }

    private static string FormatAlterCatalogObject(AlterCatalogObjectStatement s)
    {
        var clauses = new List<string>();
        if (s.Cron != null) clauses.Add($"SET CRON = {Quote(s.Cron)}");
        if (s.TimeZone != null) clauses.Add($"SET TIME ZONE {Quote(s.TimeZone)}");
        if (s.ConnectionName != null) clauses.Add($"SET USING {s.ConnectionName}");
        if (s.Recipient != null) clauses.Add($"SET TO {Quote(s.Recipient)}");

        var metadata = FormatCatalogMetadata(s.Metadata);
        if (metadata.Length > 0) clauses.Add("SET " + metadata.Substring(" WITH ".Length));

        return $"ALTER {s.Kind.ToString().ToUpperInvariant()} {s.Name} {string.Join(" ", clauses)};";
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
        if (s.ExportAsOpenLineage)
        {
            var target = s.TargetTable != null ? $" FOR {FormatLineageTarget(s.TargetTable)}" : "";
            var column = s.ColumnName != null ? $" COLUMN {s.ColumnName}" : "";
            return $"EXPORT LINEAGE{target}{column} AS OPENLINEAGE TO '{s.ExportPath}';";
        }
        var sql = s.TargetTable != null ? $"SHOW LINEAGE FOR {FormatLineageTarget(s.TargetTable)}" : "SHOW LINEAGE";
        if (s.ColumnName != null) sql += $" COLUMN {s.ColumnName}";
        if (s.ExportPath != null) sql += $" TO '{s.ExportPath}'";
        if (s.IntoTable != null) sql += $" INTO {s.IntoTable}";
        return sql + ";";
    }

    private static string FormatCreateTag(CreateTagStatement s)
    {
        var column = s.ColumnName != null ? $" COLUMN {FormatMetadataNameExpression(s.ColumnName)}" : "";
        var tags = string.Join(", ", s.Tags.Select(t => $"{t.Key} = {t.Value.ToSql()}"));
        return $"INSERT TAG FOR TABLE {FormatMetadataNameExpression(s.TableName)}{column} ({tags});";
    }

    private static string FormatDeleteTag(DeleteTagStatement s)
    {
        var column = s.ColumnName != null ? $" COLUMN {FormatMetadataNameExpression(s.ColumnName)}" : "";
        return $"DELETE TAG FOR TABLE {FormatMetadataNameExpression(s.TableName)}{column} ({string.Join(", ", s.TagNames)});";
    }

    private static string FormatCreateLineageImport(CreateLineageStatement s) =>
        $"INSERT LINEAGE FOR TABLE {FormatMetadataNameExpression(s.TableName)} FROM {s.Source.ToSql()};";

    private static string FormatMetadataNameExpression(Expression expression) =>
        expression is LiteralExpression { Type: TokenType.STRING_LITERAL, Value: string value }
            ? value
            : expression.ToSql();

    private static string FormatTransform(TransformStatement s)
    {
        var sourceStr = s.SourceTable != null ? $" FROM {s.SourceTable.ToSql()}" : "";
        var optionsStr = s.Options != null && s.Options.Count > 0
            ? " (" + string.Join(", ", s.Options.Select(o => $"{o.Key} = {o.Value.ToSql()}")) + ")"
            : "()";
        return $"TRANSFORM {s.TargetTable.ToSql()}{sourceStr} USING {s.Algorithm}{optionsStr};";
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
        var prompt = s.WithPrompt ? "\n    SET WITH_PROMPT ON;" : "";
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

    private static string FormatCreateBinding(CreateBindingStatement s)
    {
        var sb = new StringBuilder();
        sb.Append(s.Mode == ObjectCreationMode.CreateOrAlter ? "CREATE OR ALTER BINDING " :
                 s.Mode == ObjectCreationMode.CreateOrReplace ? "CREATE OR REPLACE BINDING " :
                 s.Mode == ObjectCreationMode.Alter ? "ALTER BINDING " : "CREATE BINDING ");
        sb.Append(s.Name);
        sb.Append(" AS ").Append(s.Type.ToUpperInvariant());
        if (s.Options != null && s.Options.Count > 0)
        {
            sb.Append(" (\n    ");
            var opts = s.Options.Select(kv => $"{kv.Key.ToUpperInvariant()} = {Format(kv.Value)}");
            sb.Append(string.Join(",\n    ", opts));
            sb.Append("\n)");
        }
        sb.Append(';');
        return sb.ToString();
    }

    private static string FormatGrantBinding(GrantBindingStatement s) =>
        $"GRANT {s.Permission.ToUpperInvariant()} ON BINDING {s.BindingName} TO {s.PrincipalKind.ToUpperInvariant()} '{s.PrincipalName.Replace("'", "''")}';";

    private static string FormatRevokeBinding(RevokeBindingStatement s) =>
        $"REVOKE {s.Permission.ToUpperInvariant()} ON BINDING {s.BindingName} FROM {s.PrincipalKind.ToUpperInvariant()} '{s.PrincipalName.Replace("'", "''")}';";

    private static string FormatMergeInsert(MergeInsertClause n)
    {
        var cols = n.Columns != null && n.Columns.Count > 0 ? "(" + string.Join(", ", n.Columns) + ") " : "";
        var vals = string.Join(", ", n.Values.Select(v => v.ToSql()));
        return $"THEN INSERT {cols}VALUES ({vals})";
    }



    private static string FormatExecuteTool(ExecuteToolStatement s)
    {
        var sb = new System.Text.StringBuilder($"EXECUTE TOOL '{s.ToolAlias}'");
        if (s.SourceTable != null)
        {
            sb.Append($" FROM {FormatTableReference(s.SourceTable)}");
        }
        if (s.TargetTable != null)
        {
            sb.Append($" INTO {FormatTableReference(s.TargetTable)}");
        }
        if (s.Parameters?.Count > 0)
        {
            sb.Append(" WITH (");
            sb.Append(string.Join(", ", s.Parameters.Select(kv => $"{kv.Key} = {Format(kv.Value)}")));
            sb.Append(")");
        }
        if (s.ExpectedSchema?.Count > 0)
        {
            sb.Append(" EXPECT SCHEMA (");
            sb.Append(string.Join(", ", s.ExpectedSchema.Select(c => $"{c.ColumnName} {c.DataType}{(c.NotNull ? " NOT NULL" : "")}")));
            sb.Append(")");
        }
        return sb.ToString();
    }

    // ── Expression formatters ────────────────────────────────────────────

    private static string FormatUnary(UnaryExpression e)
    {
        string op = e.Operator switch
        {
            TokenType.NOT => "NOT ",
            TokenType.MINUS => "-",
            TokenType.PLUS => "+",
            _ => e.Operator.ToString()
        };
        return $"{op}{e.Expression.ToSql()}";
    }

    private static string BinaryOperatorText(TokenType op) => op switch
    {
        TokenType.PLUS => "+",
        TokenType.MINUS => "-",
        TokenType.STAR => "*",
        TokenType.SLASH => "/",
        TokenType.MODULO => "%",
        TokenType.CONCAT => "||",
        TokenType.EQUALS => "=",
        TokenType.NOT_EQUALS => "<>",
        TokenType.LESS_THAN => "<",
        TokenType.LESS_EQUALS => "<=",
        TokenType.GREATER_THAN => ">",
        TokenType.GREATER_EQUALS => ">=",
        TokenType.REGEX_MATCH => "~",
        TokenType.REGEX_IMATCH => "~*",
        TokenType.AND => "AND",
        TokenType.OR => "OR",
        _ => op.ToString()
    };

    /// <summary>
    /// Serializes a binary expression without recursing through its operands.
    ///
    /// <para>The obvious implementation — <c>$"({e.Left.ToSql()} {op} {e.Right.ToSql()})"</c> — costs
    /// three stack frames per term, so a <c>WHERE</c> with roughly fifty conjuncts exhausted the
    /// stack and killed the process. That is reachable: a fifty-way join writes forty-nine
    /// predicates, and generated ETL predicates are routinely longer. It was also unsurvivable,
    /// because every top-level script is serialized in full to hash it for the execution-policy
    /// snapshot (Evaluator.cs) before a single statement runs, and a stack overflow cannot be
    /// caught — the process died with no message and nothing executed.</para>
    ///
    /// <para>Walking the binary spine on an explicit stack puts chain length on the heap instead.
    /// Operands that are not themselves binary still go through <see cref="Format(AstNode)"/>, so
    /// depth now tracks structural nesting (a function wrapping an expression) rather than the
    /// length of an <c>AND</c> chain. Output is byte-identical to the recursive form: the hash it
    /// feeds must not change.</para>
    /// </summary>
    private static string FormatBinary(BinaryExpression root)
    {
        var output = new Stack<string>();
        var work = new Stack<(AstNode Node, bool Expanded)>();
        work.Push((root, false));

        while (work.Count > 0)
        {
            var (node, expanded) = work.Pop();

            if (node is not BinaryExpression binary)
            {
                output.Push(node.ToSql());
                continue;
            }

            if (!expanded)
            {
                // Left is pushed last so it is popped — and so completes — first.
                work.Push((binary, true));
                work.Push((binary.Right, false));
                work.Push((binary.Left, false));
                continue;
            }

            var right = output.Pop();
            var left = output.Pop();
            output.Push($"({left} {BinaryOperatorText(binary.Operator)} {right})");
        }

        return output.Pop();
    }

    private static string FormatLiteral(LiteralExpression e)
    {
        if (e.Value == null) return "NULL";
        var valStr = e.Value.ToString() ?? "";
        if (e.Type == TokenType.TRUE) return "TRUE";
        if (e.Type == TokenType.FALSE) return "FALSE";
        if (e.Type == TokenType.STRING_LITERAL) return $"'{valStr.Replace("'", "''")}'";
        return valStr;
    }

    private static string FormatFunctionCall(FunctionCallExpression e)
    {
        var distinct = e.IsDistinct ? "DISTINCT " : "";
        var args = string.Join(", ", e.Arguments.Select(a => a.ToSql()));
        var sql = e.JsonTable != null
            ? $"{e.FunctionName}({distinct}{args} COLUMNS ({string.Join(", ", e.JsonTable.Columns.Select(FormatJsonTableColumn))}))"
            : $"{e.FunctionName}({distinct}{args})";
        if (e.WithinGroupOrderBy != null)
            sql += $" WITHIN GROUP (ORDER BY {string.Join(", ", e.WithinGroupOrderBy.Select(o => o.ToSql()))})";
        if (e.Filter != null)
            sql += $" FILTER (WHERE {e.Filter.ToSql()})";
        if (e.Window != null)
            sql += $" OVER ({e.Window.ToSql()})";
        else if (!string.IsNullOrWhiteSpace(e.WindowName))
            sql += $" OVER {e.WindowName}";
        return sql;
    }

    private static string FormatJsonTableColumn(JsonTableColumnSpec c)
    {
        if (c.ForOrdinality) return $"{c.Name} FOR ORDINALITY";
        var sql = c.Name;
        if (!string.IsNullOrWhiteSpace(c.TypeName)) sql += $" {c.TypeName}";
        if (c.Exists) sql += " EXISTS";
        if (c.Path != null) sql += $" PATH {c.Path.ToSql()}";
        if (c.DefaultOnEmpty != null) sql += $" DEFAULT {c.DefaultOnEmpty.ToSql()} ON EMPTY";
        if (c.DefaultOnError != null) sql += $" DEFAULT {c.DefaultOnError.ToSql()} ON ERROR";
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
        var chars = e.Characters != null ? $"{e.Characters.ToSql()} FROM " : "";
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
            if (n.DatabaseName != null) parts.Add(n.DatabaseName);
            if (n.SchemaName != null) parts.Add(n.SchemaName);
            parts.Add(n.TableName);
            sql = string.Join(".", parts);
        }
        if (n.Alias != null) sql += " AS " + n.Alias;
        if (n.ColumnAliases != null && n.ColumnAliases.Count > 0) sql += $"({string.Join(", ", n.ColumnAliases)})";
        foreach (var op in n.TableOperators)
        {
            if (op is PivotClause p) sql += " " + p.ToSql();
            else if (op is UnpivotClause u) sql += " " + u.ToSql();
            else if (op is MatchRecognizeClause m) sql += " " + m.ToSql();
        }
        if (n.Options != null && n.Options.Count > 0)
        {
            sql += $" WITH ({string.Join(", ", n.Options.Select(kv => $"{kv.Key} = {kv.Value.ToSql()}"))})";
        }
        return sql;
    }

    private static string FormatMatchRecognize(MatchRecognizeClause n)
    {
        var parts = new List<string>();
        if (n.PartitionBy.Count > 0) parts.Add($"PARTITION BY {string.Join(", ", n.PartitionBy.Select(e => e.ToSql()))}");
        if (n.OrderBy.Count > 0) parts.Add($"ORDER BY {string.Join(", ", n.OrderBy.Select(o => o.ToSql()))}");
        if (n.Measures.Count > 0)
        {
            parts.Add($"MEASURES {string.Join(", ", n.Measures.Select(c => c.Expression.ToSql() + (c.Alias != null ? $" AS {c.Alias}" : "")))}");
        }
        parts.Add(n.AllRowsPerMatch ? "ALL ROWS PER MATCH" : "ONE ROW PER MATCH");
        parts.Add($"PATTERN ({n.Pattern})");
        if (n.Definitions.Count > 0)
        {
            parts.Add($"DEFINE {string.Join(", ", n.Definitions.Select(kv => $"{kv.Key} AS {kv.Value.ToSql()}"))}");
        }
        return $"MATCH_RECOGNIZE ({string.Join(" ", parts)})" + (n.Alias != null ? $" AS {n.Alias}" : "");
    }

    private static string FormatForClause(ForClause n)
    {
        var options = new List<string>();
        if (n.RootName != null) options.Add($"ROOT('{n.RootName}')");
        if (n.IncludeNullValues) options.Add("INCLUDE_NULL_VALUES");
        if (n.WithoutArrayWrapper) options.Add("WITHOUT_ARRAY_WRAPPER");
        var optStr = options.Count > 0
            ? (n.Type == ForType.JSON ? ", " : " ") + string.Join(", ", options)
            : "";
        return $"FOR {n.Type} {n.Mode}{optStr}";
    }

    private static string FormatWindowFrame(WindowFrame n)
    {
        string start = BoundToSql(n.StartBound, n.StartValue);
        string frame = n.EndBound == null
            ? $"{n.Type} {start}"
            : $"{n.Type} BETWEEN {start} AND {BoundToSql(n.EndBound.Value, n.EndValue)}";
        return frame + ExclusionToSql(n.Exclusion);
    }

    private static string ExclusionToSql(WindowFrameExclusion exclusion) => exclusion switch
    {
        WindowFrameExclusion.CurrentRow => " EXCLUDE CURRENT ROW",
        WindowFrameExclusion.Group => " EXCLUDE GROUP",
        WindowFrameExclusion.Ties => " EXCLUDE TIES",
        _ => ""
    };

    private static string BoundToSql(WindowFrameBoundType bound, Expression? value) => bound switch
    {
        WindowFrameBoundType.PRECEDING => $"{value?.ToSql()} PRECEDING",
        WindowFrameBoundType.FOLLOWING => $"{value?.ToSql()} FOLLOWING",
        WindowFrameBoundType.CURRENT_ROW => "CURRENT ROW",
        WindowFrameBoundType.UNBOUNDED_PRECEDING => "UNBOUNDED PRECEDING",
        WindowFrameBoundType.UNBOUNDED_FOLLOWING => "UNBOUNDED FOLLOWING",
        _ => ""
    };

    private static string FormatWindowClause(WindowClause n)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(n.BaseName)) parts.Add(n.BaseName!);
        if (n.PartitionBy.Count > 0)
            parts.Add("PARTITION BY " + string.Join(", ", n.PartitionBy.Select(p => p.ToSql())));
        if (n.OrderBy.Count > 0)
            parts.Add("ORDER BY " + string.Join(", ", n.OrderBy.Select(o => o.ToSql())));
        if (n.Frame != null)
            parts.Add(n.Frame.ToSql());
        return string.Join(" ", parts);
    }

    // ── Non-AstNode type formatters ──────────────────────────────────────

    private static string FormatStar(StarExpression e)
    {
        if (e.Pattern != null) return $"COLUMNS('{e.Pattern}')";
        var sb = new System.Text.StringBuilder(e.Qualifier != null ? $"{e.Qualifier}.*" : "*");
        if (e.Exclude.Count > 0) sb.Append($" EXCLUDE ({string.Join(", ", e.Exclude)})");
        if (e.Replace.Count > 0) sb.Append($" REPLACE ({string.Join(", ", e.Replace.Select(r => $"{r.Value.ToSql()} AS {r.Column}"))})");
        if (e.Rename.Count > 0) sb.Append($" RENAME ({string.Join(", ", e.Rename.Select(r => $"{r.Column} AS {r.NewName}"))})");
        return sb.ToString();
    }

    private static string FormatJoin(JoinClause join)
    {
        var hintStr = join.Hint switch
        {
            JoinHint.Hash => "HASH ",
            JoinHint.Loop => "LOOP ",
            JoinHint.Merge => "MERGE ",
            _ => ""
        };
        if (join.JoinType == "INNER" && join.Hint != JoinHint.None) return $"INNER {hintStr}JOIN {join.Table.ToSql()} ON {join.Condition.ToSql()}";
        if (join.JoinType == "LEFT" && join.Hint != JoinHint.None) return $"LEFT {hintStr}OUTER JOIN {join.Table.ToSql()} ON {join.Condition.ToSql()}";
        if (join.JoinType == "RIGHT" && join.Hint != JoinHint.None) return $"RIGHT {hintStr}OUTER JOIN {join.Table.ToSql()} ON {join.Condition.ToSql()}";
        if (join.JoinType == "FULL" && join.Hint != JoinHint.None) return $"FULL {hintStr}OUTER JOIN {join.Table.ToSql()} ON {join.Condition.ToSql()}";
        if (join.IsApply)
        {
            bool condTrue = join.Condition is LiteralExpression lc && true.Equals(lc.Value);
            return condTrue ? $"{join.JoinType} {join.Table.ToSql()}" : $"{join.JoinType} {join.Table.ToSql()} ON {join.Condition.ToSql()}";
        }
        return $"{join.JoinType} JOIN {join.Table.ToSql()} ON {join.Condition.ToSql()}";
    }

    private static string FormatGroupingSet(GroupingSetClause g)
    {
        string FmtSet(List<Expression> set) =>
            set.Count == 0 ? "()" : $"({string.Join(", ", set.Select(e => e.ToSql()))})";

        return g.Type switch
        {
            GroupingSetType.Rollup => $"ROLLUP({string.Join(", ", g.GroupSets[0].Select(e => e.ToSql()))})",
            GroupingSetType.Cube => $"CUBE({string.Join(", ", g.GroupSets[0].Select(e => e.ToSql()))})",
            GroupingSetType.GroupingSets => $"GROUPING SETS({string.Join(", ", g.GroupSets.Select(FmtSet))})",
            _ => string.Join(", ", g.GroupSets[0].Select(e => e.ToSql()))
        };
    }

    private static string FormatColumnDefinition(ColumnDefinition col)
    {
        var pk = col.IsPrimaryKey ? " PRIMARY KEY" : "";
        var unq = col.IsUnique ? " UNIQUE" : "";
        var nullable = !col.IsNullable ? " NOT NULL" : "";
        var identity = col.IsIdentity ? " IDENTITY" : "";
        var def = col.DefaultExpression != null ? $" DEFAULT {col.DefaultExpression.ToSql()}" : "";
        var check = col.CheckConstraint != null ? $" CHECK ({col.CheckConstraint.ToSql()})" : "";
        var fk = col.ForeignKey != null ? $" {col.ForeignKey.ToSql()}" : "";
        var tags = col.Metadata.Count > 0
            ? " /* " + string.Join(" ", col.Metadata.Select(kv => $"@{kv.Key}: {kv.Value}")) + " */"
            : "";
        return $"{col.ColumnName} {col.DataType}{pk}{unq}{nullable}{identity}{def}{check}{fk}{tags}";
    }
    private static string FormatPageLayoutDefinition(PageLayoutDefinition layout)
    {
        var parts = new List<string>();
        if (layout.PageSize != null) parts.Add($"PAGE_SIZE = '{layout.PageSize}'");
        if (layout.Orientation != null) parts.Add($"ORIENTATION = '{layout.Orientation}'");
        if (layout.MarginTop.HasValue) parts.Add($"MARGINS = ({layout.MarginTop}, {layout.MarginRight}, {layout.MarginBottom}, {layout.MarginLeft})");
        if (layout.Units != null) parts.Add($"UNITS = '{layout.Units}'");
        if (layout.Overflow != null) parts.Add($"OVERFLOW = '{layout.Overflow}'");
        return "PAGE_LAYOUT (" + string.Join(", ", parts) + ")";
    }

    private static string FormatPrintLayoutOverride(PrintLayoutOverride layout)
    {
        var parts = new List<string>();
        if (layout.PageBreakBefore.HasValue) parts.Add($"PAGE_BREAK_BEFORE = {(layout.PageBreakBefore.Value ? "ON" : "OFF")}");
        if (layout.PageBreakAfter.HasValue) parts.Add($"PAGE_BREAK_AFTER = {(layout.PageBreakAfter.Value ? "ON" : "OFF")}");
        if (layout.KeepTogether.HasValue) parts.Add($"KEEP_TOGETHER = {(layout.KeepTogether.Value ? "ON" : "OFF")}");
        if (layout.ExcludeFromPrint.HasValue) parts.Add($"EXCLUDE_FROM_PRINT = {(layout.ExcludeFromPrint.Value ? "ON" : "OFF")}");
        return "PRINT_LAYOUT (" + string.Join(", ", parts) + ")";
    }

    private static string FormatCreateVisual(CreateVisualStatement s)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{CreationVerb(s.Mode)} VISUAL {s.Name} AS {s.VisualType.ToString().ToUpper()} (");
        var titleClause = FormatTitleClause("TITLE", s.Title, s.TitleIsMarkdown, s.TitleDefinition);
        if (!string.IsNullOrEmpty(titleClause)) sb.AppendLine($"    {titleClause},");
        var subtitleClause = FormatTitleClause("SUBTITLE", s.Subtitle, s.SubtitleIsMarkdown, s.SubtitleDefinition);
        if (!string.IsNullOrEmpty(subtitleClause)) sb.AppendLine($"    {subtitleClause},");
        // COMPAT_BREAK: 0.19 — formatted output for a visual with a TOOLTIP changes.
        // Detail surfaces are formatted here for the same reason pages, containers, and buttons
        // format theirs: omitting the clause silently deletes the author's tooltip on the next
        // format pass. Visuals were the one CREATE that dropped it.
        if (s.Tooltip != null) sb.AppendLine($"    TOOLTIP {FormatTooltip(s.Tooltip)},");
        // TEXT visuals use CONTENT; controls use DEFAULT; both map to DefaultValue on the AST node
        if (s.DefaultValue != null && s.VisualType == VisualType.Text)
            sb.AppendLine($"    CONTENT = {s.DefaultValue.ToSql()},");
        // Only emit SOURCE when actually set (TEXT/controls without a query have an empty source)
        if (s.Source.InlineSelect != null || s.Source.TempTableName != null)
            sb.AppendLine($"    SOURCE = {s.Source.ToSql()},");
        if (s.Mappings.Count > 0)
            sb.AppendLine($"    MAPPINGS ( {string.Join(", ", s.Mappings.Select(m => FormatMapping(m)))} ),");
        if (s.Options.Count > 0)
            sb.AppendLine($"    OPTIONS ( {string.Join(", ", s.Options.Select(o => $"{o.Key} = '{o.Value.Replace("'", "''")}'"))} ),");
        if (s.StyleName != null)
            sb.AppendLine($"    STYLE = {s.StyleName},");
        if (s.Styles.Count > 0 || !s.Palette.IsDefaultOrEmpty)
            sb.AppendLine($"    STYLE ( {FormatStyleAssignments(s.Styles, s.Palette)} ),");
        foreach (var axis in s.AxisOptions)
            sb.AppendLine($"    {axis.Axis}_AXIS ( {string.Join(", ", axis.Options.Select(o => $"{o.Key} = '{o.Value.Replace("'", "''")}'"))} ),");
        if (s.Actions.Count > 0)
            sb.AppendLine($"    ACTIONS ( {FormatActions(s.Actions)} ),");
        if (s.Cascade != null)
            sb.AppendLine($"    {FormatCascade(s.Cascade)},");
        if (s.AdvancedChart != null)
            sb.AppendLine(Indent(FormatAdvancedChart(s.AdvancedChart), 4) + ",");
        if (s.HtmlTemplate != null)
        {
            if (s.HtmlTemplate.Mode != HtmlVisualMode.Single)
                sb.AppendLine($"    MODE = {s.HtmlTemplate.Mode.ToString().ToUpper()},");
            sb.AppendLine($"    TEMPLATE = {Quote(s.HtmlTemplate.Template)},");
            if (s.HtmlTemplate.Css != null)
                sb.AppendLine($"    STYLE ( CSS = {Quote(s.HtmlTemplate.Css)} ),");
            if (s.HtmlTemplate.Fallback != null)
                sb.AppendLine($"    FALLBACK = {Quote(s.HtmlTemplate.Fallback)},");
        }
        if (s.DefaultValue != null && s.VisualType != VisualType.Text)
            sb.AppendLine($"    DEFAULT = {s.DefaultValue.ToSql()},");
        if (s.PrintLayout != null)
            sb.AppendLine($"    {FormatPrintLayoutOverride(s.PrintLayout)},");

        var result = sb.ToString().TrimEnd().TrimEnd(',');
        return result + "\n);";
    }

    private static string FormatHtmlTemplate(HtmlTemplateDefinition def)
    {
        var parts = new List<string>();
        if (def.Mode != HtmlVisualMode.Single)
            parts.Add($"MODE = {def.Mode.ToString().ToUpper()}");
        parts.Add($"TEMPLATE = {Quote(def.Template)}");
        if (def.Css != null)
            parts.Add($"STYLE ( CSS = {Quote(def.Css)} )");
        if (def.Fallback != null)
            parts.Add($"FALLBACK = {Quote(def.Fallback)}");
        return string.Join(", ", parts);
    }

    private static string FormatCascade(CascadeDefinition cascade)
    {
        var parts = new List<string> { $"MODE = {cascade.Mode.ToString().ToUpperInvariant()}" };
        if (cascade.Parents.Count > 0)
            parts.Add("PARENTS (" + string.Join(", ", cascade.Parents.Select(parent => $"{parent.ParameterName} = {parent.ColumnName}")) + ")");
        parts.Add($"INVALID = {cascade.InvalidSelection.ToString().ToUpperInvariant()}");
        parts.Add($"NULL = {cascade.NullSelection.ToString().ToUpperInvariant()}");
        parts.Add($"ALL_VALUE = {Quote(cascade.AllValue)}");
        parts.Add($"MULTISELECT = {cascade.MultiSelect.ToString().ToUpperInvariant()}");
        return "CASCADE ( " + string.Join(", ", parts) + " )";
    }

    private static string FormatAdvancedChart(AdvancedChartDefinition chart)
    {
        var sections = new List<string>
        {
            "COORDINATE ( " + string.Join(", ", CoordinateParts(chart.Coordinate)) + " )",
            "LAYERS (\n" + string.Join(",\n", chart.Layers.Select(layer => Indent(FormatAdvancedLayer(layer), 4))) + "\n)"
        };
        if (!chart.Scales.IsDefaultOrEmpty)
            sections.Insert(1, "SCALES (\n" + string.Join(",\n", chart.Scales.Select(scale => Indent(FormatAdvancedScale(scale), 4))) + "\n)");
        if (!chart.Encodings.IsDefaultOrEmpty)
            sections.Insert(chart.Scales.IsDefaultOrEmpty ? 1 : 2, "ENCODINGS (\n" + string.Join(",\n", chart.Encodings.Select(encoding => Indent(FormatAdvancedEncoding(encoding), 4))) + "\n)");
        if (chart.Facet != null)
        {
            var facets = new List<string>();
            if (chart.Facet.RowField != null) facets.Add($"ROW = {chart.Facet.RowField}");
            if (chart.Facet.ColumnField != null) facets.Add($"COLUMN = {chart.Facet.ColumnField}");
            if (chart.Facet.WrapField != null) facets.Add($"WRAP = {chart.Facet.WrapField}");
            if (chart.Facet.Columns.HasValue) facets.Add($"COLUMNS = {chart.Facet.Columns.Value}");
            sections.Add("FACET ( " + string.Join(", ", facets) + " )");
        }
        sections.Add($"RESOLVE ( X = {Upper(chart.Resolution.X)}, Y = {Upper(chart.Resolution.Y)}, COLOR = {Upper(chart.Resolution.Color)} )");
        return "CHART (\n" + string.Join(",\n", sections.Select(section => Indent(section, 4))) + "\n)";
    }

    private static IEnumerable<string> CoordinateParts(AdvancedChartCoordinate coordinate)
    {
        yield return $"TYPE = {Upper(coordinate.Kind)}";
        if (coordinate.StartAngle.HasValue) yield return $"START_ANGLE = {Number(coordinate.StartAngle.Value)}";
        if (coordinate.EndAngle.HasValue) yield return $"END_ANGLE = {Number(coordinate.EndAngle.Value)}";
        if (coordinate.InnerRadius.HasValue) yield return $"INNER_RADIUS = {Number(coordinate.InnerRadius.Value)}";
        if (coordinate.AspectRatio.HasValue) yield return $"ASPECT_RATIO = {Number(coordinate.AspectRatio.Value)}";
        if (coordinate.Projection.HasValue) yield return $"PROJECTION = {Upper(coordinate.Projection.Value)}";
        if (coordinate.MapName is not null) yield return $"MAP_NAME = {Quote(coordinate.MapName)}";
        if (coordinate.MapFile is not null) yield return $"MAP_FILE = {Quote(coordinate.MapFile)}";
        if (coordinate.FeatureKey is not null) yield return $"FEATURE_KEY = {Quote(coordinate.FeatureKey)}";
    }

    private static string FormatAdvancedScale(AdvancedChartScale scale)
    {
        var options = new List<string>
        {
            $"CHANNEL = {Upper(scale.Channel)}",
            $"INCLUDE_ZERO = {(scale.IncludeZero ? "ON" : "OFF")}"
        };
        if (scale.Minimum != null) options.Add($"MIN = {scale.Minimum.ToSql()}");
        if (scale.Maximum != null) options.Add($"MAX = {scale.Maximum.ToSql()}");
        options.Add(scale.ExplicitOrder.IsDefaultOrEmpty
            ? $"ORDER = {Upper(scale.Order)}"
            : "ORDER = (" + string.Join(", ", scale.ExplicitOrder.Select(Format)) + ")");
        if (scale.ColorRange is { } range)
        {
            var rangeOptions = new List<string> { $"LOW = {range.Low.ToSql()}" };
            if (range.Mid is not null) rangeOptions.Add($"MID = {range.Mid.ToSql()}");
            rangeOptions.Add($"HIGH = {range.High.ToSql()}");
            if (range.Midpoint is not null) rangeOptions.Add($"MIDPOINT = {range.Midpoint.ToSql()}");
            if (range.NullColor is not null) rangeOptions.Add($"NULL_COLOR = {range.NullColor.ToSql()}");
            options.Add($"RANGE = {Upper(range.Kind)}( {string.Join(", ", rangeOptions)} )");
        }
        return $"{scale.Name} = {Upper(scale.Kind)} ( {string.Join(", ", options)} )";
    }

    private static string FormatAdvancedLayer(AdvancedChartLayer layer)
    {
        var sections = new List<string> { $"Z_INDEX = {layer.ZIndex}" };
        if (!layer.InheritEncodings) sections.Add("INHERIT_ENCODINGS = OFF");
        if (layer.BandSize != .75m) sections.Add($"BAND_SIZE = {Number(layer.BandSize)}");
        if (layer.Mark == AdvancedChartMarkKind.Tick && layer.TickThickness != .15m) sections.Add($"THICKNESS = {Number(layer.TickThickness)}");
        if (layer.Mark == AdvancedChartMarkKind.Tick && layer.TickOrientation != AdvancedChartTickOrientation.Auto)
            sections.Add($"ORIENTATION = {Upper(layer.TickOrientation)}");
        if (layer.Position.Kind != AdvancedChartPositionKind.Identity) sections.Add(FormatAdvancedPosition(layer.Position));
        sections.Add("ENCODINGS (\n" + string.Join(",\n",
            layer.Encodings.Select(encoding => Indent(FormatAdvancedEncoding(encoding), 4))) + "\n)");
        if (!layer.Styles.IsDefaultOrEmpty)
            sections.Add("STYLE ( " + string.Join(", ", layer.Styles.Select(style => $"{style.Name} = {style.Value.ToSql()}")) + " )");
        if (!layer.Conditions.IsDefaultOrEmpty)
            sections.Add("CONDITIONS (\n" + string.Join(",\n", layer.Conditions.Select(condition =>
                Indent($"{Upper(condition.Channel)} WHEN {condition.Predicate.ToSql()} THEN {condition.WhenTrue.ToSql()}" +
                    (condition.WhenFalse == null ? string.Empty : $" ELSE {condition.WhenFalse.ToSql()}"), 4))) + "\n)");
        return $"{layer.Name} = {Upper(layer.Mark)} (\n" +
            string.Join(",\n", sections.Select(section => Indent(section, 4))) + "\n)";
    }

    private static string FormatAdvancedPosition(AdvancedChartPosition position)
    {
        if (position.Kind == AdvancedChartPositionKind.Identity) return "POSITION = IDENTITY";
        var options = new List<string> { $"X = {Number(position.X)}", $"Y = {Number(position.Y)}" };
        if (position.Kind == AdvancedChartPositionKind.Jitter)
        {
            options.Add($"KEY = {position.KeyField}");
            if (position.Seed != 0) options.Add($"SEED = {position.Seed}");
        }
        else options.Add($"UNIT = {Upper(position.Unit)}");
        return $"POSITION = {Upper(position.Kind)}( {string.Join(", ", options)} )";
    }

    private static string FormatAdvancedEncoding(AdvancedChartEncoding encoding)
    {
        var options = new List<string> { $"TYPE = {Upper(encoding.DataKind)}" };
        if (encoding.Scale != null) options.Add($"SCALE = {encoding.Scale}");
        if (encoding.Axis != AdvancedChartAxisRole.None) options.Add($"AXIS = {Upper(encoding.Axis)}");
        if (encoding.Sort != AdvancedChartSortDirection.Source) options.Add($"SORT = {Upper(encoding.Sort)}");
        if (encoding.Format != null) options.Add($"FORMAT = {Quote(encoding.Format)}");
        if (encoding.Stack != AdvancedChartStackMode.None) options.Add($"STACK = {Upper(encoding.Stack)}");
        var source = encoding.Source.Kind switch
        {
            AdvancedChartBindingSourceKind.Field => encoding.Source.Field!,
            AdvancedChartBindingSourceKind.Datum => $"DATUM({encoding.Source.Constant!.ToSql()})",
            AdvancedChartBindingSourceKind.Value => $"VALUE({encoding.Source.Constant!.ToSql()})",
            _ => throw new InvalidOperationException("Unsupported advanced chart binding source.")
        };
        return $"{Upper(encoding.Channel)} = {source} ( {string.Join(", ", options)} )";
    }

    private static string Upper<T>(T value) where T : struct, Enum => value switch
    {
        AdvancedChartCoordinateKind.TransposedCartesian => "TRANSPOSED_CARTESIAN",
        _ => value.ToString().ToUpperInvariant()
    };

    private static string Number(decimal value) => value.ToString("0.############################", System.Globalization.CultureInfo.InvariantCulture);

    private static string Indent(string value, int spaces)
    {
        var prefix = new string(' ', spaces);
        return prefix + value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\n" + prefix, StringComparison.Ordinal);
    }

    private static string FormatCreatePage(CreatePageStatement s)
    {
        var parts = new List<string>();
        var titleClause = FormatTitleClause("TITLE", s.Title, s.TitleIsMarkdown, s.TitleDefinition);
        if (!string.IsNullOrEmpty(titleClause)) parts.Add(titleClause);
        var subtitleClause = FormatTitleClause("SUBTITLE", s.Subtitle, s.SubtitleIsMarkdown, s.SubtitleDefinition);
        if (!string.IsNullOrEmpty(subtitleClause)) parts.Add(subtitleClause);
        if (s.Tooltip != null) parts.Add($"TOOLTIP {FormatTooltip(s.Tooltip)}");
        parts.Add($"STRUCTURE = {Quote(s.Structure)}");
        if (s.SlotMap.Count > 0)
            parts.Add("MAP (" + string.Join(", ", s.SlotMap.Select(kv => $"{Quote(kv.Key)} = {kv.Value}")) + ")");
        if (s.StyleName != null)
            parts.Add($"STYLE = {s.StyleName}");
        if (s.Styles.Count > 0 || !s.Palette.IsDefaultOrEmpty)
            parts.Add("STYLE (" + FormatStyleAssignments(s.Styles, s.Palette) + ")");
        if (s.Visibility != null) parts.Add($"VISIBLE = {s.Visibility}");
        if (s.RefreshIntervalSeconds > 0) parts.Add($"REFRESH = {s.RefreshIntervalSeconds}");
        if (s.PrintLayout != null) parts.Add(FormatPageLayoutDefinition(s.PrintLayout));

        return $"{CreationVerb(s.Mode)} PAGE {s.Name} AS {s.PageMode.ToString().ToUpperInvariant()} ({string.Join(", ", parts)});";
    }

    private static string FormatCreateDataset(CreateDatasetStatement s)
    {
        var options = new List<string>();
        if (s.Ttl != null) options.Add($"TTL = {Quote(s.Ttl)}");
        if (s.Compress) options.Add("COMPRESS = ON");
        if (s.AccessLevel == DatasetAccessLevel.Public) options.Add("ACCESS PUBLIC");
        switch (s.EncryptionMode)
        {
            case DatasetEncryptionMode.None:
                options.Add("ENCRYPT = OFF");
                break;
            case DatasetEncryptionMode.Password:
                options.Add("ENCRYPT = PASSWORD");
                if (s.EncryptionPassword != null) options.Add($"PASSWORD = {Quote(s.EncryptionPassword)}");
                break;
            case DatasetEncryptionMode.KeyFile:
                options.Add("ENCRYPT = KEYFILE");
                if (s.KeyFile != null) options.Add($"KEYFILE = {Quote(s.KeyFile)}");
                break;
        }

        var optionText = options.Count > 0 ? " " + string.Join(" ", options) : "";
        return $"{CreationVerb(s.Mode)} DATASET {s.TempTableName}{optionText} AS ({s.SourceQuery.ToSql().TrimEnd(';')});";
    }

    private static string FormatCreateContainer(CreateContainerStatement s)
    {
        var parts = new List<string>();
        var titleClause = FormatTitleClause("TITLE", s.Title, s.TitleIsMarkdown, s.TitleDefinition);
        if (!string.IsNullOrEmpty(titleClause)) parts.Add(titleClause);
        var subtitleClause = FormatTitleClause("SUBTITLE", s.Subtitle, s.SubtitleIsMarkdown, s.SubtitleDefinition);
        if (!string.IsNullOrEmpty(subtitleClause)) parts.Add(subtitleClause);
        if (s.Tooltip != null) parts.Add($"TOOLTIP {FormatTooltip(s.Tooltip)}");
        if (s.Visibility != null) parts.Add($"VISIBLE = {s.Visibility}");
        if (s.Icon != null) parts.Add($"ICON = {Quote(s.Icon)}");
        if (s.StyleName != null)
            parts.Add($"STYLE = {s.StyleName}");
        if (s.Styles.Count > 0 || !s.Palette.IsDefaultOrEmpty)
            parts.Add("STYLE (" + FormatStyleAssignments(s.Styles, s.Palette) + ")");
        if (s.Structure != null || s.SlotMap.Count > 0 || !s.IsPinnable)
        {
            var layout = new List<string>();
            if (s.Structure != null) layout.Add($"STRUCTURE = {Quote(s.Structure)}");
            if (s.SlotMap.Count > 0)
                layout.Add("MAP (" + string.Join(", ", s.SlotMap.Select(kv => $"{Quote(kv.Key)} = {kv.Value}")) + ")");
            if (!s.IsPinnable) layout.Add("PINNABLE = OFF");
            parts.Add("LAYOUT (" + string.Join(", ", layout) + ")");
        }

        return $"{CreationVerb(s.Mode)} CONTAINER {s.Name} AS {s.ContainerType.ToUpperInvariant()} ({string.Join(", ", parts)});";
    }

    private static string FormatCreateNavigation(CreateNavigationStatement s)
    {
        var parts = new List<string>
        {
            $"ORIENTATION = {s.Orientation.ToString().ToUpperInvariant()}"
        };
        if (s.DefaultPage != null) parts.Add($"DEFAULT = {s.DefaultPage}");
        if (s.Pages.Count > 0) parts.Add($"PAGES ({string.Join(", ", s.Pages)})");
        return $"{CreationVerb(s.Mode)} NAVIGATION {s.Name} AS {s.NavType.ToString().ToUpperInvariant()} ({string.Join(", ", parts)});";
    }

    private static string FormatActions(List<VisualAction> actions)
    {
        var grouped = actions.GroupBy(a => a.Trigger);
        var parts = new List<string>();
        foreach (var g in grouped)
        {
            var trigger = g.Key;
            var list = g.ToList();
            if (list.Count == 1)
            {
                parts.Add($"{trigger} = {list[0].ToSql()}");
            }
            else
            {
                parts.Add($"{trigger} = ({string.Join(", ", list.Select(a => a.ToSql()))})");
            }
        }
        return string.Join(", ", parts);
    }

    private static string FormatCreateButton(CreateButtonStatement s)
    {
        var parts = new List<string>();
        if (s.Title != null) parts.Add($"TITLE = {s.Title.ToSql()}");
        if (s.Tooltip != null) parts.Add($"TOOLTIP {FormatTooltip(s.Tooltip)}");
        if (s.Options.Count > 0) parts.Add("OPTIONS (" + FormatVisualOptions(s.Options) + ")");
        if (s.Actions.Count > 0) parts.Add("ACTIONS (" + FormatActions(s.Actions) + ")");
        if (s.StyleName != null)
            parts.Add($"STYLE = {s.StyleName}");
        if (s.Styles.Count > 0 || !s.Palette.IsDefaultOrEmpty)
            parts.Add("STYLE (" + FormatStyleAssignments(s.Styles, s.Palette) + ")");

        return $"{CreationVerb(s.Mode)} BUTTON {s.Name} AS ({string.Join(", ", parts)});";
    }

    private static string FormatCreateBookmark(CreateBookmarkStatement s)
    {
        var parts = new List<string>();
        if (s.Title != null) parts.Add($"TITLE = {s.Title.ToSql()}");
        if (s.Parameters.Count > 0)
        {
            // Value.ToSql() preserves the declared type: numbers/booleans/NULL are never quoted.
            var paramParts = s.Parameters.Select(p => $"{p.ParameterName} = {p.Value.ToSql()}");
            parts.Add($"PARAMETERS ({string.Join(", ", paramParts)})");
        }
        if (s.PageName != null) parts.Add($"PAGE = {s.PageName}");
        if (s.StateEntries.Count > 0)
        {
            var stateParts = s.StateEntries.Select(e =>
                $"{e.ObjectName}.{e.Property.ToString().ToUpperInvariant()} = {(e.On ? "ON" : "OFF")}");
            parts.Add($"STATE ({string.Join(", ", stateParts)})");
        }
        if (s.IsDefault) parts.Add("DEFAULT = ON");
        return $"CREATE BOOKMARK {s.Name} AS ({string.Join(", ", parts)});";
    }

    private static string FormatCreateStyle(CreateStyleStatement s)
    {
        if (s.StyleName != null)
            return $"STYLE = {s.StyleName};";
        return $"{CreationVerb(s.Mode)} STYLE {s.Name} AS ({FormatStyleAssignments(s.Styles, s.Palette)});";
    }

    private static string FormatTooltip(TooltipDefinition tooltip)
    {
        if (tooltip.PlainText != null)
            return $"= {tooltip.PlainText.ToSql()}";
        if (tooltip.ContainerRef != null)
            return tooltip.ContainerRef;

        var parts = new List<string>();
        if (tooltip.InlineMarkdown != null)
            parts.Add(Quote(tooltip.InlineMarkdown));
        if (tooltip.InlineVisuals is { Count: > 0 })
            parts.Add("VISUALS (" + string.Join(", ", tooltip.InlineVisuals) + ")");
        return "(" + string.Join(", ", parts) + ")";
    }

    private static string FormatSetThreshold(SetThresholdStatement s)
    {
        string name = s.Type switch
        {
            ThresholdType.JoinSpill => "JOIN_SPILL_THRESHOLD",
            ThresholdType.ExternalHashPartitions => "EXTERNAL_HASH_PARTITIONS",
            ThresholdType.ExternalSortChunkSize => "EXTERNAL_SORT_CHUNK_SIZE",
            ThresholdType.MaxSmtpEmailsPerScript => "MAX_SMTP_EMAILS_PER_SCRIPT",
            ThresholdType.CaseSensitive => "CASE_SENSITIVE",
            _ => "UNKNOWN"
        };
        return $"SET {name} = {s.Value.ToSql()};";
    }

    private static string FormatCreateTemplate(CreateTemplateStatement s)
    {
        var options = string.Join(", ", s.Options.Select(o => $"{o.Key} = '{o.Value.Replace("'", "''")}'"));
        var modeStr = CreationVerb(s.Mode);
        return $"{modeStr} TEMPLATE {s.Name} AS ({options});";
    }

    private static string FormatCreateTheme(CreateThemeStatement s)
    {
        var props = string.Join(", ", s.Properties.Select(p => $"{p.Key} = '{p.Value.Replace("'", "''")}'"));
        var modeStr = CreationVerb(s.Mode);
        return $"{modeStr} THEME {s.Name} AS ({props});";
    }

    private static string FormatStyleAssignments(IReadOnlyDictionary<string, string> values, ImmutableArray<string> palette)
    {
        var parts = new List<string>();
        if (!palette.IsDefaultOrEmpty)
        {
            parts.Add($"PALETTE = ({string.Join(", ", palette.Select(Quote))})");
        }
        foreach (var kv in values)
        {
            parts.Add($"{kv.Key} = {Quote(kv.Value)}");
        }
        return string.Join(", ", parts);
    }

    private static string FormatStringAssignments(IReadOnlyDictionary<string, string> values) =>
        string.Join(", ", values.Select(kv => $"{kv.Key} = {Quote(kv.Value)}"));

    private static string FormatVisualOptions(IEnumerable<VisualOption> options) =>
        string.Join(", ", options.Select(o => $"{o.Key} = {Quote(o.Value)}"));

    private static string FormatPublishBundle(PublishBundleStatement s)
    {
        var options = new List<string>();
        if (s.PasswordMode == BundleSecretMode.Prompt)
        {
            options.Add("PASSWORD = PROMPT");
        }
        else if (s.PasswordMode == BundleSecretMode.Literal && s.Password != null)
        {
            options.Add($"PASSWORD = '{s.Password.Replace("'", "''")}'");
        }

        if (!string.IsNullOrEmpty(s.EncryptionMode))
        {
            options.Add($"ENCRYPT = {s.EncryptionMode}");
        }

        if (!string.IsNullOrEmpty(s.KeyFile))
        {
            options.Add($"KEYFILE = '{s.KeyFile.Replace("'", "''")}'");
        }

        if (!string.IsNullOrEmpty(s.Description))
        {
            options.Add($"DESCRIPTION = '{s.Description.Replace("'", "''")}'");
        }

        var optionsStr = options.Count > 0
            ? " WITH (" + string.Join(", ", options) + ")"
            : "";

        return $"PUBLISH BUNDLE '{s.BundleName.Replace("'", "''")}' FROM {s.SourcePath.ToSql()} ENTRY '{s.EntryPath.Replace("'", "''")}'{optionsStr};";
    }

    private static string FormatValidateBundle(ValidateBundleStatement s)
    {
        var options = new List<string>();
        if (s.PasswordMode == BundleSecretMode.Prompt)
        {
            options.Add("PASSWORD = PROMPT");
        }
        else if (s.PasswordMode == BundleSecretMode.Literal && s.Password != null)
        {
            options.Add($"PASSWORD = '{s.Password.Replace("'", "''")}'");
        }

        var optionsStr = options.Count > 0
            ? " WITH (" + string.Join(", ", options) + ")"
            : "";

        return $"VALIDATE BUNDLE '{s.BundleName.Replace("'", "''")}' FROM {s.SourcePath.ToSql()} ENTRY '{s.EntryPath.Replace("'", "''")}'{optionsStr};";
    }

    private static string FormatExportScript(ExportScriptStatement s)
    {
        return $"EXPORT SCRIPT {s.SourcePath.ToSql()} TO {s.TargetPath.ToSql()};";
    }

    private static string FormatExpectSchema(ExpectSchemaStatement s)
    {
        var target = s.Target;
        var drift = s.WarnOnDrift ? " ON DRIFT WARN" : "";
        if (s.SchemaPath != null)
        {
            var cleanPath = s.SchemaPath.Trim('\'', '"');
            return $"EXPECT SCHEMA {target} FROM '{cleanPath.Replace("'", "''")}'{drift};";
        }
        var cols = s.Columns != null
            ? "(" + string.Join(", ", s.Columns.Select(c => $"{c.ColumnName} {c.DataType}{(c.NotNull ? " NOT NULL" : "")}")) + ")"
            : "()";
        return $"EXPECT SCHEMA {target} {cols}{drift};";
    }

    private static string FormatMapping(VisualMapping m)
    {
        if (string.Equals(m.Role, "SPARKLINE", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(m.SparklineSource))
            {
                var cardSparklineType = (m.SparklineType ?? "line").ToUpperInvariant();
                return $"SPARKLINE = {m.SparklineSource} (X = {m.SparklineXColumn}, Y = {m.SparklineYColumn}, TYPE = {cardSparklineType})";
            }
            var cols = m.SparklineColumns != null ? string.Join(", ", m.SparklineColumns) : "";
            var type = m.SparklineType != null ? m.SparklineType.ToUpperInvariant() : "LINE";
            var alias = m.DisplayName != null ? $" AS '{m.DisplayName.Replace("'", "''")}'" : "";
            return $"SPARKLINE({cols}) {type}{alias}";
        }

        if (m.Role != null && m.Role != "COLUMN" && !string.Equals(m.Role, m.Column, StringComparison.OrdinalIgnoreCase))
        {
            return $"{m.Role} = {m.Column}";
        }

        var sb = new System.Text.StringBuilder();
        sb.Append(m.Column);
        if (!string.IsNullOrEmpty(m.Format))
            sb.Append($" FORMAT '{m.Format.Replace("'", "''")}'");
        if (!string.IsNullOrEmpty(m.Align))
            sb.Append($" ALIGN '{m.Align.Replace("'", "''")}'");
        if (m.DataBar)
        {
            sb.Append(" DATA_BAR");
            if (!string.IsNullOrEmpty(m.DataBarColor))
                sb.Append($" COLOR '{m.DataBarColor.Replace("'", "''")}'");
        }
        if (!string.IsNullOrEmpty(m.ColorScaleFrom))
        {
            sb.Append($" COLOR_SCALE FROM '{m.ColorScaleFrom.Replace("'", "''")}'");
            if (!string.IsNullOrEmpty(m.ColorScaleTo))
                sb.Append($" TO '{m.ColorScaleTo.Replace("'", "''")}'");
        }
        if (m.CellRenderer == "image")
        {
            sb.Append(" IMAGE");
            if (m.ImageWidth.HasValue)
                sb.Append($" WIDTH {m.ImageWidth}");
        }
        else if (m.CellRenderer == "hyperlink")
        {
            sb.Append(" HYPERLINK");
            if (!string.IsNullOrEmpty(m.HyperlinkLabel))
                sb.Append($" LABEL '{m.HyperlinkLabel.Replace("'", "''")}'");
        }
        if (m.ProgressBar)
        {
            var options = new List<string>();
            if (m.ProgressMinimum.HasValue) options.Add($"MIN = {m.ProgressMinimum.Value.ToString(CultureInfo.InvariantCulture)}");
            if (m.ProgressMaximum.HasValue) options.Add($"MAX = {m.ProgressMaximum.Value.ToString(CultureInfo.InvariantCulture)}");
            if (!string.IsNullOrEmpty(m.ProgressColor)) options.Add($"COLOR = '{m.ProgressColor.Replace("'", "''")}'");
            sb.Append($" PROGRESS_BAR ({string.Join(", ", options)})");
        }
        if (!string.IsNullOrEmpty(m.DisplayName))
            sb.Append($" AS '{m.DisplayName.Replace("'", "''")}'");

        return sb.ToString();
    }

    private static string FormatSecurityOverride(SetSecurityOverrideStatement s)
    {
        var name = s.Override switch
        {
            SecurityOverride.FileTypeAccess => "ALLOW_FILE_TYPE_ACCESS",
            SecurityOverride.FileTypeExtension => $"ALLOW_FILE_TYPE_ACCESS = {s.Value?.ToSql()}",
            SecurityOverride.LargeFileCount => "ALLOW_GREATER_THAN_100_FILE",
            SecurityOverride.DeepRecursion => "ALLOW_RECURSIVE_GREATER_THAN_5_LAYERS",
            SecurityOverride.LargeStringResults => "ALLOW_LARGE_STRING_RESULTS",
            _ => "ALLOW_FILE_TYPE_ACCESS"
        };
        if (s.Override == SecurityOverride.FileTypeExtension)
        {
            return $"SET {name};";
        }
        return $"SET {name} {(s.Enabled ? "ON" : "OFF")};";
    }

    private static string FormatAssertTable(AssertTableStatement s)
    {
        var sb = new StringBuilder();
        sb.Append($"ASSERT TABLE {s.ActualTable} MATCHES {s.ExpectedTable}");
        var opts = new List<string>();
        if (s.IgnoreOrder) opts.Add("IGNORE_ORDER = TRUE");
        if (s.Tolerance.HasValue) opts.Add($"TOLERANCE = {s.Tolerance.Value}");
        if (s.IgnoreColumns is { Count: > 0 }) opts.Add($"IGNORE_COLUMNS = '{string.Join(",", s.IgnoreColumns)}'");
        if (s.Message != null) opts.Add($"MESSAGE = {s.Message.ToSql()}");
        if (opts.Count > 0) sb.Append($" WITH ({string.Join(", ", opts)})");
        sb.Append(';');
        return sb.ToString();
    }

    public static string FormatTitleDefinition(TitleDefinition d)
    {
        var parts = new List<string>();
        if (d.Text != null)
            parts.Add($"TEXT = {d.Text.ToSql()}{(d.IsMarkdown ? " MARKDOWN" : "")}");
        if (d.Color != null) parts.Add($"COLOR = '{d.Color.Replace("'", "''")}'");
        if (d.Font != null) parts.Add($"FONT = '{d.Font.Replace("'", "''")}'");
        if (d.Size != null) parts.Add($"SIZE = '{d.Size.Replace("'", "''")}'");
        if (d.Weight != null) parts.Add($"WEIGHT = '{d.Weight.Replace("'", "''")}'");
        if (d.Align != null) parts.Add($"ALIGN = {d.Align.ToUpperInvariant()}");
        return "TITLE ( " + string.Join(", ", parts) + " )";
    }

    private static string FormatTitleClause(string propertyName, Expression? expr, bool isMd, TitleDefinition? def)
    {
        if (def != null && (def.Color != null || def.Font != null || def.Size != null || def.Weight != null || def.Align != null))
        {
            var parts = new List<string>();
            if (def.Text != null)
                parts.Add($"TEXT = {def.Text.ToSql()}{(def.IsMarkdown ? " MARKDOWN" : "")}");
            if (def.Color != null) parts.Add($"COLOR = '{def.Color.Replace("'", "''")}'");
            if (def.Font != null) parts.Add($"FONT = '{def.Font.Replace("'", "''")}'");
            if (def.Size != null) parts.Add($"SIZE = '{def.Size.Replace("'", "''")}'");
            if (def.Weight != null) parts.Add($"WEIGHT = '{def.Weight.Replace("'", "''")}'");
            if (def.Align != null) parts.Add($"ALIGN = {def.Align.ToUpperInvariant()}");
            return $"{propertyName} ( {string.Join(", ", parts)} )";
        }
        if (expr != null)
        {
            if (isMd)
                return $"{propertyName} = ({expr.ToSql()})";
            return $"{propertyName} = {expr.ToSql()}";
        }
        return string.Empty;
    }
}
