using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using ETL_SQL.Core.Data;

namespace ETL_SQL.Orchestrator.Storage
{
    /// <summary>
    /// <see cref="IJobCatalogStore"/> — schedules, notifications, and their attachments to jobs.
    /// </summary>
    /// <remarks>
    /// Two rules run through everything here.
    /// <para>
    /// <b>Mutations are idempotent.</b> ETL-SQL's configuration is code, and an exported script must
    /// converge when replayed rather than failing on its second run. Attaching a link that exists and
    /// detaching one that does not are both no-ops that report what happened.
    /// </para>
    /// <para>
    /// <b>Deletes restrict rather than cascade</b> for shared objects. Dropping a schedule that three
    /// jobs use would silently unschedule all three; the delete fails and names them instead. Only
    /// deleting the job itself cascades, because its links have no meaning without it.
    /// </para>
    /// </remarks>
    public partial class RelationalJobHistoryStore
    {
        // ── Schedules ─────────────────────────────────────────────────────────────

        public async Task SaveScheduleAsync(ScheduleDefinition schedule)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            // CreatedBy is set on insert only — the point of the column is that it does not move.
            command.CommandText = @"
                INSERT INTO Schedules (Name, Cron, TimeZone, IsEnabled, DisplayName, Description, Options, CreatedBy, ModifiedBy)
                VALUES (@name, @cron, @timeZone, @isEnabled, @displayName, @description, @options, @createdBy, @modifiedBy)
                ON CONFLICT(Name) DO UPDATE SET
                    Cron        = excluded.Cron,
                    TimeZone    = excluded.TimeZone,
                    IsEnabled   = excluded.IsEnabled,
                    DisplayName = excluded.DisplayName,
                    Description = excluded.Description,
                    Options     = excluded.Options,
                    CreatedBy   = COALESCE(Schedules.CreatedBy, excluded.CreatedBy),
                    ModifiedBy  = excluded.ModifiedBy,
                    Version     = Schedules.Version + 1;";
            command.AddParam("@name", schedule.Name);
            command.AddParam("@cron", schedule.Cron);
            command.AddParam("@timeZone", schedule.TimeZone);
            command.AddParam("@isEnabled", schedule.IsEnabled ? 1 : 0);
            command.AddParam("@displayName", (object?)schedule.DisplayName ?? DBNull.Value);
            command.AddParam("@description", (object?)schedule.Description ?? DBNull.Value);
            command.AddParam("@options", (object?)schedule.Options ?? DBNull.Value);
            command.AddParam("@createdBy", (object?)schedule.CreatedBy ?? DBNull.Value);
            command.AddParam("@modifiedBy", (object?)schedule.ModifiedBy ?? DBNull.Value);
            await command.ExecuteNonQueryAsync();
        }

        public async Task<ScheduleDefinition?> GetScheduleAsync(string name)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM Schedules WHERE Name = @name COLLATE NOCASE;";
            command.AddParam("@name", name);
            using var reader = await command.ExecuteReaderAsync();
            return await reader.ReadAsync() ? ReadSchedule(reader) : null;
        }

        public async Task<IReadOnlyList<ScheduleDefinition>> GetSchedulesAsync(int limit = 1000, int offset = 0)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM Schedules ORDER BY Name LIMIT @limit OFFSET @offset;";
            command.AddParam("@limit", Math.Clamp(limit, 1, 10000));
            command.AddParam("@offset", Math.Max(0, offset));

            var results = new List<ScheduleDefinition>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) results.Add(ReadSchedule(reader));
            return results;
        }

        public async Task<IReadOnlyList<string>> DeleteScheduleAsync(string name)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            var blockers = await ReadLinkedJobNamesAsync(
                connection, transaction, "SELECT JobName FROM JobSchedules WHERE ScheduleName = @name COLLATE NOCASE;", name);
            if (blockers.Count > 0)
            {
                transaction.Rollback();
                return blockers;
            }

            using var delete = connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM Schedules WHERE Name = @name COLLATE NOCASE;";
            delete.AddParam("@name", name);
            await delete.ExecuteNonQueryAsync();

            transaction.Commit();
            return Array.Empty<string>();
        }

        public Task<bool> SetScheduleEnabledAsync(string name, bool isEnabled) =>
            SetEnabledAsync("Schedules", name, isEnabled);

        private static ScheduleDefinition ReadSchedule(DbDataReader reader) => new(
            reader.GetString(reader.GetOrdinal("Name")),
            reader.GetString(reader.GetOrdinal("Cron")),
            reader.GetString(reader.GetOrdinal("TimeZone")),
            reader.GetInt32(reader.GetOrdinal("IsEnabled")) == 1,
            ReadOptionalString(reader, "DisplayName"),
            ReadOptionalString(reader, "Description"),
            ReadOptionalString(reader, "Options"),
            ReadOptionalString(reader, "CreatedBy"),
            ReadOptionalString(reader, "ModifiedBy"),
            reader.GetInt64(reader.GetOrdinal("Version")));

        // ── Notifications ─────────────────────────────────────────────────────────

        public async Task SaveNotificationAsync(NotificationDefinition notification)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO Notifications (Name, ConnectionName, Recipient, IsEnabled, DisplayName, Description, Options, CreatedBy, ModifiedBy)
                VALUES (@name, @connectionName, @recipient, @isEnabled, @displayName, @description, @options, @createdBy, @modifiedBy)
                ON CONFLICT(Name) DO UPDATE SET
                    ConnectionName = excluded.ConnectionName,
                    Recipient      = excluded.Recipient,
                    IsEnabled      = excluded.IsEnabled,
                    DisplayName    = excluded.DisplayName,
                    Description    = excluded.Description,
                    Options        = excluded.Options,
                    CreatedBy      = COALESCE(Notifications.CreatedBy, excluded.CreatedBy),
                    ModifiedBy     = excluded.ModifiedBy,
                    Version        = Notifications.Version + 1;";
            command.AddParam("@name", notification.Name);
            command.AddParam("@connectionName", notification.ConnectionName);
            command.AddParam("@recipient", (object?)notification.Recipient ?? DBNull.Value);
            command.AddParam("@isEnabled", notification.IsEnabled ? 1 : 0);
            command.AddParam("@displayName", (object?)notification.DisplayName ?? DBNull.Value);
            command.AddParam("@description", (object?)notification.Description ?? DBNull.Value);
            command.AddParam("@options", (object?)notification.Options ?? DBNull.Value);
            command.AddParam("@createdBy", (object?)notification.CreatedBy ?? DBNull.Value);
            command.AddParam("@modifiedBy", (object?)notification.ModifiedBy ?? DBNull.Value);
            await command.ExecuteNonQueryAsync();
        }

        public async Task<NotificationDefinition?> GetNotificationAsync(string name)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM Notifications WHERE Name = @name COLLATE NOCASE;";
            command.AddParam("@name", name);
            using var reader = await command.ExecuteReaderAsync();
            return await reader.ReadAsync() ? ReadNotification(reader) : null;
        }

        public async Task<IReadOnlyList<NotificationDefinition>> GetNotificationsAsync(int limit = 1000, int offset = 0)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM Notifications ORDER BY Name LIMIT @limit OFFSET @offset;";
            command.AddParam("@limit", Math.Clamp(limit, 1, 10000));
            command.AddParam("@offset", Math.Max(0, offset));

            var results = new List<NotificationDefinition>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) results.Add(ReadNotification(reader));
            return results;
        }

        public async Task<IReadOnlyList<string>> DeleteNotificationAsync(string name)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            var blockers = await ReadLinkedJobNamesAsync(
                connection, transaction,
                "SELECT DISTINCT JobName FROM JobNotifications WHERE NotificationName = @name COLLATE NOCASE;", name);
            if (blockers.Count > 0)
            {
                transaction.Rollback();
                return blockers;
            }

            using var delete = connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM Notifications WHERE Name = @name COLLATE NOCASE;";
            delete.AddParam("@name", name);
            await delete.ExecuteNonQueryAsync();

            transaction.Commit();
            return Array.Empty<string>();
        }

        public Task<bool> SetNotificationEnabledAsync(string name, bool isEnabled) =>
            SetEnabledAsync("Notifications", name, isEnabled);

        private static NotificationDefinition ReadNotification(DbDataReader reader) => new(
            reader.GetString(reader.GetOrdinal("Name")),
            reader.GetString(reader.GetOrdinal("ConnectionName")),
            ReadOptionalString(reader, "Recipient"),
            reader.GetInt32(reader.GetOrdinal("IsEnabled")) == 1,
            ReadOptionalString(reader, "DisplayName"),
            ReadOptionalString(reader, "Description"),
            ReadOptionalString(reader, "Options"),
            ReadOptionalString(reader, "CreatedBy"),
            ReadOptionalString(reader, "ModifiedBy"),
            reader.GetInt64(reader.GetOrdinal("Version")));

        // ── Job ↔ Schedule ────────────────────────────────────────────────────────

        public async Task<bool> AddJobScheduleAsync(string jobName, string scheduleName, DateTime? nextRun)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            // DO NOTHING rather than DO UPDATE: re-running an export script must not reset the run
            // state of a link that is already armed and firing.
            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO JobSchedules (JobName, ScheduleName, LastRun, NextRun)
                VALUES (@jobName, @scheduleName, NULL, @nextRun)
                ON CONFLICT(JobName, ScheduleName) DO NOTHING;";
            command.AddParam("@jobName", jobName);
            command.AddParam("@scheduleName", scheduleName);
            command.AddParam("@nextRun", (object?)nextRun?.ToString("O") ?? DBNull.Value);
            return await command.ExecuteNonQueryAsync() == 1;
        }

        public async Task<bool> RemoveJobScheduleAsync(string jobName, string scheduleName)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                DELETE FROM JobSchedules
                WHERE JobName = @jobName COLLATE NOCASE AND ScheduleName = @scheduleName COLLATE NOCASE;";
            command.AddParam("@jobName", jobName);
            command.AddParam("@scheduleName", scheduleName);
            return await command.ExecuteNonQueryAsync() > 0;
        }

        public async Task<IReadOnlyList<JobScheduleLink>> GetJobSchedulesAsync(string? jobName = null)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = jobName is null
                ? "SELECT * FROM JobSchedules ORDER BY JobName, ScheduleName;"
                : "SELECT * FROM JobSchedules WHERE JobName = @jobName COLLATE NOCASE ORDER BY ScheduleName;";
            if (jobName is not null) command.AddParam("@jobName", jobName);

            var results = new List<JobScheduleLink>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(new JobScheduleLink(
                    reader.GetString(reader.GetOrdinal("JobName")),
                    reader.GetString(reader.GetOrdinal("ScheduleName")),
                    ParseOptionalTimestamp(reader, "LastRun"),
                    ParseOptionalTimestamp(reader, "NextRun")));
            }
            return results;
        }

        public async Task UpdateJobScheduleRunAsync(string jobName, string scheduleName, DateTime lastRun, DateTime? nextRun)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE JobSchedules SET LastRun = @lastRun, NextRun = @nextRun
                WHERE JobName = @jobName COLLATE NOCASE AND ScheduleName = @scheduleName COLLATE NOCASE;";
            command.AddParam("@lastRun", lastRun.ToString("O"));
            command.AddParam("@nextRun", (object?)nextRun?.ToString("O") ?? DBNull.Value);
            command.AddParam("@jobName", jobName);
            command.AddParam("@scheduleName", scheduleName);
            await command.ExecuteNonQueryAsync();
        }

        public async Task ArmJobScheduleAsync(string jobName, string scheduleName, DateTime? nextRun)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE JobSchedules SET NextRun = @nextRun
                WHERE JobName = @jobName COLLATE NOCASE AND ScheduleName = @scheduleName COLLATE NOCASE;";
            command.AddParam("@nextRun", (object?)nextRun?.ToString("O") ?? DBNull.Value);
            command.AddParam("@jobName", jobName);
            command.AddParam("@scheduleName", scheduleName);
            await command.ExecuteNonQueryAsync();
        }

        public async Task<IReadOnlyList<JobDefinition>> GetJobsDueByScheduleAsync(DateTime nowUtc)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            // DISTINCT is the coalescing rule: two links of one job falling due together are one
            // occurrence of that job, not two concurrent runs of it.
            // NextRun IS NOT NULL is deliberate — see IJobCatalogStore for why "no next occurrence"
            // must not mean "run now".
            command.CommandText = @"
                SELECT DISTINCT j.* FROM Jobs j
                JOIN JobSchedules js ON js.JobName = j.Name COLLATE NOCASE
                JOIN Schedules s ON s.Name = js.ScheduleName COLLATE NOCASE
                WHERE j.IsEnabled = 1
                  AND s.IsEnabled = 1
                  AND js.NextRun IS NOT NULL
                  AND js.NextRun <= @now;";
            command.AddParam("@now", nowUtc.ToString("O"));

            var jobs = new List<JobDefinition>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) jobs.Add(ReadJob(reader));
            return jobs;
        }

        // ── Job ↔ Notification ────────────────────────────────────────────────────

        public async Task<bool> AddJobNotificationAsync(string jobName, string notificationName, NotificationTrigger trigger)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            var conflicting = await FindOverlappingTriggerAsync(connection, jobName, notificationName, trigger);
            if (conflicting is not null)
                throw new InvalidOperationException(
                    $"Job '{jobName}' already notifies '{notificationName}' ON {conflicting.Value.ToString().ToUpperInvariant()}, " +
                    $"which overlaps ON {trigger.ToString().ToUpperInvariant()} — COMPLETION covers both SUCCESS and FAILURE, " +
                    "so the pair would deliver twice for the same run. Remove one before adding the other.");

            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO JobNotifications (JobName, NotificationName, TriggerCondition)
                VALUES (@jobName, @notificationName, @trigger)
                ON CONFLICT(JobName, NotificationName, TriggerCondition) DO NOTHING;";
            command.AddParam("@jobName", jobName);
            command.AddParam("@notificationName", notificationName);
            command.AddParam("@trigger", trigger.ToString());
            return await command.ExecuteNonQueryAsync() == 1;
        }

        public async Task<bool> RemoveJobNotificationAsync(string jobName, string notificationName, NotificationTrigger trigger)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                DELETE FROM JobNotifications
                WHERE JobName = @jobName COLLATE NOCASE
                  AND NotificationName = @notificationName COLLATE NOCASE
                  AND TriggerCondition = @trigger COLLATE NOCASE;";
            command.AddParam("@jobName", jobName);
            command.AddParam("@notificationName", notificationName);
            command.AddParam("@trigger", trigger.ToString());
            return await command.ExecuteNonQueryAsync() > 0;
        }

        public async Task<IReadOnlyList<JobNotificationLink>> GetJobNotificationsAsync(string? jobName = null)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = jobName is null
                ? "SELECT * FROM JobNotifications ORDER BY JobName, NotificationName, TriggerCondition;"
                : "SELECT * FROM JobNotifications WHERE JobName = @jobName COLLATE NOCASE ORDER BY NotificationName, TriggerCondition;";
            if (jobName is not null) command.AddParam("@jobName", jobName);

            var results = new List<JobNotificationLink>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var raw = reader.GetString(reader.GetOrdinal("TriggerCondition"));
                if (!Enum.TryParse<NotificationTrigger>(raw, ignoreCase: true, out var trigger))
                    continue;
                results.Add(new JobNotificationLink(
                    reader.GetString(reader.GetOrdinal("JobName")),
                    reader.GetString(reader.GetOrdinal("NotificationName")),
                    trigger));
            }
            return results;
        }

        /// <summary>
        /// Returns the already-attached trigger that would double-deliver alongside
        /// <paramref name="trigger"/>, or <c>null</c> when there is none. <c>COMPLETION</c> is the union
        /// of the other two, so it conflicts with both and they conflict with it.
        /// </summary>
        private static async Task<NotificationTrigger?> FindOverlappingTriggerAsync(
            DbConnection connection, string jobName, string notificationName, NotificationTrigger trigger)
        {
            var opposed = trigger == NotificationTrigger.Completion
                ? new[] { NotificationTrigger.Success, NotificationTrigger.Failure }
                : new[] { NotificationTrigger.Completion };

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT TriggerCondition FROM JobNotifications
                WHERE JobName = @jobName COLLATE NOCASE
                  AND NotificationName = @notificationName COLLATE NOCASE;";
            command.AddParam("@jobName", jobName);
            command.AddParam("@notificationName", notificationName);

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                if (!Enum.TryParse<NotificationTrigger>(reader.GetString(0), ignoreCase: true, out var existing))
                    continue;
                if (Array.IndexOf(opposed, existing) >= 0) return existing;
            }
            return null;
        }

        // ── Shared helpers ────────────────────────────────────────────────────────

        private async Task<bool> SetEnabledAsync(string table, string name, bool isEnabled)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            // The table name is a compile-time literal from this class, never caller input.
            command.CommandText = $@"
                UPDATE {table} SET IsEnabled = @isEnabled, Version = Version + 1
                WHERE Name = @name COLLATE NOCASE;";
            command.AddParam("@isEnabled", isEnabled ? 1 : 0);
            command.AddParam("@name", name);
            return await command.ExecuteNonQueryAsync() > 0;
        }

        private static async Task<List<string>> ReadLinkedJobNamesAsync(
            DbConnection connection, DbTransaction transaction, string sql, string name)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.AddParam("@name", name);

            var names = new List<string>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) names.Add(reader.GetString(0));
            return names;
        }

        private static DateTime? ParseOptionalTimestamp(DbDataReader reader, string column)
        {
            var ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? null : DateTime.Parse(
                reader.GetString(ordinal), null, System.Globalization.DateTimeStyles.RoundtripKind);
        }

        /// <summary>
        /// Removes a job's schedule and notification attachments. Called from job deletion, where the
        /// links cascade because they have no meaning without the job.
        /// </summary>
        private static async Task DeleteJobLinksAsync(DbConnection connection, DbTransaction transaction, string jobName)
        {
            foreach (var sql in new[]
                     {
                         "DELETE FROM JobSchedules WHERE JobName = @name COLLATE NOCASE;",
                         "DELETE FROM JobNotifications WHERE JobName = @name COLLATE NOCASE;"
                     })
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = sql;
                command.AddParam("@name", jobName);
                await command.ExecuteNonQueryAsync();
            }
        }
    }
}
