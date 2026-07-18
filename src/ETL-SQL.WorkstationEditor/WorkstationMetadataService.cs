using ETL_SQL.Analysis.Linting;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;

namespace ETL_SQL.WorkstationEditor;

public sealed class WorkstationMetadataService(IMetadataManager metadata)
{
    public async Task<Script> RegisterScriptMetadataAsync(string scriptText, string documentUri)
    {
        var tokens = new Lexer(scriptText).Tokenize();
        var script = new Parser(tokens, scriptText).Parse();

        var activeConnections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var activeTempTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var statement in script.Statements)
            await DiscoverMetadataRecursiveAsync(statement, documentUri, activeConnections, activeTempTables);

        metadata.CleanUpDocumentConnectionsAndTempTables(documentUri, activeConnections, activeTempTables);
        return script;
    }

    public IMetadataProvider CreateLintMetadataProvider(string documentUri) =>
        new DocumentMetadataProvider(metadata, documentUri);

    private async Task DiscoverMetadataRecursiveAsync(
        Statement statement,
        string documentUri,
        HashSet<string> activeConnections,
        HashSet<string> activeTempTables)
    {
        if (statement is CreateConnectionStatement connection)
        {
            var connectionString = connection.TargetExpression?.ToSql() ?? string.Empty;
            connectionString = connectionString.Trim('\'', '\"', '(', ')', ' ');
            metadata.RegisterDocumentConnection(
                documentUri,
                connection.ConnectionName,
                connection.ConnectionType ?? "UNKNOWN",
                connectionString);
            activeConnections.Add(connection.ConnectionName);
        }
        else if (statement is CreateTableStatement createTable)
        {
            var tableName = createTable.TargetTable.TableName;
            if (tableName.StartsWith("#", StringComparison.Ordinal))
            {
                metadata.RegisterTempTable(documentUri, tableName, createTable.Columns.Select(c => c.ColumnName).ToList());
                activeTempTables.Add(tableName);
            }
        }
        else if (statement is SelectStatement select && select.IntoTable != null)
        {
            var tableName = select.IntoTable.TableName;
            if (tableName.StartsWith("#", StringComparison.Ordinal))
            {
                var columns = new List<string>();
                var hasStar = select.Columns.Any(c => c.Expression is IdentifierExpression id && id.Name == "*");
                if (hasStar && select.FromTable != null)
                {
                    var connectionName = select.FromTable.ConnectionName
                        ?? metadata.GetConnections(documentUri).FirstOrDefault(c => c.IsDocument)?.Name
                        ?? "DEFAULT";
                    columns.AddRange(await metadata.GetColumnsAsync(connectionName, select.FromTable.TableName, documentUri));
                }
                else
                {
                    columns.AddRange(select.Columns.Select(c => c.Alias ?? c.Expression.ToSql().Split('.').Last().Trim('[', ']', '"', '\'')));
                }

                metadata.RegisterTempTable(documentUri, tableName, columns.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
                activeTempTables.Add(tableName);
            }
        }
        else if (statement is DockerStatement docker && !string.IsNullOrEmpty(docker.Alias))
        {
            metadata.RegisterDocumentConnection(documentUri, docker.Alias, "DOCKER", docker.ImageName.ToSql());
            activeConnections.Add(docker.Alias);
        }
        else if (statement is ExecuteRemoteBlockStatement remote)
        {
            await DiscoverMetadataRecursiveAsync(remote.Body, documentUri, activeConnections, activeTempTables);
        }
        else if (statement is ExecutePushdownStatement pushdown)
        {
            if (pushdown.IntoTable != null && pushdown.IntoTable.TableName.StartsWith("#", StringComparison.Ordinal))
            {
                metadata.RegisterTempTable(documentUri, pushdown.IntoTable.TableName, []);
                activeTempTables.Add(pushdown.IntoTable.TableName);
            }
        }
        else if (statement is BlockStatement block)
        {
            foreach (var child in block.Statements)
                await DiscoverMetadataRecursiveAsync(child, documentUri, activeConnections, activeTempTables);
        }
        else if (statement is IfStatement ifStatement)
        {
            await DiscoverMetadataRecursiveAsync(ifStatement.IfBody, documentUri, activeConnections, activeTempTables);
            if (ifStatement.ElseIfClauses != null)
            {
                foreach (var clause in ifStatement.ElseIfClauses)
                    await DiscoverMetadataRecursiveAsync(clause.Body, documentUri, activeConnections, activeTempTables);
            }
            if (ifStatement.ElseBody != null)
                await DiscoverMetadataRecursiveAsync(ifStatement.ElseBody, documentUri, activeConnections, activeTempTables);
        }
        else if (statement is WhileStatement whileStatement)
        {
            await DiscoverMetadataRecursiveAsync(whileStatement.Body, documentUri, activeConnections, activeTempTables);
        }
        else if (statement is ForStatement forStatement)
        {
            await DiscoverMetadataRecursiveAsync(forStatement.Body, documentUri, activeConnections, activeTempTables);
        }
        else if (statement is ForeachStatement foreachStatement)
        {
            await DiscoverMetadataRecursiveAsync(foreachStatement.Body, documentUri, activeConnections, activeTempTables);
        }
        else if (statement is TryCatchStatement tryCatch)
        {
            await DiscoverMetadataRecursiveAsync(tryCatch.TryBody, documentUri, activeConnections, activeTempTables);
            await DiscoverMetadataRecursiveAsync(tryCatch.CatchBody, documentUri, activeConnections, activeTempTables);
        }
    }

    private sealed class DocumentMetadataProvider(IMetadataManager metadataManager, string documentUri) : IMetadataProvider
    {
        public Task<IEnumerable<string>> GetTablesAsync(string connectionName) =>
            metadataManager.GetTablesAsync(connectionName, documentUri);

        public Task<IEnumerable<string>> GetColumnsAsync(string connectionName, string tableName) =>
            metadataManager.GetColumnsAsync(connectionName, tableName, documentUri);

        public IEnumerable<string> GetConnections() =>
            metadataManager.GetConnections(documentUri).Select(c => c.Name);

        public string? GetConnectionType(string connectionName) =>
            metadataManager.GetConnectionType(connectionName, documentUri);
    }
}
