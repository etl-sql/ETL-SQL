using ETL_SQL.Core.Data;

namespace ETL_SQL.Orchestrator.Storage;

public interface IJobToolApprovalStore
{
    Task<bool> SetToolApprovalAsync(JobDefinition job, bool approved, CancellationToken ct);
    Task<bool> HasToolApprovalAsync(JobDefinition job, CancellationToken ct);
}

public partial class RelationalJobHistoryStore : IJobToolApprovalStore
{
    public async Task<bool> SetToolApprovalAsync(JobDefinition job, bool approved, CancellationToken ct)
    {
        await EnsureInitializedAsync();
        await using var connection = _dialect.CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE jobs
               SET tool_approved_version = CASE WHEN @approved = 1 THEN version ELSE NULL END
             WHERE id = @id AND tenant_id = @tenant AND created_by = @owner
               AND version = @version AND job_type = 'Script' AND target_path IS NULL
            """;
        BindToolApprovalJob(command, job);
        command.AddParam("@approved", approved ? 1 : 0);
        return await command.ExecuteNonQueryAsync(ct) == 1;
    }

    public async Task<bool> HasToolApprovalAsync(JobDefinition job, CancellationToken ct)
    {
        await EnsureInitializedAsync();
        await using var connection = _dialect.CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT 1 FROM jobs
             WHERE id = @id AND tenant_id = @tenant AND created_by = @owner
               AND version = @version AND tool_approved_version = @version
               AND is_enabled = 1 AND job_type = 'Script' AND target_path IS NULL
            """;
        BindToolApprovalJob(command, job);
        return await command.ExecuteScalarAsync(ct) is not null;
    }

    private static void BindToolApprovalJob(System.Data.Common.DbCommand command, JobDefinition job)
    {
        command.AddParam("@id", job.Id.Value);
        command.AddParam("@tenant", job.TenantId);
        command.AddParam("@owner", job.CreatedBy);
        command.AddParam("@version", job.Version);
    }
}
