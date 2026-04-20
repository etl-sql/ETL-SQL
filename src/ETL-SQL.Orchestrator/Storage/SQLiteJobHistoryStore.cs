using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using ETL_SQL.Core.Data;

namespace ETL_SQL.Orchestrator.Storage
{
    /// <summary>
    /// SQLite-backed implementation of the job history store, managing job definitions and execution logs.
    /// </summary>
    public class SQLiteJobHistoryStore : IJobHistoryStore
    {
        private readonly string _connectionString;

        public SQLiteJobHistoryStore(string dbPath = "etlsql.db")
        {
            _connectionString = $"Data Source={dbPath}";
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

            using var command = connection.CreateCommand();
            command.CommandText = createJobsTable + createHistoryTable;
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
        }

        public async Task SaveJobAsync(JobDefinition job)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var sql = @"
                INSERT OR REPLACE INTO Jobs (Name, Script, Interval, Unit, AtTime, LastRun, NextRun, IsEnabled, MaxRetries, RetryDelaySeconds)
                VALUES ($name, $script, $interval, $unit, $atTime, $lastRun, $nextRun, $isEnabled, $maxRetries, $retryDelay);";

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

            await command.ExecuteNonQueryAsync();
        }

        public async Task<IEnumerable<JobDefinition>> GetActiveJobsAsync()
        {
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
                    reader.IsDBNull(9) ? 30 : reader.GetInt32(9)
                ));
            }
            return jobs;
        }

        public async Task DeleteJobAsync(string name)
        {
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
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var sql = "INSERT INTO JobHistory (JobName, StartTime, Status) VALUES ($name, $start, 'RUNNING'); SELECT last_insert_rowid();";
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$name", jobName);
            command.Parameters.AddWithValue("$start", DateTime.Now.ToString("O"));

            return (long)(await command.ExecuteScalarAsync())!;
        }

        public async Task LogJobEndAsync(long entryId, string status, string? errorMessage = null, long rowsProcessed = 0, long peakMemoryBytes = 0, double cpuTimeSeconds = 0)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var sql = "UPDATE JobHistory SET EndTime = $end, Status = $status, ErrorMessage = $err, RowsProcessed = $rows, PeakMemoryBytes = $mem, CpuTimeSeconds = $cpu WHERE Id = $id;";
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$id", entryId);
            command.Parameters.AddWithValue("$end", DateTime.Now.ToString("O"));
            command.Parameters.AddWithValue("$status", status);
            command.Parameters.AddWithValue("$err", (object?)errorMessage ?? DBNull.Value);
            command.Parameters.AddWithValue("$rows", rowsProcessed);
            command.Parameters.AddWithValue("$mem", peakMemoryBytes);
            command.Parameters.AddWithValue("$cpu", cpuTimeSeconds);

            await command.ExecuteNonQueryAsync();
        }

        public async Task<IEnumerable<JobHistoryEntry>> GetHistoryAsync(string? jobName = null, int limit = 100)
        {
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
                    reader.IsDBNull(8) ? 0 : reader.GetDouble(8)
                ));
            }
            return entries;
        }
    }
}
