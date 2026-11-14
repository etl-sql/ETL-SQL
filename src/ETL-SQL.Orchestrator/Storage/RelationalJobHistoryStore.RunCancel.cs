using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Data;

namespace ETL_SQL.Orchestrator.Storage;

public partial class RelationalJobHistoryStore : IJobRunCancelStore
{
    // A request for a run that ended before its node saw it would otherwise stay forever.
    private static readonly TimeSpan RunCancelRetention = TimeSpan.FromDays(1);

    public async Task RequestRunCancelAsync(long historyId, string requestedBy, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        using var connection = _dialect.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var now = DateTime.UtcNow;

        using (var prune = connection.CreateCommand())
        {
            prune.CommandText = "DELETE FROM job_run_cancel_requests WHERE requested_at < @cutoff;";
            prune.AddParam("@cutoff", now.Subtract(RunCancelRetention).ToString("O"));
            await prune.ExecuteNonQueryAsync(cancellationToken);
        }

        using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO job_run_cancel_requests (history_id, requested_by, requested_at)
            VALUES (@id, @by, @at)
            ON CONFLICT(history_id) DO NOTHING;";
        command.AddParam("@id", historyId);
        command.AddParam("@by", requestedBy);
        command.AddParam("@at", now.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<long>> GetRequestedRunCancelsAsync(
        IReadOnlyCollection<long> historyIds, CancellationToken cancellationToken = default)
    {
        if (historyIds.Count == 0) return Array.Empty<long>();
        await EnsureInitializedAsync();
        using var connection = _dialect.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        var names = historyIds.Select((id, i) => (Name: "@h" + i, Id: id)).ToList();
        command.CommandText =
            $"SELECT history_id FROM job_run_cancel_requests WHERE history_id IN ({string.Join(", ", names.Select(n => n.Name))});";
        foreach (var (name, id) in names) command.AddParam(name, id);

        var requested = new List<long>();
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            requested.Add(Convert.ToInt64(reader.GetValue(0)));
        return requested;
    }

    public async Task ClearRunCancelAsync(long historyId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        using var connection = _dialect.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM job_run_cancel_requests WHERE history_id = @id;";
        command.AddParam("@id", historyId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
