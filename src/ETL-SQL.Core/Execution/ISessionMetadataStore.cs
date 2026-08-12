using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;

namespace ETL_SQL.Core.Execution;

/// <summary>
/// Defines operations for persisting session state metadata to high-performance storage.
/// Supports variables, lineage, and temp table rehydration.
/// </summary>
public interface ISessionMetadataStore : IDisposable
{
    Task InitializeAsync();

    Task SaveVariablesAsync(IDictionary<string, object?> variables, IDictionary<string, VariableMetadata> metadata);
    Task<(Dictionary<string, object?>, Dictionary<string, VariableMetadata>)> LoadVariablesAsync();

    Task SaveLineageAsync(IEnumerable<LineageEntry> entries);
    Task<IEnumerable<LineageEntry>> LoadLineageAsync();

    Task<IEnumerable<SavedTempTable>> LoadAllTempTablesAsync();
    Task SaveTempTablesAsync(IEnumerable<SavedTempTable> tables);

    Task SaveConnectionsAsync(IEnumerable<ETL_SQL.Core.Data.ConnectionInfo> connections);
    Task<IEnumerable<ETL_SQL.Core.Data.ConnectionInfo>> LoadConnectionsAsync();




    Task SaveDockerStateAsync(string? lastConn, IDictionary<string, string> connStrings);
    Task<(string? LastConn, Dictionary<string, string> ConnStrings)> LoadDockerStateAsync();
}

public interface ISessionMetadataStoreFactory
{
    ISessionMetadataStore Create(
        string sessionId,
        string sessionRoot,
        string machineKeyEntropy,
        string? keyScope = null);
}

public record SavedTempTable(string TableName, List<ColumnDefinition> Schema, List<string> ChunkNames);
