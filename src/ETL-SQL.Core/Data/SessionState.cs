using System;
using System.Collections.Generic;
using ETL_SQL.Data;

namespace ETL_SQL.Core.Data
{
    public class SessionState
    {
        public string SessionId { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime LastModifiedAt { get; set; }
        
        // Variables
        public Dictionary<string, object?> GlobalVariables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, VariableMetadata> GlobalMetadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        
        // Connections
        public List<ConnectionInfo> Connections { get; set; } = new();
        
        // Docker State
        public string? LastDockerConnectionString { get; set; }
        public Dictionary<string, string> DockerConnectionStrings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        
        // Temp Tables
        public List<TempTableInfo> TempTables { get; set; } = new();

        // Lineage
        public List<LineageEntry> LineageEntries { get; set; } = new();

        // Script context for recovery
        public string? LastScriptSource { get; set; }

        // Auditing
        public string? OwnerUser { get; set; }
        public string? OwnerMachine { get; set; }
    }

    public class ConnectionInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string ConnectionString { get; set; } = string.Empty;
        public Dictionary<string, string> Options { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public class TempTableInfo
    {
        public string Name { get; set; } = string.Empty;
        public string DataFilePath { get; set; } = string.Empty;
        public List<ColumnDefinition> Columns { get; set; } = new();
        public List<TableConstraintInfo> Constraints { get; set; } = new();
    }
}
