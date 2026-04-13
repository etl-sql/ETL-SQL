using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Threading;
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
        public string SessionRoot { get; } = InitializeSessionRoot(customSessionDir);
        private readonly ILogger _logger = logger;
        private const string SessionFileExtension = ".etlsession";
        private const string RecoveryManifestExtension = ".recovery.json";
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _sessionLocks = new();

        private SemaphoreSlim GetSessionLock(string sessionId) => _sessionLocks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));

        private static string InitializeSessionRoot(string? customDir)
        {
            var root = customDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ETL-SQL", "Sessions");
            
            // Security Hardening: Validate the session root if it's custom
            if (customDir != null)
            {
                var fullPath = Path.GetFullPath(root);
                var pathRoot = Path.GetPathRoot(fullPath);
                if (string.Equals(fullPath, pathRoot, StringComparison.OrdinalIgnoreCase))
                    throw new UnauthorizedAccessException("Session storage cannot be placed at the root directory.");

                string[] blocked = { ".git", ".vscode", "Windows", "System32" };
                if (blocked.Any(b => fullPath.Contains(Path.DirectorySeparatorChar + b + Path.DirectorySeparatorChar) || fullPath.EndsWith(Path.DirectorySeparatorChar + b)))
                    throw new UnauthorizedAccessException($"Session storage cannot be placed in protected directory: {fullPath}");
            }

            if (!Directory.Exists(root)) Directory.CreateDirectory(root);
            return root;
        }

        private string GetSessionFilePath(string sessionId) => Path.Combine(SessionRoot, sessionId + SessionFileExtension);
        private string GetRecoveryFilePath(string sessionId) => Path.Combine(SessionRoot, sessionId + RecoveryManifestExtension);
        private string GetTempTableDir(string sessionId) => Path.Combine(SessionRoot, sessionId + "_temp");

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
                            
                            // Hardware-bound encryption for temp data
                            var entropy = GetMachineKey();
                            File.WriteAllText(dataFile, CryptoUtils.Protect(json, entropy));
                            _logger.Debug("[SESSION] Persisted {RowCount} rows for temp table {TableName} to {FileName} (Machine-Locked)", totalSavedRows, conn.Key, Path.GetFileName(dataFile));
                        }
                    }
                    
                    if (totalSavedRows == 0)
                    {
                        _logger.Debug("[SESSION] Temp table {TableName} is empty; no data file created.", conn.Key);
                    }
                    
                    state.TempTables.Add(info);
                }
            }

            // 5. Capture Lineage
            state.LineageEntries = evaluator.LineageTracker.GetFullLineage().ToList();
            
            // 6. Protect and save full state (Zero-password, Machine-Bound)
            string fullJson = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            string sessionFile = GetSessionFilePath(sessionId);
            string entropyKey = GetMachineKey();
            
            File.WriteAllText(sessionFile, CryptoUtils.Protect(fullJson, entropyKey));

            var manifest = new
            {
                SessionId = sessionId,
                LastModified = state.LastModifiedAt,
                ScriptSource = scriptSource,
                TempTables = state.TempTables.Select(t => t.Name).ToList(),
                Variables = state.GlobalVariables.Keys.ToList()
            };

            var sessionLock = GetSessionLock(sessionId);
            await sessionLock.WaitAsync();
            try
            {
                await WriteAtomicAsync(sessionFile, CryptoUtils.Protect(fullJson, entropyKey));
                await WriteAtomicAsync(GetRecoveryFilePath(sessionId), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
            }
            finally
            {
                sessionLock.Release();
            }
        }

        private async Task WriteAtomicAsync(string path, string content)
        {
            var tmpPath = path + ".tmp";
            try
            {
                await File.WriteAllTextAsync(tmpPath, content);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmpPath, path);
            }
            finally
            {
                if (File.Exists(tmpPath))
                {
                    try { File.Delete(tmpPath); } catch { }
                }
            }
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

        /// <summary>Loads existing session state from disk using machine-bound decryption.</summary>
        public async Task<SessionState?> LoadSession(string sessionId, string? legacyPassword = null)
        {
            _logger.Debug("[SESSION_MANAGER_ENTER] LoadSession method entered.");
            
            string sessionFile = GetSessionFilePath(sessionId);
            if (!File.Exists(sessionFile)) return null;

            try
            {
                _logger.Debug("[SESSION_READ_FILE] Reading {SessionFile}...", sessionFile);
                string protectedJson = File.ReadAllText(sessionFile);
                
                _logger.Debug("[SESSION_UNPROTECT] Unprotecting state using OS context...");
                string entropy = GetMachineKey();
                
                string plainJson = CryptoUtils.Unprotect(protectedJson, entropy);
                
                _logger.Debug("[SESSION_DESERIALIZE] Deserializing JSON...");
                return JsonSerializer.Deserialize<SessionState>(plainJson);
            }
            catch (CryptographicException)
            {
                _logger.Warning("[SESSION_SECURITY] Failed to resume session {SessionId}. The session file is locked to a different machine or user account.", sessionId);
                return null;
            }
            catch (Exception ex)
            {
                _logger.Error("[SESSION_ERROR] Unexpected error loading session: {Message}", ex, ex.Message);
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
            foreach (var file in Directory.GetFiles(SessionRoot, "*" + SessionFileExtension))
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
