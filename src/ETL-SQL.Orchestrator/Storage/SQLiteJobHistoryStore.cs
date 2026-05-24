using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using ETL_SQL.Analysis.Lineage;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;

namespace ETL_SQL.Orchestrator.Storage
{
    /// <summary>
    /// SQLite-backed implementation of the job history store, managing job definitions and execution logs.
    /// </summary>
    public class SQLiteJobHistoryStore : IJobHistoryStore, IBundleStore, ILineageCatalogStore
    {
        private readonly string _connectionString;
        private bool _initialized;
        private readonly System.Threading.SemaphoreSlim _initLock = new(1, 1);

        /// <summary>
        /// Returns the canonical global DB path in LocalApplicationData so all instances on the
        /// same machine share the same job history regardless of their working directory.
        /// </summary>
        public static string DefaultDbPath()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ETL-SQL");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "etlsql.db");
        }

        public SQLiteJobHistoryStore(string? dbPath = null)
        {
            _connectionString = $"Data Source={dbPath ?? DefaultDbPath()}";
        }

        private async Task EnsureInitializedAsync()
        {
            if (_initialized) return;
            await _initLock.WaitAsync();
            try { if (!_initialized) { await InitializeAsync(); _initialized = true; } }
            finally { _initLock.Release(); }
        }

        /// <summary>Initializes the SQLite database and creates the necessary tables if they don't exist.</summary>
        public async Task InitializeAsync()
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var createJobsTable = @"
                CREATE TABLE IF NOT EXISTS Jobs (
                    Name TEXT PRIMARY KEY,
                    Script TEXT NOT NULL,
                    Interval INTEGER NOT NULL,
                    Unit TEXT NOT NULL,
                    AtTime TEXT,
                    LastRun TEXT,
                    NextRun TEXT,
                    IsEnabled INTEGER NOT NULL DEFAULT 1,
                    MaxRetries INTEGER NOT NULL DEFAULT 0,
                    RetryDelaySeconds INTEGER NOT NULL DEFAULT 30
                );";

            var createHistoryTable = @"
                CREATE TABLE IF NOT EXISTS JobHistory (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    JobName TEXT NOT NULL,
                    StartTime TEXT NOT NULL,
                    EndTime TEXT,
                    Status TEXT NOT NULL,
                    ErrorMessage TEXT,
                    RowsProcessed INTEGER DEFAULT 0
                );";

            var createBundleTables = @"
                CREATE TABLE IF NOT EXISTS BundleVersions (
                    BundleName TEXT NOT NULL,
                    Version INTEGER NOT NULL,
                    EntryPath TEXT NOT NULL,
                    ContentHash TEXT NOT NULL,
                    PublishedAt TEXT NOT NULL,
                    Publisher TEXT,
                    Description TEXT,
                    EncryptionMode TEXT NOT NULL DEFAULT 'MACHINE',
                    EncryptionMetadata TEXT,
                    PRIMARY KEY (BundleName, Version)
                );

                CREATE TABLE IF NOT EXISTS BundleFiles (
                    BundleName TEXT NOT NULL,
                    Version INTEGER NOT NULL,
                    VirtualPath TEXT NOT NULL,
                    Content TEXT NOT NULL,
                    ContentHash TEXT NOT NULL,
                    SizeBytes INTEGER NOT NULL,
                    ContentType TEXT NOT NULL,
                    PRIMARY KEY (BundleName, Version, VirtualPath),
                    FOREIGN KEY (BundleName, Version) REFERENCES BundleVersions(BundleName, Version)
                );

                CREATE TABLE IF NOT EXISTS BundleDependencies (
                    BundleName TEXT NOT NULL,
                    Version INTEGER NOT NULL,
                    FromPath TEXT NOT NULL,
                    ToPath TEXT NOT NULL,
                    PRIMARY KEY (BundleName, Version, FromPath, ToPath),
                    FOREIGN KEY (BundleName, Version) REFERENCES BundleVersions(BundleName, Version)
                );";

            var createLineageHistoryTable = @"
                CREATE TABLE IF NOT EXISTS LineageHistory (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    RunAt TEXT NOT NULL,
                    JobName TEXT,
                    ScriptPath TEXT,
                    TargetTable TEXT NOT NULL,
                    TargetColumn TEXT,
                    SourceTables TEXT NOT NULL DEFAULT '[]',
                    SourceColumns TEXT NOT NULL DEFAULT '[]',
                    Operation TEXT NOT NULL,
                    Tags TEXT NOT NULL DEFAULT '{}',
                    SourceFile TEXT,
                    Line INTEGER NOT NULL DEFAULT 0
                );
                CREATE INDEX IF NOT EXISTS idx_lh_target ON LineageHistory(TargetTable COLLATE NOCASE);
                CREATE INDEX IF NOT EXISTS idx_lh_runAt ON LineageHistory(RunAt);";

            using var command = connection.CreateCommand();
            command.CommandText = createJobsTable + createHistoryTable + createBundleTables + createLineageHistoryTable;
            await command.ExecuteNonQueryAsync();

            // 8B-2: Schema migration — add resource tracking columns if missing
            await EnsureHistoryColumnsExist(connection);
            await EnsureJobColumnsExist(connection);
        }

        private async Task EnsureJobColumnsExist(SqliteConnection connection)
        {
            var columns = new List<string>();
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "PRAGMA table_info(Jobs);";
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync()) columns.Add(reader.GetString(1));
            }

            if (!columns.Contains("MaxRetries"))
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE Jobs ADD COLUMN MaxRetries INTEGER NOT NULL DEFAULT 0;";
                await cmd.ExecuteNonQueryAsync();
            }

            if (!columns.Contains("RetryDelaySeconds"))
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE Jobs ADD COLUMN RetryDelaySeconds INTEGER NOT NULL DEFAULT 30;";
                await cmd.ExecuteNonQueryAsync();
            }

            if (!columns.Contains("ScriptHash"))
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE Jobs ADD COLUMN ScriptHash TEXT;";
                await cmd.ExecuteNonQueryAsync();
            }

            if (!columns.Contains("HashPolicy"))
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE Jobs ADD COLUMN HashPolicy TEXT NOT NULL DEFAULT 'Warn';";
                await cmd.ExecuteNonQueryAsync();
            }
        }

        private async Task EnsureHistoryColumnsExist(SqliteConnection connection)
        {
            var columns = new List<string>();
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "PRAGMA table_info(JobHistory);";
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync()) columns.Add(reader.GetString(1));
            }

            if (!columns.Contains("PeakMemoryBytes"))
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE JobHistory ADD COLUMN PeakMemoryBytes INTEGER DEFAULT 0;";
                await cmd.ExecuteNonQueryAsync();
            }

            if (!columns.Contains("CpuTimeSeconds"))
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE JobHistory ADD COLUMN CpuTimeSeconds REAL DEFAULT 0;";
                await cmd.ExecuteNonQueryAsync();
            }

            if (!columns.Contains("ScriptHashAtRunTime"))
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE JobHistory ADD COLUMN ScriptHashAtRunTime TEXT;";
                await cmd.ExecuteNonQueryAsync();
            }

            if (!columns.Contains("HashMatched"))
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE JobHistory ADD COLUMN HashMatched INTEGER;";
                await cmd.ExecuteNonQueryAsync();
            }
        }

        public async Task SaveJobAsync(JobDefinition job)
        {
            await EnsureInitializedAsync();
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var sql = @"
                INSERT OR REPLACE INTO Jobs (Name, Script, Interval, Unit, AtTime, LastRun, NextRun, IsEnabled, MaxRetries, RetryDelaySeconds, ScriptHash, HashPolicy)
                VALUES ($name, $script, $interval, $unit, $atTime, $lastRun, $nextRun, $isEnabled, $maxRetries, $retryDelay, $scriptHash, $hashPolicy);";

            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$name", job.Name);
            command.Parameters.AddWithValue("$script", job.Script);
            command.Parameters.AddWithValue("$interval", job.Interval);
            command.Parameters.AddWithValue("$unit", job.Unit);
            command.Parameters.AddWithValue("$atTime", (object?)job.AtTime ?? DBNull.Value);
            command.Parameters.AddWithValue("$lastRun", (object?)job.LastRun?.ToString("O") ?? DBNull.Value);
            command.Parameters.AddWithValue("$nextRun", (object?)job.NextRun?.ToString("O") ?? DBNull.Value);
            command.Parameters.AddWithValue("$isEnabled", job.IsEnabled ? 1 : 0);
            command.Parameters.AddWithValue("$maxRetries", job.MaxRetries);
            command.Parameters.AddWithValue("$retryDelay", job.RetryDelaySeconds);
            command.Parameters.AddWithValue("$scriptHash", (object?)job.ScriptHash ?? DBNull.Value);
            command.Parameters.AddWithValue("$hashPolicy", job.HashPolicy);

            await command.ExecuteNonQueryAsync();
        }

        public async Task<IEnumerable<JobDefinition>> GetActiveJobsAsync()
        {
            await EnsureInitializedAsync();
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var sql = "SELECT * FROM Jobs WHERE IsEnabled = 1;";
            using var command = connection.CreateCommand();
            command.CommandText = sql;

            var jobs = new List<JobDefinition>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                jobs.Add(new JobDefinition(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetInt32(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : DateTime.Parse(reader.GetString(5)),
                    reader.IsDBNull(6) ? null : DateTime.Parse(reader.GetString(6)),
                    reader.GetInt32(7) == 1,
                    reader.IsDBNull(8) ? 0 : reader.GetInt32(8),
                    reader.IsDBNull(9) ? 30 : reader.GetInt32(9),
                    reader.IsDBNull(10) ? null : reader.GetString(10),
                    reader.IsDBNull(11) ? "Warn" : reader.GetString(11)
                ));
            }
            return jobs;
        }

        public async Task<IEnumerable<JobDefinition>> GetAllJobsAsync()
        {
            await EnsureInitializedAsync();
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM Jobs;";

            var jobs = new List<JobDefinition>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                jobs.Add(new JobDefinition(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetInt32(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : DateTime.Parse(reader.GetString(5)),
                    reader.IsDBNull(6) ? null : DateTime.Parse(reader.GetString(6)),
                    reader.GetInt32(7) == 1,
                    reader.IsDBNull(8) ? 0 : reader.GetInt32(8),
                    reader.IsDBNull(9) ? 30 : reader.GetInt32(9),
                    reader.IsDBNull(10) ? null : reader.GetString(10),
                    reader.IsDBNull(11) ? "Warn" : reader.GetString(11)
                ));
            }
            return jobs;
        }

        public async Task<JobDefinition?> GetJobAsync(string name)
        {
            await EnsureInitializedAsync();
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM Jobs WHERE Name = @name COLLATE NOCASE LIMIT 1;";
            command.Parameters.AddWithValue("@name", name);

            using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;

            return new JobDefinition(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : DateTime.Parse(reader.GetString(5)),
                reader.IsDBNull(6) ? null : DateTime.Parse(reader.GetString(6)),
                reader.GetInt32(7) == 1,
                reader.IsDBNull(8) ? 0 : reader.GetInt32(8),
                reader.IsDBNull(9) ? 30 : reader.GetInt32(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(11) ? "Warn" : reader.GetString(11)
            );
        }

        public async Task DeleteJobAsync(string name)
        {
            await EnsureInitializedAsync();
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();
            try
            {
                var sql1 = "DELETE FROM Jobs WHERE Name = $name;";
                using var command1 = connection.CreateCommand();
                command1.CommandText = sql1;
                command1.Transaction = transaction;
                command1.Parameters.AddWithValue("$name", name);
                await command1.ExecuteNonQueryAsync();

                var sql2 = "DELETE FROM JobHistory WHERE JobName = $name;";
                using var command2 = connection.CreateCommand();
                command2.CommandText = sql2;
                command2.Transaction = transaction;
                command2.Parameters.AddWithValue("$name", name);
                await command2.ExecuteNonQueryAsync();

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task UpdateJobLastRunAsync(string name, DateTime lastRun, DateTime? nextRun)
        {
            await EnsureInitializedAsync();
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var sql = "UPDATE Jobs SET LastRun = $lastRun, NextRun = $nextRun WHERE Name = $name;";
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$lastRun", lastRun.ToString("O"));
            command.Parameters.AddWithValue("$nextRun", (object?)nextRun?.ToString("O") ?? DBNull.Value);
            await command.ExecuteNonQueryAsync();
        }

        public async Task<long> LogJobStartAsync(string jobName)
        {
            await EnsureInitializedAsync();
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var sql = "INSERT INTO JobHistory (JobName, StartTime, Status) VALUES ($name, $start, 'RUNNING'); SELECT last_insert_rowid();";
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$name", jobName);
            command.Parameters.AddWithValue("$start", DateTime.Now.ToString("O"));

            return (long)(await command.ExecuteScalarAsync())!;
        }

        public async Task LogJobEndAsync(long entryId, string status, string? errorMessage = null, long rowsProcessed = 0, long peakMemoryBytes = 0, double cpuTimeSeconds = 0, string? scriptHashAtRunTime = null, bool? hashMatched = null)
        {
            await EnsureInitializedAsync();
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var sql = "UPDATE JobHistory SET EndTime = $end, Status = $status, ErrorMessage = $err, RowsProcessed = $rows, PeakMemoryBytes = $mem, CpuTimeSeconds = $cpu, ScriptHashAtRunTime = $hash, HashMatched = $matched WHERE Id = $id;";
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$id", entryId);
            command.Parameters.AddWithValue("$end", DateTime.Now.ToString("O"));
            command.Parameters.AddWithValue("$status", status);
            command.Parameters.AddWithValue("$err", (object?)errorMessage ?? DBNull.Value);
            command.Parameters.AddWithValue("$rows", rowsProcessed);
            command.Parameters.AddWithValue("$mem", peakMemoryBytes);
            command.Parameters.AddWithValue("$cpu", cpuTimeSeconds);
            command.Parameters.AddWithValue("$hash", (object?)scriptHashAtRunTime ?? DBNull.Value);
            command.Parameters.AddWithValue("$matched", hashMatched.HasValue ? (object)(hashMatched.Value ? 1 : 0) : DBNull.Value);

            await command.ExecuteNonQueryAsync();
        }

        public async Task<IEnumerable<JobHistoryEntry>> GetHistoryAsync(string? jobName = null, int limit = 100)
        {
            await EnsureInitializedAsync();
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var sql = "SELECT * FROM JobHistory ";
            if (jobName != null) sql += "WHERE JobName = $name ";
            sql += "ORDER BY StartTime DESC LIMIT $limit;";

            using var command = connection.CreateCommand();
            command.CommandText = sql;
            if (jobName != null) command.Parameters.AddWithValue("$name", jobName);
            command.Parameters.AddWithValue("$limit", limit);

            var entries = new List<JobHistoryEntry>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                entries.Add(new JobHistoryEntry(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    DateTime.Parse(reader.GetString(2)),
                    reader.IsDBNull(3) ? null : DateTime.Parse(reader.GetString(3)),
                    reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.GetInt64(6),
                    reader.IsDBNull(7) ? 0 : reader.GetInt64(7),
                    reader.IsDBNull(8) ? 0 : reader.GetDouble(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9),
                    reader.IsDBNull(10) ? null : (bool?)(reader.GetInt32(10) != 0)
                ));
            }
            return entries;
        }

        public async Task<BundleVersionInfo> PublishBundleAsync(BundlePublishRequest request)
        {
            await EnsureInitializedAsync();
            var latest = await GetLatestVersionAsync(request.BundleName);
            if (latest != null && string.Equals(latest.ContentHash, request.ContentHash, StringComparison.OrdinalIgnoreCase))
                return latest;

            var nextVersion = (latest?.Version ?? 0) + 1;
            var publishedAt = DateTime.Now;

            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();
            try
            {
                using (var cmd = connection.CreateCommand())
                {
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"
                        INSERT INTO BundleVersions
                            (BundleName, Version, EntryPath, ContentHash, PublishedAt, Publisher, Description, EncryptionMode, EncryptionMetadata)
                        VALUES
                            ($bundle, $version, $entry, $hash, $published, $publisher, $description, $mode, $metadata);";
                    cmd.Parameters.AddWithValue("$bundle", request.BundleName);
                    cmd.Parameters.AddWithValue("$version", nextVersion);
                    cmd.Parameters.AddWithValue("$entry", NormalizeVirtualPath(request.EntryPath));
                    cmd.Parameters.AddWithValue("$hash", request.ContentHash);
                    cmd.Parameters.AddWithValue("$published", publishedAt.ToString("O"));
                    cmd.Parameters.AddWithValue("$publisher", (object?)request.Publisher ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$description", (object?)request.Description ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$mode", request.EncryptionMode);
                    cmd.Parameters.AddWithValue("$metadata", (object?)request.EncryptionMetadata ?? DBNull.Value);
                    await cmd.ExecuteNonQueryAsync();
                }

                foreach (var file in request.Files)
                {
                    using var cmd = connection.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"
                        INSERT INTO BundleFiles
                            (BundleName, Version, VirtualPath, Content, ContentHash, SizeBytes, ContentType)
                        VALUES
                            ($bundle, $version, $path, $content, $hash, $size, $type);";
                    cmd.Parameters.AddWithValue("$bundle", request.BundleName);
                    cmd.Parameters.AddWithValue("$version", nextVersion);
                    cmd.Parameters.AddWithValue("$path", NormalizeVirtualPath(file.VirtualPath));
                    cmd.Parameters.AddWithValue("$content", file.Content);
                    cmd.Parameters.AddWithValue("$hash", file.ContentHash);
                    cmd.Parameters.AddWithValue("$size", file.SizeBytes);
                    cmd.Parameters.AddWithValue("$type", file.ContentType);
                    await cmd.ExecuteNonQueryAsync();
                }

                foreach (var dep in request.Dependencies)
                {
                    using var cmd = connection.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"
                        INSERT OR IGNORE INTO BundleDependencies
                            (BundleName, Version, FromPath, ToPath)
                        VALUES
                            ($bundle, $version, $from, $to);";
                    cmd.Parameters.AddWithValue("$bundle", request.BundleName);
                    cmd.Parameters.AddWithValue("$version", nextVersion);
                    cmd.Parameters.AddWithValue("$from", NormalizeVirtualPath(dep.FromPath));
                    cmd.Parameters.AddWithValue("$to", NormalizeVirtualPath(dep.ToPath));
                    await cmd.ExecuteNonQueryAsync();
                }

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }

            var versionInfo = new BundleVersionInfo(request.BundleName, nextVersion, NormalizeVirtualPath(request.EntryPath),
                request.ContentHash, publishedAt, request.Publisher, request.Description);
            var lineage = BuildBundleLineage(request, versionInfo);
            if (lineage.Count > 0)
            {
                await SaveLineageAsync(
                    lineage,
                    $"bundle:{versionInfo.BundleName}@{versionInfo.Version}",
                    $"orch://{versionInfo.BundleName}@{versionInfo.Version}/{versionInfo.EntryPath}",
                    versionInfo.PublishedAt);
            }

            return versionInfo;
        }

        private static IReadOnlyList<LineageEntry> BuildBundleLineage(BundlePublishRequest request, BundleVersionInfo version)
        {
            var entries = new List<LineageEntry>();
            foreach (var file in request.Files.Where(IsScriptFile))
            {
                var virtualPath = NormalizeVirtualPath(file.VirtualPath);
                try
                {
                    var tokens = new Lexer(file.Content).Tokenize();
                    var script = new Parser(tokens, file.Content).Parse();
                    var tracker = new LineageTracker(NullLogger.Instance);
                    tracker.GlobalMetadata["bundle"] = version.BundleName;
                    tracker.GlobalMetadata["bundle_version"] = version.Version.ToString();
                    tracker.GlobalMetadata["bundle_path"] = virtualPath;
                    new LineageAnalyzer(tracker).Analyze(script);

                    foreach (var entry in tracker.GetFullLineage())
                    {
                        entry.SourceFile = virtualPath;
                        entry.Metadata["bundle"] = version.BundleName;
                        entry.Metadata["bundle_version"] = version.Version.ToString();
                        entry.Metadata["bundle_path"] = virtualPath;
                        entries.Add(entry);
                    }
                }
                catch
                {
                    // Bundle content has already been accepted by the publish path. Lineage is best-effort.
                }
            }

            return entries;
        }

        private static bool IsScriptFile(BundlePublishFile file) =>
            file.ContentType.Equals("application/etlsql", StringComparison.OrdinalIgnoreCase)
            || file.ContentType.Equals("application/rptsql", StringComparison.OrdinalIgnoreCase)
            || file.VirtualPath.EndsWith(".etlsql", StringComparison.OrdinalIgnoreCase)
            || file.VirtualPath.EndsWith(".rptsql", StringComparison.OrdinalIgnoreCase);

        public async Task<BundleVersionInfo?> GetLatestVersionAsync(string bundleName)
        {
            await EnsureInitializedAsync();
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"SELECT BundleName, Version, EntryPath, ContentHash, PublishedAt, Publisher, Description
                                FROM BundleVersions WHERE BundleName = $bundle COLLATE NOCASE
                                ORDER BY Version DESC LIMIT 1;";
            cmd.Parameters.AddWithValue("$bundle", bundleName);
            using var reader = await cmd.ExecuteReaderAsync();
            return await reader.ReadAsync() ? ReadBundleVersion(reader) : null;
        }

        public async Task<BundleVersionInfo?> GetVersionAsync(string bundleName, int version)
        {
            await EnsureInitializedAsync();
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"SELECT BundleName, Version, EntryPath, ContentHash, PublishedAt, Publisher, Description
                                FROM BundleVersions WHERE BundleName = $bundle COLLATE NOCASE AND Version = $version LIMIT 1;";
            cmd.Parameters.AddWithValue("$bundle", bundleName);
            cmd.Parameters.AddWithValue("$version", version);
            using var reader = await cmd.ExecuteReaderAsync();
            return await reader.ReadAsync() ? ReadBundleVersion(reader) : null;
        }

        public async Task<BundleFileInfo?> GetFileAsync(string bundleName, int version, string virtualPath)
        {
            await EnsureInitializedAsync();
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"SELECT BundleName, Version, VirtualPath, Content, ContentHash, SizeBytes, ContentType
                                FROM BundleFiles
                                WHERE BundleName = $bundle COLLATE NOCASE AND Version = $version AND VirtualPath = $path COLLATE NOCASE
                                LIMIT 1;";
            cmd.Parameters.AddWithValue("$bundle", bundleName);
            cmd.Parameters.AddWithValue("$version", version);
            cmd.Parameters.AddWithValue("$path", NormalizeVirtualPath(virtualPath));
            using var reader = await cmd.ExecuteReaderAsync();
            return await reader.ReadAsync() ? ReadBundleFile(reader) : null;
        }

        public async Task<IEnumerable<BundleVersionInfo>> GetBundlesAsync()
        {
            await EnsureInitializedAsync();
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"SELECT bv.BundleName, bv.Version, bv.EntryPath, bv.ContentHash, bv.PublishedAt, bv.Publisher, bv.Description
                                FROM BundleVersions bv
                                INNER JOIN (
                                    SELECT BundleName, MAX(Version) AS Version FROM BundleVersions GROUP BY BundleName
                                ) latest ON latest.BundleName = bv.BundleName AND latest.Version = bv.Version
                                ORDER BY bv.BundleName COLLATE NOCASE;";
            return await ReadBundleVersionsAsync(cmd);
        }

        public async Task<IEnumerable<BundleVersionInfo>> GetVersionsAsync(string bundleName)
        {
            await EnsureInitializedAsync();
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"SELECT BundleName, Version, EntryPath, ContentHash, PublishedAt, Publisher, Description
                                FROM BundleVersions WHERE BundleName = $bundle COLLATE NOCASE
                                ORDER BY Version DESC;";
            cmd.Parameters.AddWithValue("$bundle", bundleName);
            return await ReadBundleVersionsAsync(cmd);
        }

        public async Task<IEnumerable<BundleFileInfo>> GetFilesAsync(string bundleName, int version)
        {
            await EnsureInitializedAsync();
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"SELECT BundleName, Version, VirtualPath, Content, ContentHash, SizeBytes, ContentType
                                FROM BundleFiles WHERE BundleName = $bundle COLLATE NOCASE AND Version = $version
                                ORDER BY VirtualPath COLLATE NOCASE;";
            cmd.Parameters.AddWithValue("$bundle", bundleName);
            cmd.Parameters.AddWithValue("$version", version);
            var files = new List<BundleFileInfo>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) files.Add(ReadBundleFile(reader));
            return files;
        }

        public async Task<IEnumerable<BundleDependencyInfo>> GetDependenciesAsync(string bundleName, int version)
        {
            await EnsureInitializedAsync();
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"SELECT BundleName, Version, FromPath, ToPath
                                FROM BundleDependencies WHERE BundleName = $bundle COLLATE NOCASE AND Version = $version
                                ORDER BY FromPath COLLATE NOCASE, ToPath COLLATE NOCASE;";
            cmd.Parameters.AddWithValue("$bundle", bundleName);
            cmd.Parameters.AddWithValue("$version", version);
            var deps = new List<BundleDependencyInfo>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                deps.Add(new BundleDependencyInfo(reader.GetString(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3)));
            return deps;
        }

        private static async Task<IEnumerable<BundleVersionInfo>> ReadBundleVersionsAsync(SqliteCommand cmd)
        {
            var versions = new List<BundleVersionInfo>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) versions.Add(ReadBundleVersion(reader));
            return versions;
        }

        private static BundleVersionInfo ReadBundleVersion(SqliteDataReader reader) => new(
            reader.GetString(0),
            reader.GetInt32(1),
            reader.GetString(2),
            reader.GetString(3),
            DateTime.Parse(reader.GetString(4)),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6));

        private static BundleFileInfo ReadBundleFile(SqliteDataReader reader) => new(
            reader.GetString(0),
            reader.GetInt32(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetInt64(5),
            reader.GetString(6));

        private static string NormalizeVirtualPath(string path)
            => path.Replace('\\', '/').TrimStart('/');

        // ── ILineageCatalogStore ──────────────────────────────────────────────

        public async Task SaveLineageAsync(IEnumerable<LineageEntry> entries, string? jobName, string? scriptPath, DateTime runAt)
        {
            await EnsureInitializedAsync();
            var runAtStr = runAt.ToString("O");
            var entriesList = entries.ToList();
            if (entriesList.Count == 0) return;

            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();
            try
            {
                foreach (var entry in entriesList)
                {
                    using var cmd = connection.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"
                        INSERT INTO LineageHistory
                            (RunAt, JobName, ScriptPath, TargetTable, TargetColumn, SourceTables, SourceColumns, Operation, Tags, SourceFile, Line)
                        VALUES
                            ($runAt, $job, $script, $target, $col, $sources, $srcCols, $op, $tags, $file, $line);";
                    cmd.Parameters.AddWithValue("$runAt",   runAtStr);
                    cmd.Parameters.AddWithValue("$job",     (object?)jobName    ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$script",  (object?)scriptPath ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$target",  entry.TargetTable);
                    cmd.Parameters.AddWithValue("$col",     (object?)entry.TargetColumn ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$sources", JsonSerializer.Serialize(entry.SourceTables));
                    cmd.Parameters.AddWithValue("$srcCols", JsonSerializer.Serialize(entry.SourceColumns));
                    cmd.Parameters.AddWithValue("$op",      entry.Operation);
                    cmd.Parameters.AddWithValue("$tags",    JsonSerializer.Serialize(entry.Metadata));
                    cmd.Parameters.AddWithValue("$file",    (object?)entry.SourceFile ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$line",    entry.Line);
                    await cmd.ExecuteNonQueryAsync();
                }
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<IEnumerable<LineageHistoryEntry>> GetHistoryForTableAsync(string tableName, int limit = 100)
        {
            await EnsureInitializedAsync();
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, RunAt, JobName, ScriptPath, TargetTable, TargetColumn,
                       SourceTables, Operation, Tags, SourceFile, Line
                FROM LineageHistory
                WHERE TargetTable = $table COLLATE NOCASE
                ORDER BY RunAt DESC, Id DESC
                LIMIT $limit;";
            cmd.Parameters.AddWithValue("$table", tableName);
            cmd.Parameters.AddWithValue("$limit", limit);
            return await ReadLineageHistoryAsync(cmd);
        }

        public async Task<IEnumerable<LineageHistoryEntry>> GetHistoryForTagAsync(string tagKey, string? tagValue = null, int limit = 100)
        {
            await EnsureInitializedAsync();
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            // Tags is stored as a JSON object. Use LIKE to find the key; refine with value if provided.
            var pattern = tagValue == null
                ? $"%\"{tagKey}\"%"
                : $"%\"{tagKey}\":\"{tagValue}\"%";
            cmd.CommandText = @"
                SELECT Id, RunAt, JobName, ScriptPath, TargetTable, TargetColumn,
                       SourceTables, Operation, Tags, SourceFile, Line
                FROM LineageHistory
                WHERE Tags LIKE $pattern
                ORDER BY RunAt DESC, Id DESC
                LIMIT $limit;";
            cmd.Parameters.AddWithValue("$pattern", pattern);
            cmd.Parameters.AddWithValue("$limit", limit);
            return await ReadLineageHistoryAsync(cmd);
        }

        public async Task<IEnumerable<LineageHistoryEntry>> GetHistoryForJobAsync(string jobName, int limit = 100)
        {
            await EnsureInitializedAsync();
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, RunAt, JobName, ScriptPath, TargetTable, TargetColumn,
                       SourceTables, Operation, Tags, SourceFile, Line
                FROM LineageHistory
                WHERE JobName = $jobName COLLATE NOCASE
                ORDER BY RunAt DESC, Id DESC
                LIMIT $limit;";
            cmd.Parameters.AddWithValue("$jobName", jobName);
            cmd.Parameters.AddWithValue("$limit", limit);
            return await ReadLineageHistoryAsync(cmd);
        }

        private static async Task<IEnumerable<LineageHistoryEntry>> ReadLineageHistoryAsync(SqliteCommand cmd)
        {
            var results = new List<LineageHistoryEntry>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var sourceTables = JsonSerializer.Deserialize<List<string>>(reader.GetString(6)) ?? new List<string>();
                var tags = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(8)) ?? new Dictionary<string, string>();
                results.Add(new LineageHistoryEntry(
                    reader.GetInt64(0),
                    DateTime.Parse(reader.GetString(1)),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    sourceTables,
                    reader.GetString(7),
                    tags,
                    reader.IsDBNull(9)  ? null : reader.GetString(9),
                    reader.IsDBNull(10) ? 0    : reader.GetInt32(10)
                ));
            }
            return results;
        }
    }
}
