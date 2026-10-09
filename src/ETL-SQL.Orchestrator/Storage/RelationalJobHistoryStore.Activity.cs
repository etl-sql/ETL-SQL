using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Data;

namespace ETL_SQL.Orchestrator.Storage;

public partial class RelationalJobHistoryStore : IScheduledJobActivityStore
{
    public async Task<IReadOnlyList<ScheduledJobActivity>> GetScheduledJobActivityAsync(
        string? tenantId, DateTimeOffset asOfUtc, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureInitializedAsync();
        asOfUtc = asOfUtc.ToUniversalTime();
        var dayStart = new DateTimeOffset(asOfUtc.UtcDateTime.Date, TimeSpan.Zero);
        using var connection = _dialect.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        // One statement gives both providers a consistent projection. History uses ISO-8601 text
        // with historical local offsets. Fetch adjacent calendar dates, then compare instants in
        // UTC: lexical comparison of complete offset timestamps would misclassify midnight runs.
        command.CommandText = """
            SELECT j.*, h.status AS ActivityOutcome, h.end_time AS ActivityEndTime
              FROM jobs j
              LEFT JOIN job_history h ON h.job_id = j.id AND h.tenant_id = j.tenant_id
                   AND h.end_time >= @candidateStart AND h.end_time < @candidateEnd
                   AND h.status IN ('SUCCESS', 'FAILURE', 'BLOCKED')
             WHERE j.tenant_id = @tenant
             ORDER BY j.name COLLATE NOCASE, j.id, h.id;
            """;
        command.AddParam("@tenant", TenantKey(tenantId));
        command.AddParam("@candidateStart", dayStart.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.AddParam("@candidateEnd", dayStart.AddDays(2).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        var items = new List<ScheduledJobActivity>();
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = JobId.From(reader.GetString(reader.GetOrdinal("id")));
            if (items.Count == 0 || items[^1].Job.Id != id)
            {
                var state = ScheduledJobRunState.Idle;
                var owner = ReadOptionalString(reader, "lease_owner");
                var expiry = ReadOptionalString(reader, "lease_expires_at");
                if (!string.IsNullOrEmpty(owner) && expiry is not null && ParseInstant(expiry) > asOfUtc)
                {
                    var rawState = reader.GetString(reader.GetOrdinal("run_state"));
                    if (!Enum.TryParse(rawState, out state) || !Enum.IsDefined(state))
                        throw new InvalidOperationException("Stored scheduled job run state is invalid.");
                }
                items.Add(new ScheduledJobActivity(ReadJob(reader), state, false, false));
            }

            var ended = ReadOptionalString(reader, "ActivityEndTime");
            if (ended is null) continue;
            var endUtc = ParseInstant(ended);
            if (endUtc < dayStart || endUtc > asOfUtc) continue;
            var status = reader.GetString(reader.GetOrdinal("ActivityOutcome"));
            var current = items[^1];
            items[^1] = current with
            {
                CompletedToday = current.CompletedToday || status == "SUCCESS",
                FailedToday = current.FailedToday || status is "FAILURE" or "BLOCKED"
            };
        }
        return items;
    }

    public async Task<bool> TrySetScheduledJobRunStateAsync(
        JobId jobId, string owner, long fenceToken, ScheduledJobRunState state,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(state)) throw new ArgumentOutOfRangeException(nameof(state));
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureInitializedAsync();
        using var connection = _dialect.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE jobs SET run_state = @state
             WHERE id = @id AND lease_owner = @owner AND lease_fence_token = @token
               AND lease_expires_at > @now;
            """;
        command.AddParam("@id", jobId.Require());
        command.AddParam("@owner", owner);
        command.AddParam("@token", fenceToken);
        command.AddParam("@state", state.ToString());
        command.AddParam("@now", DateTime.UtcNow.ToString("O"));
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static DateTimeOffset ParseInstant(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).ToUniversalTime();
}
