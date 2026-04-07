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
    public class SessionStateManager
    {
        private readonly string _sessionRoot;
        private const string SessionFileExtension = ".etlsession";
        private const string RecoveryManifestExtension = ".recovery.json";

        public SessionStateManager(string? customSessionDir = null)
        {
            _sessionRoot = customSessionDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ETL-SQL", "Sessions");
            if (!Directory.Exists(_sessionRoot)) Directory.CreateDirectory(_sessionRoot);
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
                        DataFilePath = dataFile
                    };
                    
                    // Simple JSON serialization of the data table
                    var batches = await mem.ReadBatches().ToListAsync();
                    if (batches.Count > 0)
                    {
                        var allColumnNames = batches.SelectMany(b => b.ColumnNames).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                        info.Columns = allColumnNames;
                        
                        var allRows = batches.SelectMany(b => b.Rows.Select(r => r.Columns)).ToList();
                        string json = JsonSerializer.Serialize(allRows);
                        
                        // Encrypt before saving if possible
                        var password = evaluator.ScriptPassword ?? GetMachineKey();
                        File.WriteAllText(dataFile, CryptoUtils.Encrypt(json, password));
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

        private bool IsSerializable(object value)
        {
            return value is string or int or long or decimal or double or bool or DateTime;
        }

        /// <summary>Loads existing session state from disk.</summary>
        public async Task<SessionState?> LoadSession(string sessionId, string? password = null)
        {
            Console.Error.WriteLine("[SESSION_MANAGER_ENTER] LoadSession method entered.");
            Console.Error.Flush();
            
            string sessionFile = GetSessionFilePath(sessionId);
            if (!File.Exists(sessionFile)) return null;

            try
            {
                Console.Error.WriteLine($"[SESSION_READ_FILE] Reading {sessionFile}...");
                Console.Error.Flush();
                string encryptedJson = File.ReadAllText(sessionFile);
                
                Console.Error.WriteLine("[SESSION_DERIVE_KEY] Deriving decryption key...");
                Console.Error.Flush();
                string masterPassword = password ?? GetMachineKey();
                
                Console.Error.WriteLine("[SESSION_DECRYPT] Decrypting state...");
                Console.Error.Flush();
                string plainJson = CryptoUtils.Decrypt(encryptedJson, masterPassword);
                
                Console.Error.WriteLine("[SESSION_DESERIALIZE] Deserializing JSON...");
                Console.Error.Flush();
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
