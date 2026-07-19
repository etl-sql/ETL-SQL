using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ETL_SQL.Core.Services;

/// <summary>
/// Walks a parsed script and registers the metadata an editor needs for autocomplete and the
/// schema/session explorers: document-scoped connections and temp tables with their columns.
/// </summary>
/// <remarks>
/// Shared by every editor host so they agree on what a script declares. Hosts differ on whether
/// a script may declare its own connections — see <see cref="RegisterConnections"/>.
/// </remarks>
public sealed class ScriptMetadataDiscovery(IMetadataManager metadata)
{
    /// <summary>
    /// Whether <c>CREATE CONNECTION</c> in the script registers a usable document connection.
    /// True for local hosts (Workstation, VS Code) where the script owns its connections. False
    /// for the Portal, where connections come from the ACL-gated shared catalog and a
    /// script-declared one must not become introspectable.
    /// </summary>
    public bool RegisterConnections { get; init; } = true;

    /// <summary>
    /// Registers everything the script declares, then prunes entries that disappeared since the
    /// last pass (avoids the flush-and-rebuild gap where autocomplete briefly loses state).
    /// </summary>
    public async Task DiscoverAsync(Script script, string documentUri)
    {
        var activeConnections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var activeTempTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var statement in script.Statements)
            await DiscoverAsync(statement, documentUri, activeConnections, activeTempTables);

        metadata.CleanUpDocumentConnectionsAndTempTables(documentUri, activeConnections, activeTempTables);
    }

    private async Task DiscoverAsync(
        Statement statement,
        string documentUri,
        HashSet<string> activeConnections,
        HashSet<string> activeTempTables)
    {
        switch (statement)
        {
            case CreateConnectionStatement connection when RegisterConnections:
            {
                var connectionString = connection.TargetExpression?.ToSql() ?? string.Empty;
                connectionString = connectionString.Trim('\'', '\"', '(', ')', ' ');
                metadata.RegisterDocumentConnection(
                    documentUri,
                    connection.ConnectionName,
                    connection.ConnectionType ?? "UNKNOWN",
                    connectionString);
                activeConnections.Add(connection.ConnectionName);
                break;
            }

            case CreateTableStatement createTable when IsTempTable(createTable.TargetTable.TableName):
            {
                var tableName = createTable.TargetTable.TableName;
                metadata.RegisterTempTable(
                    documentUri,
                    tableName,
                    createTable.Columns.Select(c => new ColumnMetadata(c.ColumnName, c.DataType)).ToList());
                activeTempTables.Add(tableName);
                break;
            }

            case SelectStatement select when select.IntoTable != null && IsTempTable(select.IntoTable.TableName):
            {
                var tableName = select.IntoTable.TableName;
                metadata.RegisterTempTable(
                    documentUri,
                    tableName,
                    await ResolveProjectedColumnsAsync(select, documentUri));
                activeTempTables.Add(tableName);
                break;
            }

            case DockerStatement docker when RegisterConnections && !string.IsNullOrEmpty(docker.Alias):
                metadata.RegisterDocumentConnection(documentUri, docker.Alias, "DOCKER", docker.ImageName.ToSql());
                activeConnections.Add(docker.Alias);
                break;

            case ExecutePushdownStatement pushdown
                when pushdown.IntoTable != null && IsTempTable(pushdown.IntoTable.TableName):
                metadata.RegisterTempTable(documentUri, pushdown.IntoTable.TableName, new List<ColumnMetadata>());
                activeTempTables.Add(pushdown.IntoTable.TableName);
                break;

            case ExecuteRemoteBlockStatement remote:
                await DiscoverAsync(remote.Body, documentUri, activeConnections, activeTempTables);
                break;

            case BlockStatement block:
                foreach (var child in block.Statements)
                    await DiscoverAsync(child, documentUri, activeConnections, activeTempTables);
                break;

            case IfStatement ifStatement:
                await DiscoverAsync(ifStatement.IfBody, documentUri, activeConnections, activeTempTables);
                foreach (var clause in ifStatement.ElseIfClauses ?? [])
                    await DiscoverAsync(clause.Body, documentUri, activeConnections, activeTempTables);
                if (ifStatement.ElseBody != null)
                    await DiscoverAsync(ifStatement.ElseBody, documentUri, activeConnections, activeTempTables);
                break;

            case WhileStatement whileStatement:
                await DiscoverAsync(whileStatement.Body, documentUri, activeConnections, activeTempTables);
                break;

            case ForStatement forStatement:
                await DiscoverAsync(forStatement.Body, documentUri, activeConnections, activeTempTables);
                break;

            case ForeachStatement foreachStatement:
                await DiscoverAsync(foreachStatement.Body, documentUri, activeConnections, activeTempTables);
                break;

            case TryCatchStatement tryCatch:
                await DiscoverAsync(tryCatch.TryBody, documentUri, activeConnections, activeTempTables);
                await DiscoverAsync(tryCatch.CatchBody, documentUri, activeConnections, activeTempTables);
                break;
        }
    }

    /// <summary>
    /// Names the temp table's columns, inheriting the source column's type for a bare column
    /// reference. Computed expressions stay ANY rather than claiming a type we haven't inferred.
    /// </summary>
    private async Task<List<ColumnMetadata>> ResolveProjectedColumnsAsync(SelectStatement select, string documentUri)
    {
        var sourceTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (select.FromTable != null)
        {
            var connectionName = select.FromTable.ConnectionName
                ?? metadata.GetConnections(documentUri).FirstOrDefault(c => c.IsDocument)?.Name
                ?? "DEFAULT";
            foreach (var column in await metadata.GetColumnDetailsAsync(connectionName, select.FromTable.TableName, documentUri))
                sourceTypes[column.Name] = column.DataType;
        }

        var hasStar = select.Columns.Any(c => c.Expression is IdentifierExpression { Name: "*" });
        if (hasStar && select.FromTable != null)
            return sourceTypes.Select(kvp => new ColumnMetadata(kvp.Key, kvp.Value)).ToList();

        var columns = new List<ColumnMetadata>();
        foreach (var column in select.Columns)
        {
            var name = column.Alias ?? column.Expression.ToSql().Split('.').Last().Trim('[', ']', '"', '\'');
            var isPlainReference = column.Alias is null && column.Expression is IdentifierExpression;
            columns.Add(new ColumnMetadata(
                name,
                isPlainReference && sourceTypes.TryGetValue(name, out var type) ? type : "ANY"));
        }

        return columns.DistinctBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsTempTable(string tableName) => tableName.StartsWith('#');
}
