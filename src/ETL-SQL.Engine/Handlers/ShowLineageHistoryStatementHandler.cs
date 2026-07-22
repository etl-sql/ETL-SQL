using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;
using Microsoft.Extensions.Configuration;

namespace ETL_SQL.Engine.Handlers;

public class ShowLineageHistoryForTableStatementHandler : IStatementHandler
{
    public Type SupportedStatementType => typeof(ShowLineageHistoryForTableStatement);
    private readonly ILineageCatalogStore _catalog;
    private readonly IConfiguration? _config;

    public ShowLineageHistoryForTableStatementHandler(ILineageCatalogStore catalog, IConfiguration? config = null)
    {
        _catalog = catalog;
        _config = config;
    }

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (ShowLineageHistoryForTableStatement)statement;

        if (stmt.At != null)
        {
            await LineageHistoryRouting.RouteToRemoteAsync(stmt, stmt.At, context);
            return;
        }

        int defaultLimit = _config?.GetValue<int>("Engine:DefaultHistoryLimit") ?? 100;
        var entries = await _catalog.GetHistoryForTableAsync(stmt.TableName, stmt.Limit ?? defaultLimit);
        var table = await LineageHistoryRouting.BuildTable(entries);

        if (stmt.IntoTable != null)
        {
            if (!context.Connections.ContainsKey(stmt.IntoTable))
                context.Connections[stmt.IntoTable] = new InMemoryDataSource();
            var dest = await context.ResolveDataSourceAsync(new TableReference(stmt.IntoTable));
            await dest.WriteBatches(new[] { table }.ToAsyncEnumerable());
        }
        else
        {
            context.LastResult = table;
            context.LastResultSets.Add(table);
            context.OnResultSet?.Invoke(table);
        }
    }
}

public class ShowLineageHistoryForTagStatementHandler : IStatementHandler
{
    public Type SupportedStatementType => typeof(ShowLineageHistoryForTagStatement);
    private readonly ILineageCatalogStore _catalog;
    private readonly IConfiguration? _config;

    public ShowLineageHistoryForTagStatementHandler(ILineageCatalogStore catalog, IConfiguration? config = null)
    {
        _catalog = catalog;
        _config = config;
    }

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (ShowLineageHistoryForTagStatement)statement;

        if (stmt.At != null)
        {
            await LineageHistoryRouting.RouteToRemoteAsync(stmt, stmt.At, context);
            return;
        }

        int defaultLimit = _config?.GetValue<int>("Engine:DefaultHistoryLimit") ?? 100;
        var entries = await _catalog.GetHistoryForTagAsync(stmt.TagKey, stmt.TagValue, stmt.Limit ?? defaultLimit);
        var table = await LineageHistoryRouting.BuildTable(entries);

        if (stmt.IntoTable != null)
        {
            if (!context.Connections.ContainsKey(stmt.IntoTable))
                context.Connections[stmt.IntoTable] = new InMemoryDataSource();
            var dest = await context.ResolveDataSourceAsync(new TableReference(stmt.IntoTable));
            await dest.WriteBatches(new[] { table }.ToAsyncEnumerable());
        }
        else
        {
            context.LastResult = table;
            context.LastResultSets.Add(table);
            context.OnResultSet?.Invoke(table);
        }
    }
}

public class ShowLineageHistoryForJobStatementHandler : IStatementHandler
{
    public Type SupportedStatementType => typeof(ShowLineageHistoryForJobStatement);
    private readonly ILineageCatalogStore _catalog;
    private readonly IConfiguration? _config;

    public ShowLineageHistoryForJobStatementHandler(ILineageCatalogStore catalog, IConfiguration? config = null)
    {
        _catalog = catalog;
        _config = config;
    }

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (ShowLineageHistoryForJobStatement)statement;

        if (stmt.At != null)
        {
            await LineageHistoryRouting.RouteToRemoteAsync(stmt, stmt.At, context);
            return;
        }

        int defaultLimit = _config?.GetValue<int>("Engine:DefaultHistoryLimit") ?? 100;
        var entries = await _catalog.GetHistoryForJobAsync(stmt.JobName, stmt.Limit ?? defaultLimit);
        var table = await LineageHistoryRouting.BuildTable(entries);

        if (stmt.IntoTable != null)
        {
            if (!context.Connections.ContainsKey(stmt.IntoTable))
                context.Connections[stmt.IntoTable] = new InMemoryDataSource();
            var dest = await context.ResolveDataSourceAsync(new TableReference(stmt.IntoTable));
            await dest.WriteBatches(new[] { table }.ToAsyncEnumerable());
        }
        else
        {
            context.LastResult = table;
            context.LastResultSets.Add(table);
            context.OnResultSet?.Invoke(table);
        }
    }
}

public class ShowLineageHistoryForMissingTagsStatementHandler : IStatementHandler
{
    public Type SupportedStatementType => typeof(ShowLineageHistoryForMissingTagsStatement);
    private readonly ILineageCatalogStore _catalog;
    private readonly IConfiguration? _config;

    public ShowLineageHistoryForMissingTagsStatementHandler(ILineageCatalogStore catalog, IConfiguration? config = null)
    {
        _catalog = catalog;
        _config = config;
    }

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (ShowLineageHistoryForMissingTagsStatement)statement;

        if (stmt.At != null)
        {
            await LineageHistoryRouting.RouteToRemoteAsync(stmt, stmt.At, context);
            return;
        }

        int defaultLimit = _config?.GetValue<int>("Engine:DefaultHistoryLimit") ?? 100;
        var entries = await _catalog.GetMissingMetadataAsync(
            StewardshipTagCatalog.RequiredStewardshipTags.ToArray(),
            stmt.Limit ?? defaultLimit);
        var table = await LineageHistoryRouting.BuildMissingMetadataTable(entries);

        if (stmt.IntoTable != null)
        {
            if (!context.Connections.ContainsKey(stmt.IntoTable))
                context.Connections[stmt.IntoTable] = new InMemoryDataSource();
            var dest = await context.ResolveDataSourceAsync(new TableReference(stmt.IntoTable));
            await dest.WriteBatches(new[] { table }.ToAsyncEnumerable());
        }
        else
        {
            context.LastResult = table;
            context.LastResultSets.Add(table);
            context.OnResultSet?.Invoke(table);
        }
    }
}

public class ShowProtectedDataStatementHandler : IStatementHandler
{
    public Type SupportedStatementType => typeof(ShowProtectedDataStatement);
    private readonly ILineageCatalogStore _catalog;
    private readonly IConfiguration? _config;

    public ShowProtectedDataStatementHandler(ILineageCatalogStore catalog, IConfiguration? config = null)
    {
        _catalog = catalog;
        _config = config;
    }

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (ShowProtectedDataStatement)statement;

        if (stmt.At != null)
        {
            await LineageHistoryRouting.RouteToRemoteAsync(stmt, stmt.At, context);
            return;
        }

        int defaultLimit = _config?.GetValue<int>("Engine:DefaultHistoryLimit") ?? 100;
        var limit = stmt.Limit ?? defaultLimit;
        var scanLimit = Math.Max(limit * 20, 1000);
        var protectedEntries = LineageProtectedData
            .FromHistory(await _catalog.GetRecentLineageAsync(scanLimit))
            .Take(limit);
        var table = await LineageHistoryRouting.BuildProtectedDataTable(protectedEntries);

        if (stmt.IntoTable != null)
        {
            if (!context.Connections.ContainsKey(stmt.IntoTable))
                context.Connections[stmt.IntoTable] = new InMemoryDataSource();
            var dest = await context.ResolveDataSourceAsync(new TableReference(stmt.IntoTable));
            await dest.WriteBatches(new[] { table }.ToAsyncEnumerable());
        }
        else
        {
            context.LastResult = table;
            context.LastResultSets.Add(table);
            context.OnResultSet?.Invoke(table);
        }
    }
}

internal static class LineageHistoryRouting
{
    internal static async Task RouteToRemoteAsync(Statement stmt, string atConn, IExecutionContext context)
    {
        IDataSource? conn = null;
        if (context.Connections.TryGetValue(atConn, out conn)) { }
        else conn = context.Connections.FirstOrDefault(c => c.Key.Equals(atConn, StringComparison.OrdinalIgnoreCase)).Value;

        if (conn == null)
        {
            var available = string.Join(", ", context.Connections.Keys);
            throw new ExecutionException($"Connection '{atConn}' not found in current session. Registered connections: [{available}]");
        }

        if (conn is not IPortalAdminConnection adminConn)
            throw new ExecutionException($"Connection '{atConn}' (Type: {conn.ConnectorType}) does not support orchestrator operations.");

        await adminConn.ExecuteAdminStatementAsync(stmt, context, context.CancellationToken);
    }

    internal static async Task<DataTable> BuildTable(IEnumerable<LineageHistoryEntry> entries)
    {
        var table = new DataTable();
        table.SetColumns(new[] { "Id", "RunAt", "JobName", "TargetTable", "TargetColumn", "SourceTables", "Operation", "Tags", "SourceFile", "Line" });
        foreach (var e in entries)
        {
            var row = new Row();
            row["Id"] = e.Id;
            row["RunAt"] = e.RunAt;
            row["JobName"] = e.JobName;
            row["TargetTable"] = e.TargetTable;
            row["TargetColumn"] = e.TargetColumn;
            row["SourceTables"] = string.Join(", ", e.SourceTables);
            row["Operation"] = e.Operation;
            row["Tags"] = System.Text.Json.JsonSerializer.Serialize(e.Tags);
            row["SourceFile"] = e.SourceFile;
            row["Line"] = e.Line;
            await table.AddRowAsync(row);
        }
        return table;
    }

    internal static async Task<DataTable> BuildMissingMetadataTable(IEnumerable<LineageMissingMetadataEntry> entries)
    {
        var table = new DataTable();
        table.SetColumns(new[] { "TargetTable", "TargetColumn", "MissingTags", "PresentTags", "RunAt", "JobName", "ScriptPath" });
        foreach (var e in entries)
        {
            var row = new Row();
            row["TargetTable"] = e.TargetTable;
            row["TargetColumn"] = e.TargetColumn;
            row["MissingTags"] = string.Join(", ", e.MissingTags.Select(t => "@" + t));
            row["PresentTags"] = System.Text.Json.JsonSerializer.Serialize(e.PresentTags);
            row["RunAt"] = e.RunAt;
            row["JobName"] = e.JobName;
            row["ScriptPath"] = e.ScriptPath;
            await table.AddRowAsync(row);
        }
        return table;
    }

    internal static async Task<DataTable> BuildProtectedDataTable(IEnumerable<ProtectedLineageHistoryEntry> entries)
    {
        var table = new DataTable();
        table.SetColumns(new[]
        {
            "Id", "RunAt", "JobName", "TargetTable", "TargetColumn", "SourceTables", "Operation",
            "ProtectionTags", "ProtectionReason", "Owner", "Steward", "Contact", "Domain",
            "Classification", "Quality", "Tags", "SourceFile", "Line"
        });
        foreach (var e in entries)
        {
            var row = new Row();
            row["Id"] = e.Id;
            row["RunAt"] = e.RunAt;
            row["JobName"] = e.JobName;
            row["TargetTable"] = e.TargetTable;
            row["TargetColumn"] = e.TargetColumn;
            row["SourceTables"] = string.Join(", ", e.SourceTables);
            row["Operation"] = e.Operation;
            row["ProtectionTags"] = string.Join(", ", e.ProtectionTags);
            row["ProtectionReason"] = e.ProtectionReason;
            row["Owner"] = e.Owner;
            row["Steward"] = e.Steward;
            row["Contact"] = e.Contact;
            row["Domain"] = e.Domain;
            row["Classification"] = e.Classification;
            row["Quality"] = e.Quality;
            row["Tags"] = System.Text.Json.JsonSerializer.Serialize(e.Tags);
            row["SourceFile"] = e.SourceFile;
            row["Line"] = e.Line;
            await table.AddRowAsync(row);
        }
        return table;
    }
}
