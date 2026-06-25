using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Execution;
using ETL_SQL.Data;
using ConnectionInfo = ETL_SQL.Core.Data.ConnectionInfo;

namespace ETL_SQL.Engine.Services
{
    /// <summary>
    /// Manages saving and loading of session state to allow ad-hoc development (Run Selection)
    /// to maintain state across multiple process runs. Optimized with SQLite and Persistent Chunks.
    /// </summary>
    public class SessionStateManager : ISessionStateManager
    {
        public string SessionRoot { get; }
        private readonly ILogger _logger;
        private readonly ETL_SQL.Services.SecurityService _securityService;
        private readonly Microsoft.Extensions.Configuration.IConfiguration _configuration;
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _sessionLocks = new();
        private readonly ConcurrentDictionary<string, byte> _activeSessions = new();
        private readonly int _ttlHours;

        public SessionStateManager(ILogger logger, ETL_SQL.Services.SecurityService securityService, Microsoft.Extensions.Configuration.IConfiguration configuration, string? customSessionDir = null)
        {
            _logger = logger;
            _securityService = securityService;
            _configuration = configuration;
            SessionRoot = InitializeSessionRoot(customSessionDir);

            _ttlHours = int.TryParse(_configuration["Session:PersistentSessionTTLHours"], out var val) ? val : 24;

            // Defer reaping to a randomized background delay (5–30 s) so simultaneous process
            // starts do not race to delete the same stale session directories. Errors inside
            // ReapStaleSessions are already caught and logged, so fire-and-forget is safe.
            var reapDelay = TimeSpan.FromSeconds(Random.Shared.Next(5, 30));
            _ = Task.Delay(reapDelay).ContinueWith(_ => ReapStaleSessions(), TaskScheduler.Default);
        }

        /// <summary>
        /// Generates a deterministic encryption key for a session based on the machine key and session ID.
        /// Centralizing this ensures consistency between SpillStore and rehydration logic.
        /// </summary>
        public byte[] GetSpillKey(string sessionId)
        {
            var entropy = ETL_SQL.Services.SecurityService.GetMachineKey();
            using var sha256 = SHA256.Create();
            return sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(entropy + sessionId));
        }

        public void RegisterActiveSession(string sessionId) => _activeSessions.TryAdd(sessionId, 0);
        public void UnregisterActiveSession(string sessionId) => _activeSessions.TryRemove(sessionId, out _);
        public bool IsSessionInUse(string sessionId) => _activeSessions.ContainsKey(sessionId);

        private SemaphoreSlim GetSessionLock(string sessionId) => _sessionLocks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));

        private static string InitializeSessionRoot(string? customDir)
        {
            var root = customDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ETL-SQL", "Sessions");
            if (!Directory.Exists(root)) Directory.CreateDirectory(root);
            return root;
        }

        private string GetSessionDirectory(string sessionId)
        {
            var sessionDir = Path.GetFullPath(Path.Combine(SessionRoot, sessionId));
            if (!SafePath.IsWithinRoot(SessionRoot, sessionDir))
                throw new ExecutionException($"Invalid session id: {sessionId}");

            return sessionDir;
        }

        private string GetSessionDbPath(string sessionId) => Path.Combine(GetSessionDirectory(sessionId), "metadata.db");

        /// <summary>Saves the current evaluator state to a persistent SQLite-backed session.</summary>
        public async Task SaveSession(string sessionId, object evaluatorObj, string? scriptSource = null)
        {
            if (evaluatorObj is not Evaluator evaluator)
                return;

            // Enforce MaxSessionSize (Zero-Trust Guardrail)
            var currentSize = MeasureSessionSize(evaluator);
            if (currentSize > evaluator.MaxSessionSize)
            {
                throw new ETL_SQL.Core.Common.Exceptions.ExecutionException($"Session size {currentSize} bytes exceeds the safety limit of {evaluator.MaxSessionSize} bytes. Consider reducing global variable payload or lineage depth.");
            }

            var entropyKey = ETL_SQL.Services.SecurityService.GetMachineKey();
            using var store = new SqliteSessionMetadataStore(sessionId, SessionRoot, entropyKey);
            await store.InitializeAsync();

            var sessionLock = GetSessionLock(sessionId);
            await sessionLock.WaitAsync();
            try
            {
                // 1. Save Variables
                var (vars, meta) = evaluator.GetGlobalState();
                await store.SaveVariablesAsync(vars, meta);

                // 2. Save Lineage
                var lineage = evaluator.LineageTracker.GetFullLineage();
                await store.SaveLineageAsync(lineage);

                // 3. Save Connections
                var connections = evaluator.Connections
                    .Where(c => c.Value.ConnectorType != "INMEMORY")
                    .Select(c => new ConnectionInfo
                    {
                        Name = c.Key,
                        Type = c.Value.ConnectorType,
                        ConnectionString = GetSafeConnectionString(c.Value),
                        Options = c.Value.Options ?? new()
                    }).ToList();
                await store.SaveConnectionsAsync(connections);

                // 4. Save Docker State
                var dockerStrings = evaluator.DockerManager.GetState();
                var dockerLast = evaluator.DockerManager.LastConnectionString;
                await store.SaveDockerStateAsync(dockerLast, dockerStrings);

                // 6. Save Temp Tables
                var savedTables = await evaluator.DataSourceManager.GetTempTablesToSave();
                await store.SaveTempTablesAsync(savedTables);

                _logger.Info("[SESSION] Session {SessionId} persisted successfully (SQLite + Meta-Chunks)", sessionId);
            }
            finally
            {
                sessionLock.Release();
            }
        }

        private string GetSafeConnectionString(IDataSource ds)
        {
            // Database connectors often hide the connection string from IDataSource.Path for security/legacy reasons.
            // We look for a 'ConnectionString' property via reflection to ensure we persist the real credentials.
            var prop = ds.GetType().GetProperty("ConnectionString", BindingFlags.Public | BindingFlags.Instance);
            if (prop != null)
            {
                var val = prop.GetValue(ds)?.ToString();
                if (!string.IsNullOrEmpty(val)) return val;
            }
            return ds.Path;
        }

        /// <summary>Loads existing session state from the SQLite store.</summary>
        public async Task<SessionState?> LoadSession(string sessionId)
        {
            if (!File.Exists(GetSessionDbPath(sessionId))) return null;

            var entropyKey = ETL_SQL.Services.SecurityService.GetMachineKey();
            using var store = new SqliteSessionMetadataStore(sessionId, SessionRoot, entropyKey);
            await store.InitializeAsync();

            try
            {
                var state = new SessionState { SessionId = sessionId };

                // 1. Load Variables
                var (vars, meta) = await store.LoadVariablesAsync();
                state.GlobalVariables = vars;
                state.GlobalMetadata = meta;

                // 2. Load Lineage
                state.LineageEntries = (await store.LoadLineageAsync()).ToList();

                // 3. Load Connections
                state.Connections = (await store.LoadConnectionsAsync()).ToList();

                // 4. Load Docker State
                var (lastDocker, dockerStrings) = await store.LoadDockerStateAsync();
                state.LastDockerConnectionString = lastDocker;
                state.DockerConnectionStrings = dockerStrings;

                // 5. Load Temp Tables
                var savedTables = await store.LoadAllTempTablesAsync();
                foreach (var saved in savedTables)
                {
                    state.TempTables.Add(new TempTableInfo
                    {
                        Name = saved.TableName,
                        Columns = saved.Schema,
                        SpillChunkNames = saved.ChunkNames
                    });
                }

                return state;
            }
            catch (Exception ex)
            {
                _logger.Error("[SESSION_ERROR] Failed to load session {SessionId}: {Message}", ex, sessionId, ex.Message);
                return null;
            }
        }

        /// <summary>Clears session files and database from disk.</summary>
        public void ClearSession(string sessionId)
        {
            if (IsSessionInUse(sessionId))
            {
                _logger.Warning("[SESSION] Cannot clear session {SessionId} because it is active.", sessionId);
                return;
            }

            _securityService.ExecuteInternal(() =>
            {
                var sessionDir = GetSessionDirectory(sessionId);
                if (Directory.Exists(sessionDir))
                {
                    Directory.Delete(sessionDir, true);
                    _logger.Info("[SESSION] Cleared all persistent data for session {SessionId}", sessionId);
                }
            });
        }

        /// <summary>Returns a list of all managed sessions on disk.</summary>
        public IEnumerable<SessionSummary> GetSessions()
        {
            if (!Directory.Exists(SessionRoot)) yield break;

            foreach (var dir in Directory.GetDirectories(SessionRoot))
            {
                var sessionId = Path.GetFileName(dir);
                var dbPath = Path.Combine(dir, "metadata.db");
                if (!File.Exists(dbPath)) continue;

                var lastModified = File.GetLastWriteTime(dbPath);
                var createdAt = File.GetCreationTime(dbPath);

                long totalSize = Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);

                yield return new SessionSummary
                {
                    SessionId = sessionId,
                    CreatedAt = createdAt,
                    LastModifiedAt = lastModified,
                    TotalSizeBytes = totalSize
                };
            }
        }

        public void ReapStaleSessions(TimeSpan maxAge)
        {
            var now = DateTime.Now;
            foreach (var summary in GetSessions())
            {
                if (now - summary.LastModifiedAt > maxAge && !IsSessionInUse(summary.SessionId))
                {
                    ClearSession(summary.SessionId);
                }
            }
        }
        /// <summary>
        /// Scans the session root and deletes any session directories where the metadata 
        /// has not been touched within the configured TTL hours.
        /// </summary>
        public void ReapStaleSessions()
        {
            try
            {
                if (!Directory.Exists(SessionRoot)) return;

                var cutoff = DateTime.Now.AddHours(-_ttlHours);
                var sessionDirs = Directory.GetDirectories(SessionRoot);
                int reapCount = 0;

                foreach (var dir in sessionDirs)
                {
                    var sessionId = Path.GetFileName(dir);
                    if (IsSessionInUse(sessionId)) continue;

                    var dbPath = GetSessionDbPath(sessionId);
                    if (File.Exists(dbPath))
                    {
                        var lastWrite = File.GetLastWriteTime(dbPath);
                        if (lastWrite < cutoff)
                        {
                            try
                            {
                                Directory.Delete(dir, true);
                                reapCount++;
                            }
                            catch (Exception ex)
                            {
                                _logger.Warning("Failed to reap stale session {SessionId}: {Message}", sessionId, ex.Message);
                            }
                        }
                    }
                    else
                    {
                        // Directory exists but no metadata.db? Might be an abandoned or corrupted session.
                        // If it's old enough, clean it up.
                        var dirTime = Directory.GetLastWriteTime(dir);
                        if (dirTime < cutoff)
                        {
                            try
                            {
                                Directory.Delete(dir, true);
                                reapCount++;
                            }
                            catch (Exception ex)
                            {
                                _logger.Warning("Failed to reap abandoned session directory {SessionId}: {Message}", sessionId, ex.Message);
                            }
                        }
                    }
                }

                if (reapCount > 0)
                {
                    _logger.Info("[SESSION] Reaped {Count} stale persistent sessions (TTL: {TTL}h).", reapCount, _ttlHours);
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Error during session reaping: {Message}", ex);
            }
        }

        private string Compress(string data)
        {
            if (string.IsNullOrEmpty(data)) return data;
            var bytes = System.Text.Encoding.UTF8.GetBytes(data);
            using var ms = new MemoryStream();
            using (var gzip = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionLevel.Optimal))
            {
                gzip.Write(bytes, 0, bytes.Length);
            }
            return "COMP:" + Convert.ToBase64String(ms.ToArray());
        }

        private string Decompress(string data)
        {
            if (string.IsNullOrEmpty(data) || !data.StartsWith("COMP:")) return data;
            var base64 = data.Substring(5);
            var bytes = Convert.FromBase64String(base64);
            using var ms = new MemoryStream(bytes);
            using var decompressed = new MemoryStream();
            using (var gzip = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionMode.Decompress))
            {
                gzip.CopyTo(decompressed);
            }
            return System.Text.Encoding.UTF8.GetString(decompressed.ToArray());
        }

        private long MeasureSessionSize(Evaluator evaluator)
        {
            long size = 0;

            // 1. Variables and Metadata
            var (vars, meta) = evaluator.GetGlobalState();
            foreach (var kv in vars)
            {
                size += kv.Key.Length * 2;
                if (kv.Value is string s) size += s.Length * 2;
                else if (kv.Value is byte[] b) size += b.Length;
                else size += 16; // Guess for other primitives/objects
            }
            foreach (var kv in meta)
            {
                size += kv.Key.Length * 2;
                if (kv.Value.DataType != null) size += kv.Value.DataType.Length * 2;
            }

            // 2. Lineage
            var lineage = evaluator.LineageTracker.GetFullLineage();
            foreach (var entry in lineage)
            {
                size += (entry.TargetTable.Length + (entry.TargetColumn?.Length ?? 0) + entry.Operation.Length) * 2;
                if (entry.SourceTables != null) size += entry.SourceTables.Sum(st => st.Length) * 2;
                if (entry.SourceColumns != null) size += entry.SourceColumns.Sum(sc => sc.Length) * 2;
                if (entry.Metadata != null) size += entry.Metadata.Sum(kv => kv.Key.Length + kv.Value.Length) * 2;
                if (entry.DerivedFromDescriptions != null) size += entry.DerivedFromDescriptions.Length * 2;
                size += 50; // Timestamp and other fields
            }

            // 3. Connections
            foreach (var conn in evaluator.Connections)
            {
                size += conn.Key.Length * 2;
                if (conn.Value is IDatabaseSource db)
                {
                    if (db.ConnectionString != null) size += db.ConnectionString.Length * 2;
                }
            }

            return size;
        }
    }
}
