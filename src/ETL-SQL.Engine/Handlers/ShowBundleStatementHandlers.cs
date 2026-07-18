using System;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers;

internal static class ShowBundleTableWriter
{
    public static async Task<bool> TryExecuteRemoteAsync(Statement statement, string? at, IExecutionContext context)
    {
        if (at == null) return false;
        var conn = context.Connections.FirstOrDefault(c => c.Key.Equals(at, StringComparison.OrdinalIgnoreCase)).Value;
        if (conn == null)
            throw new ExecutionException($"Connection '{at}' not found in current session.");
        if (conn is not IPortalAdminConnection adminConn)
            throw new ExecutionException($"Connection '{at}' (Type: {conn.ConnectorType}) does not support orchestrator operations.");
        await adminConn.ExecuteAdminStatementAsync(statement, context, context.CancellationToken);
        return true;
    }

    public static async Task WriteAsync(DataTable table, string? intoTable, IExecutionContext context)
    {
        if (intoTable != null)
        {
            if (!context.Connections.ContainsKey(intoTable))
                context.Connections[intoTable] = new InMemoryDataSource();
            var destination = await context.ResolveDataSourceAsync(new TableReference(intoTable));
            await destination.WriteBatches(new[] { table }.ToAsyncEnumerable());
            return;
        }

        if (table.Rows.Count == 0)
            context.Log("0 rows returned.", ConsoleColor.Cyan);
        context.LastResult = table;
        context.LastResultSets.Add(table);
        context.OnResultSet?.Invoke(table);
    }

    public static void AddBundleVersionColumns(DataTable table)
    {
        table.AddColumn("BundleName");
        table.AddColumn("Version");
        table.AddColumn("EntryPath");
        table.AddColumn("ContentHash");
        table.AddColumn("PublishedAt");
        table.AddColumn("Publisher");
        table.AddColumn("Description");
    }

    public static async Task AddVersionRowAsync(DataTable table, BundleVersionInfo version)
    {
        var row = new Row();
        row["BundleName"] = version.BundleName;
        row["Version"] = version.Version;
        row["EntryPath"] = version.EntryPath;
        row["ContentHash"] = version.ContentHash;
        row["PublishedAt"] = version.PublishedAt;
        row["Publisher"] = version.Publisher;
        row["Description"] = version.Description;
        await table.AddRowAsync(row);
    }
}

public class ShowPublishedBundlesStatementHandler(IBundleStore store) : IStatementHandler
{
    public Type SupportedStatementType => typeof(ShowPublishedBundlesStatement);

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (ShowPublishedBundlesStatement)statement;
        if (await ShowBundleTableWriter.TryExecuteRemoteAsync(stmt, stmt.At, context)) return;
        var table = new DataTable();
        ShowBundleTableWriter.AddBundleVersionColumns(table);
        foreach (var bundle in await store.GetBundlesAsync())
            await ShowBundleTableWriter.AddVersionRowAsync(table, bundle);
        await ShowBundleTableWriter.WriteAsync(table, stmt.IntoTable, context);
    }
}

public class ShowBundleVersionsStatementHandler(IBundleStore store) : IStatementHandler
{
    public Type SupportedStatementType => typeof(ShowBundleVersionsStatement);

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (ShowBundleVersionsStatement)statement;
        if (await ShowBundleTableWriter.TryExecuteRemoteAsync(stmt, stmt.At, context)) return;
        var table = new DataTable();
        ShowBundleTableWriter.AddBundleVersionColumns(table);
        foreach (var version in await store.GetVersionsAsync(stmt.BundleName))
            await ShowBundleTableWriter.AddVersionRowAsync(table, version);
        await ShowBundleTableWriter.WriteAsync(table, stmt.IntoTable, context);
    }
}

public class ShowBundleFilesStatementHandler(IBundleStore store) : IStatementHandler
{
    public Type SupportedStatementType => typeof(ShowBundleFilesStatement);

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (ShowBundleFilesStatement)statement;
        if (await ShowBundleTableWriter.TryExecuteRemoteAsync(stmt, stmt.At, context)) return;
        var files = (await store.GetFilesAsync(stmt.BundleName, stmt.Version)).ToList();
        if (files.Count == 0 && await store.GetVersionAsync(stmt.BundleName, stmt.Version) == null)
            throw new ExecutionException($"Bundle '{stmt.BundleName}' version {stmt.Version} was not found.");

        var table = new DataTable();
        table.AddColumn("BundleName");
        table.AddColumn("Version");
        table.AddColumn("VirtualPath");
        table.AddColumn("ContentHash");
        table.AddColumn("SizeBytes");
        table.AddColumn("ContentType");
        foreach (var file in files)
        {
            var row = new Row();
            row["BundleName"] = file.BundleName;
            row["Version"] = file.Version;
            row["VirtualPath"] = file.VirtualPath;
            row["ContentHash"] = file.ContentHash;
            row["SizeBytes"] = file.SizeBytes;
            row["ContentType"] = file.ContentType;
            await table.AddRowAsync(row);
        }
        await ShowBundleTableWriter.WriteAsync(table, stmt.IntoTable, context);
    }
}

public class ShowBundleDependenciesStatementHandler(IBundleStore store) : IStatementHandler
{
    public Type SupportedStatementType => typeof(ShowBundleDependenciesStatement);

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (ShowBundleDependenciesStatement)statement;
        if (await ShowBundleTableWriter.TryExecuteRemoteAsync(stmt, stmt.At, context)) return;
        var deps = (await store.GetDependenciesAsync(stmt.BundleName, stmt.Version)).ToList();
        if (deps.Count == 0 && await store.GetVersionAsync(stmt.BundleName, stmt.Version) == null)
            throw new ExecutionException($"Bundle '{stmt.BundleName}' version {stmt.Version} was not found.");

        var table = new DataTable();
        table.AddColumn("BundleName");
        table.AddColumn("Version");
        table.AddColumn("FromPath");
        table.AddColumn("ToPath");
        foreach (var dep in deps)
        {
            var row = new Row();
            row["BundleName"] = dep.BundleName;
            row["Version"] = dep.Version;
            row["FromPath"] = dep.FromPath;
            row["ToPath"] = dep.ToPath;
            await table.AddRowAsync(row);
        }
        await ShowBundleTableWriter.WriteAsync(table, stmt.IntoTable, context);
    }
}
