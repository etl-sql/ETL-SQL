using System.Globalization;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Multitenancy;

namespace ETL_SQL.Orchestrator.Storage;

public partial class RelationalJobHistoryStore
{
    public async Task<IEnumerable<JobDefinition>> GetAllJobsAsync(TenantContext tenant)
    {
        var tenantId = RequireEvidenceTenant(tenant);
        await EnsureInitializedAsync();
        using var connection = _dialect.CreateConnection();
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM Jobs WHERE TenantId = @tenant ORDER BY Name;";
        command.AddParam("@tenant", tenantId);

        var jobs = new List<JobDefinition>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) jobs.Add(ReadJob(reader));
        return jobs;
    }

    public async Task<JobDefinition?> GetJobAsync(TenantContext tenant, string name)
    {
        var tenantId = RequireEvidenceTenant(tenant);
        await EnsureInitializedAsync();
        using var connection = _dialect.CreateConnection();
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT * FROM Jobs
            WHERE TenantId = @tenant AND Name = @name COLLATE NOCASE
            LIMIT 1;";
        command.AddParam("@tenant", tenantId);
        command.AddParam("@name", name);

        using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadJob(reader) : null;
    }

    public async Task<IEnumerable<JobHistoryEntry>> GetHistoryAsync(
        TenantContext tenant, string? jobName = null, int limit = 100)
    {
        var tenantId = RequireEvidenceTenant(tenant);
        await EnsureInitializedAsync();
        using var connection = _dialect.CreateConnection();
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        // Partitioned by the history row's own tenant column rather than by a join to Jobs on name.
        // The join was wrong twice over: it matched any tenant's job that happened to share the name,
        // and it dropped the runs of a job that had since been deleted — history the tenant still
        // owns. The name filter stays on the history row for the same reason.
        var sql = @"
            SELECT h.*
            FROM JobHistory h
            WHERE h.TenantId = @tenant ";
        if (!string.IsNullOrWhiteSpace(jobName))
        {
            sql += "AND h.JobName = @job COLLATE NOCASE ";
            command.AddParam("@job", jobName);
        }
        command.CommandText = sql + "ORDER BY h.StartTime DESC, h.Id DESC LIMIT @limit;";
        command.AddParam("@tenant", tenantId);
        command.AddParam("@limit", Math.Clamp(limit, 1, 1000));

        var entries = new List<JobHistoryEntry>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) entries.Add(ReadHistoryEntry(reader));
        return entries;
    }

    public async Task<JobHistoryEntry?> GetHistoryEntryAsync(
        TenantContext tenant, long entryId)
    {
        var tenantId = RequireEvidenceTenant(tenant);
        await EnsureInitializedAsync();
        using var connection = _dialect.CreateConnection();
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT h.*
            FROM JobHistory h
            WHERE h.TenantId = @tenant AND h.Id = @historyId
            LIMIT 1;";
        command.AddParam("@tenant", tenantId);
        command.AddParam("@historyId", entryId);
        using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadHistoryEntry(reader) : null;
    }

    public async Task<IReadOnlyList<ETL_SQL.Core.Profiling.StatementMetricsPayload>>
        GetJobStatementMetricsAsync(TenantContext tenant, long entryId)
    {
        var tenantId = RequireEvidenceTenant(tenant);
        await EnsureInitializedAsync();
        using var connection = _dialect.CreateConnection();
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT m.Statement, m.DurationMs, m.RowsProcessed, m.CpuTimeMs, m.SpilledBytes,
                   m.SpillReadBytes, m.Partitions, m.QueueWaitMs, m.LockWaitMs, m.IndexUsed,
                   m.DqRowsValidated, m.DqRowsQuarantined, m.DqRowsWarned, m.DqValidationMs,
                   m.Failed
            FROM JobStatementMetrics m
            INNER JOIN JobHistory h ON h.Id = m.JobHistoryId
            WHERE h.TenantId = @tenant AND h.Id = @historyId
            ORDER BY m.Ordinal;";
        command.AddParam("@tenant", tenantId);
        command.AddParam("@historyId", entryId);

        var result = new List<ETL_SQL.Core.Profiling.StatementMetricsPayload>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new ETL_SQL.Core.Profiling.StatementMetricsPayload
            {
                Statement = reader.GetString(0),
                DurationMs = Convert.ToInt64(reader.GetValue(1)),
                RowsProcessed = Convert.ToInt64(reader.GetValue(2)),
                CpuTimeMs = Convert.ToInt64(reader.GetValue(3)),
                SpilledBytes = Convert.ToInt64(reader.GetValue(4)),
                SpillReadBytes = Convert.ToInt64(reader.GetValue(5)),
                Partitions = Convert.ToInt32(reader.GetValue(6)),
                QueueWaitMs = Convert.ToInt64(reader.GetValue(7)),
                LockWaitMs = Convert.ToInt64(reader.GetValue(8)),
                IndexUsed = reader.IsDBNull(9) ? null : reader.GetString(9),
                DataQualityRowsValidated = Convert.ToInt64(reader.GetValue(10)),
                DataQualityRowsQuarantined = Convert.ToInt64(reader.GetValue(11)),
                DataQualityRowsWarned = Convert.ToInt64(reader.GetValue(12)),
                DataQualityValidationMs = Convert.ToInt64(reader.GetValue(13)),
                Failed = Convert.ToInt64(reader.GetValue(14)) != 0
            });
        }
        return result;
    }

    public async Task<IReadOnlyList<JobDataQualityFailure>> GetDataQualityFailuresForJobAsync(
        TenantContext tenant, string jobName, int limit = 1000)
    {
        var tenantId = RequireEvidenceTenant(tenant);
        await EnsureInitializedAsync();
        using var connection = _dialect.CreateConnection();
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT h.Id, h.JobName, h.StartTime, h.EndTime, h.Status,
                   f.TargetTable, f.ColumnName, f.RuleText, f.Action, f.FailureCount, f.Owner
            FROM JobDataQualityFailures f
            INNER JOIN JobHistory h ON h.Id = f.JobHistoryId
            WHERE h.TenantId = @tenant AND h.JobName = @jobName COLLATE NOCASE
            ORDER BY h.StartTime DESC, h.Id DESC, f.TargetTable, f.ColumnName, f.RuleText, f.Action
            LIMIT @limit;";
        command.AddParam("@tenant", tenantId);
        command.AddParam("@jobName", jobName);
        command.AddParam("@limit", Math.Clamp(limit, 1, 10000));

        var results = new List<JobDataQualityFailure>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new JobDataQualityFailure(
                reader.GetInt64(0), reader.GetString(1), DateTime.Parse(reader.GetString(2)),
                reader.IsDBNull(3) ? null : DateTime.Parse(reader.GetString(3)), reader.GetString(4),
                reader.IsDBNull(5) || string.IsNullOrEmpty(reader.GetString(5)) ? null : reader.GetString(5),
                reader.GetString(6), reader.GetString(7), reader.GetString(8),
                reader.GetInt64(9), reader.IsDBNull(10) ? null : reader.GetString(10)));
        }
        return results;
    }

    public async Task<IReadOnlyList<JobDataQualityFailure>> GetDataQualityFailuresForRunAsync(
        TenantContext tenant, long entryId, int limit = 1000)
    {
        var tenantId = RequireEvidenceTenant(tenant);
        await EnsureInitializedAsync();
        using var connection = _dialect.CreateConnection();
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT h.Id, h.JobName, h.StartTime, h.EndTime, h.Status,
                   f.TargetTable, f.ColumnName, f.RuleText, f.Action, f.FailureCount, f.Owner
            FROM JobDataQualityFailures f
            INNER JOIN JobHistory h ON h.Id = f.JobHistoryId
            WHERE h.TenantId = @tenant AND h.Id = @historyId
            ORDER BY f.TargetTable, f.ColumnName, f.RuleText, f.Action
            LIMIT @limit;";
        command.AddParam("@tenant", tenantId);
        command.AddParam("@historyId", entryId);
        command.AddParam("@limit", Math.Clamp(limit, 1, 10000));

        var results = new List<JobDataQualityFailure>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new JobDataQualityFailure(
                reader.GetInt64(0), reader.GetString(1), DateTime.Parse(reader.GetString(2)),
                reader.IsDBNull(3) ? null : DateTime.Parse(reader.GetString(3)), reader.GetString(4),
                reader.IsDBNull(5) || string.IsNullOrEmpty(reader.GetString(5)) ? null : reader.GetString(5),
                reader.GetString(6), reader.GetString(7), reader.GetString(8),
                reader.GetInt64(9), reader.IsDBNull(10) ? null : reader.GetString(10)));
        }
        return results;
    }

    public async Task<string?> GetJobStateAsync(
        TenantContext tenant, string jobName, string key)
    {
        var tenantId = RequireEvidenceTenant(tenant);
        await EnsureInitializedAsync();
        using var connection = _dialect.CreateConnection();
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        // Joined on identity, and the name is resolved only on the Jobs side where it is unique per
        // tenant. Job state, unlike history, belongs to a live job and is deleted with it, so there
        // is always an identity to resolve.
        command.CommandText = @"
            SELECT s.StateValue
            FROM JobState s
            INNER JOIN Jobs j ON j.Id = s.JobId
            WHERE j.TenantId = @tenant
              AND j.Name = @job COLLATE NOCASE
              AND s.StateKey = @key;";
        command.AddParam("@tenant", tenantId);
        command.AddParam("@job", jobName);
        command.AddParam("@key", key);
        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? null : (string?)result;
    }

    public async Task SetJobStateAsync(
        TenantContext tenant, string jobName, string key, string? value)
    {
        var tenantId = RequireEvidenceTenant(tenant);
        await EnsureInitializedAsync();
        using var connection = _dialect.CreateConnection();
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO JobState (JobId, StateKey, StateValue, UpdatedAt)
            SELECT j.Id, @key, @value, @updatedAt
            FROM Jobs j
            WHERE j.TenantId = @tenant AND j.Name = @job COLLATE NOCASE
            ON CONFLICT (JobId, StateKey)
            DO UPDATE SET StateValue = EXCLUDED.StateValue, UpdatedAt = EXCLUDED.UpdatedAt;";
        command.AddParam("@tenant", tenantId);
        command.AddParam("@job", jobName);
        command.AddParam("@key", key);
        command.AddParam("@value", value);
        command.AddParam("@updatedAt", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
        if (await command.ExecuteNonQueryAsync() == 0)
            throw new KeyNotFoundException("The tenant-bound job was not found.");
    }

    public async Task<IReadOnlyList<JobStateEntry>> GetJobStatesAsync(
        TenantContext tenant, string? jobName = null, int limit = 1000)
    {
        var tenantId = RequireEvidenceTenant(tenant);
        await EnsureInitializedAsync();
        using var connection = _dialect.CreateConnection();
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        var sql = @"
            SELECT j.Name, s.StateKey, s.StateValue, s.UpdatedAt
            FROM JobState s
            INNER JOIN Jobs j ON j.Id = s.JobId
            WHERE j.TenantId = @tenant ";
        if (!string.IsNullOrWhiteSpace(jobName))
        {
            sql += "AND j.Name = @job COLLATE NOCASE ";
            command.AddParam("@job", jobName);
        }
        command.CommandText = sql + "ORDER BY j.Name, s.StateKey LIMIT @limit;";
        command.AddParam("@tenant", tenantId);
        command.AddParam("@limit", Math.Clamp(limit, 1, 5000));

        var results = new List<JobStateEntry>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new JobStateEntry(
                reader.GetString(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                DateTime.Parse(reader.GetString(3), null, DateTimeStyles.RoundtripKind)));
        }
        return results;
    }

    private static string RequireEvidenceTenant(TenantContext tenant)
    {
        RequireRuntimeTenant(tenant);
        return tenant.Tenant.Value;
    }
}
