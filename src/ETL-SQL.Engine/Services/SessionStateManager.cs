using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ConnectionInfo = ETL_SQL.Core.Data.ConnectionInfo;
using ETL_SQL.Data;
using System.Security.Cryptography;
using System.Text;

namespace ETL_SQL.Engine.Services
{
    /// <summary>
    /// Manages saving and loading of session state to allow ad-hoc development (Run Selection)
    /// to maintain state across multiple process runs.
    /// </summary>
    public class SessionStateManager(ILogger logger, string? customSessionDir = null)
    {
        private readonly string _sessionRoot = InitializeSessionRoot(customSessionDir);
        private readonly ILogger _logger = logger;
        private const string SessionFileExtension = ".etlsession";
        private const string RecoveryManifestExtension = ".recovery.json";

        private static string InitializeSessionRoot(string? customDir)
        {
            var root = customDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ETL-SQL", "Sessions");
            if (!Directory.Exists(root)) Directory.CreateDirectory(root);
            return root;
        }

        private string GetSessionFilePath(string sessionId) => Path.Combine(_sessionRoot, sessionId + SessionFileExtension);
        private string GetRecoveryFilePath(string sessionId) => Path.Combine(_sessionRoot, sessionId + RecoveryManifestExtension);
        private string GetTempTableDir(string sessionId) => Path.Combine(_sessionRoot, sessionId + "_temp");

        public string GetMachineKey()
        {
            var rawKey = $"{Environment.MachineName}:{Environment.UserName}";
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawKey));
            return Convert.ToBase64String(bytes);
        }

        /// <summary>Saves the current evaluator state to a session file.</summary>
        public async Task SaveSession(string sessionId, Evaluator evaluator, string? scriptSource = null)
        {
            var state = new SessionState
            {
                SessionId = sessionId,
                CreatedAt = DateTime.Now, // Should preserve if exists
                LastModifiedAt = DateTime.Now,
                LastScriptSource = scriptSource,
                LastDockerConnectionString = evaluator.DockerManager.LastConnectionString
            };

            // 1. Capture Variables (only primitives for now)
            var (vars, meta) = evaluator.GetGlobalState();
            foreach (var kvp in vars)
            {
                if (kvp.Value == null || IsSerializable(kvp.Value))
                {
                    state.GlobalVariables[kvp.Key] = kvp.Value;
                }
            }
            state.GlobalMetadata = meta;

            // 2. Capture Connections
            foreach (var conn in evaluator.Connections)
            {
                if (conn.Value is IDataSource ds) // Capture all IDataSource implementations
                {
                    state.Connections.Add(new ConnectionInfo
                    {
                        Name = conn.Key,
                        Type = (ds as IDatabaseSource)?.Dialect ?? ds.GetType().Name.Replace("DataSource", "").ToUpperInvariant(),
                        ConnectionString = (ds as IDatabaseSource)?.ConnectionString ?? ds.Path,
                        Options = ds.Options != null ? new Dictionary<string, string>(ds.Options) : new Dictionary<string, string>()
                    });
                }
            }

            // 3. Capture Docker connection strings (from static manager)
            // We'll need to expose a way to get all connection strings from DockerManager.
            // For now, at least save the last one.
            
            // 4. Capture Temp Tables (#tables)
            var tempDir = GetTempTableDir(sessionId);
            if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);

            foreach (var conn in evaluator.Connections)
            {
                if (conn.Key.StartsWith("#") && conn.Value is InMemoryDataSource mem)
                {
                    var dataFile = Path.Combine(tempDir, conn.Key.Replace("#", "temp_") + ".json");
                    var info = new TempTableInfo
                    {
                        Name = conn.Key,
                        DataFilePath = dataFile,
                        Columns = mem.Schema.Values.ToList(),
                        Constraints = MapConstraints(mem.TableConstraints)
                    };
                    
                    // Simple JSON serialization of the data table
                    var batches = await mem.ReadBatches().ToListAsync();
                    int totalSavedRows = 0;
                    if (batches.Count > 0)
                    {
                        // Columns property is already set correctly on 'info' at line 100
                        
                        var schemaCols = mem.Schema.Keys.ToList();
                        var allRows = new List<Dictionary<string, object?>>();
                        foreach (var batch in batches)
                        {
                            foreach (var row in batch.Rows)
                            {
                                var rowDict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                                foreach (var col in schemaCols) rowDict[col] = row[col];
                                allRows.Add(rowDict);
                            }
                        }
                        totalSavedRows = allRows.Count;

                        if (totalSavedRows > 0)
                        {
                            string json = JsonSerializer.Serialize(allRows);
                            
                            // Encrypt before saving if possible
                            var password = evaluator.ScriptPassword ?? GetMachineKey();
                            File.WriteAllText(dataFile, CryptoUtils.Encrypt(json, password));
                            _logger.Debug($"[SESSION] Persisted {totalSavedRows} rows for temp table {conn.Key} to {Path.GetFileName(dataFile)}");
                        }
                    }
                    
                    if (totalSavedRows == 0)
                    {
                        _logger.Debug($"[SESSION] Temp table {conn.Key} is empty; no data file created.");
                    }
                    
                    state.TempTables.Add(info);
                }
            }

            // 5. Capture Lineage
            state.LineageEntries = evaluator.LineageTracker.GetFullLineage().ToList();
            
            // 6. Encrypt and save full state
            string fullJson = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            string sessionFile = GetSessionFilePath(sessionId);
            string masterPassword = evaluator.ScriptPassword ?? GetMachineKey();
            
            File.WriteAllText(sessionFile, CryptoUtils.Encrypt(fullJson, masterPassword));

            // 6. Save unencrypted recovery manifest (Bug # recovery focus)
            var manifest = new
            {
                SessionId = sessionId,
                LastModified = state.LastModifiedAt,
                ScriptSource = scriptSource,
                TempTables = state.TempTables.Select(t => t.Name).ToList(),
                Variables = state.GlobalVariables.Keys.ToList()
            };
            File.WriteAllText(GetRecoveryFilePath(sessionId), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        }

        private List<TableConstraintInfo> MapConstraints(IEnumerable<TableConstraint> constraints)
        {
            var result = new List<TableConstraintInfo>();
            foreach (var tc in constraints)
            {
                var info = new TableConstraintInfo { Name = tc.ConstraintName };
                if (tc is TablePrimaryKeyConstraint pk)
                {
                    info.Type = ConstraintType.PrimaryKey;
                    info.Columns = pk.Columns;
                }
                else if (tc is TableUniqueConstraint uk)
                {
                    info.Type = ConstraintType.Unique;
                    info.Columns = uk.Columns;
                }
                else if (tc is TableCheckConstraint c)
                {
                    info.Type = ConstraintType.Check;
                    info.Expression = c.Expression;
                }
                else if (tc is TableForeignKeyConstraint fk)
                {
                    info.Type = ConstraintType.ForeignKey;
                    info.Columns = fk.Columns;
                    info.ForeignKey = fk.Reference;
                }
                result.Add(info);
            }
            return result;
        }

        private bool IsSerializable(object value)
        {
            return value is string or int or long or decimal or double or bool or DateTime;
        }

        /// <summary>Loads existing session state from disk.</summary>
        public async Task<SessionState?> LoadSession(string sessionId, string? password = null)
        {
            _logger.Debug("[SESSION_MANAGER_ENTER] LoadSession method entered.");
            
            string sessionFile = GetSessionFilePath(sessionId);
            if (!File.Exists(sessionFile)) return null;

            try
            {
                _logger.Debug($"[SESSION_READ_FILE] Reading {sessionFile}...");
                string encryptedJson = File.ReadAllText(sessionFile);
                
                _logger.Debug("[SESSION_DERIVE_KEY] Deriving decryption key...");
                string masterPassword = password ?? GetMachineKey();
                
                _logger.Debug("[SESSION_DECRYPT] Decrypting state...");
                string plainJson = CryptoUtils.Decrypt(encryptedJson, masterPassword);
                
                _logger.Debug("[SESSION_DESERIALIZE] Deserializing JSON...");
                return JsonSerializer.Deserialize<SessionState>(plainJson);
            }
            catch
            {
                // Decryption failure (wrong password or machine key changed)
                return null;
            }
        }

        /// <summary>Clears session files from disk.</summary>
        public void ClearSession(string sessionId)
        {
            string sessionFile = GetSessionFilePath(sessionId);
            if (File.Exists(sessionFile)) File.Delete(sessionFile);

            string recoveryFile = GetRecoveryFilePath(sessionId);
            if (File.Exists(recoveryFile)) File.Delete(recoveryFile);

            string tempDir = GetTempTableDir(sessionId);
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }

        /// <summary>Deletes stale session files older than the specified duration.</summary>
        public void ReapStaleSessions(TimeSpan maxAge)
        {
            var now = DateTime.Now;
            foreach (var file in Directory.GetFiles(_sessionRoot, "*" + SessionFileExtension))
            {
                if (now - File.GetLastWriteTime(file) > maxAge)
                {
                    try
                    {
                        string sessionId = Path.GetFileNameWithoutExtension(file);
                        ClearSession(sessionId);
                    }
                    catch { }
                }
            }
        }
    }
}
