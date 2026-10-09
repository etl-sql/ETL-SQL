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
            // That includes filling in a missing one: an object with no owner is administrators-only
            // until it is adopted, and adoption is an explicit audited act rather than a side effect
            // of whoever edited it next.
            command.CommandText = @"
                INSERT INTO schedules (id, name, cron, time_zone, is_enabled, display_name, description, options, created_by, modified_by, tenant_id)
                VALUES (@id, @name, @cron, @timeZone, @isEnabled, @displayName, @description, @options, @createdBy, @modifiedBy, @tenantId)
                ON CONFLICT(tenant_id, name) DO UPDATE SET
                    cron        = excluded.cron,
                    time_zone    = excluded.time_zone,
                    is_enabled   = excluded.is_enabled,
                    display_name = excluded.display_name,
                    description = excluded.description,
                    options     = excluded.options,
                    modified_by  = excluded.modified_by,
                    version     = schedules.version + 1;";
            command.AddParam("@id", NewOrExistingId(schedule.Id));
            command.AddParam("@name", schedule.Name);
            command.AddParam("@tenantId", TenantKey(schedule.TenantId));
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

        public async Task<ScheduleDefinition?> GetScheduleAsync(string? tenantId, string name)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT * FROM schedules WHERE tenant_id = @tenant AND name = @name COLLATE NOCASE;";
            command.AddParam("@tenant", TenantKey(tenantId));
            command.AddParam("@name", name);
            using var reader = await command.ExecuteReaderAsync();
            return await reader.ReadAsync() ? ReadSchedule(reader) : null;
        }

        public async Task<ScheduleDefinition?> GetScheduleByIdAsync(ScheduleId scheduleIdRef)
        {
            var scheduleId = scheduleIdRef.Require();
            if (string.IsNullOrWhiteSpace(scheduleId)) return null;
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM schedules WHERE id = @id;";
            command.AddParam("@id", scheduleId);
            using var reader = await command.ExecuteReaderAsync();
            return await reader.ReadAsync() ? ReadSchedule(reader) : null;
        }

        public async Task<IReadOnlyList<ScheduleDefinition>> GetSchedulesAsync(int limit = 1000, int offset = 0)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM schedules ORDER BY name LIMIT @limit OFFSET @offset;";
            command.AddParam("@limit", Math.Clamp(limit, 1, 10000));
            command.AddParam("@offset", Math.Max(0, offset));

            var results = new List<ScheduleDefinition>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) results.Add(ReadSchedule(reader));
            return results;
        }

        public async Task<IReadOnlyList<string>> DeleteScheduleAsync(ScheduleId scheduleIdRef)
        {
            var scheduleId = scheduleIdRef.Require();
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            // Blockers are reported by name because that is what an operator can act on, but they are
            // found by id — the link table no longer stores names at all.
            var blockers = await ReadLinkedJobNamesAsync(
                connection, transaction,
                @"SELECT j.name FROM job_schedules js
                  INNER JOIN jobs j ON j.id = js.job_id
                  WHERE js.schedule_id = @id;", scheduleId);
            if (blockers.Count > 0)
            {
                transaction.Rollback();
                return blockers;
            }

            using var delete = connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM schedules WHERE id = @id;";
            delete.AddParam("@id", scheduleId);
            await delete.ExecuteNonQueryAsync();

            transaction.Commit();
            return Array.Empty<string>();
        }

        public Task<bool> SetScheduleEnabledAsync(ScheduleId scheduleId, bool isEnabled) =>
            SetEnabledAsync("schedules", scheduleId.Require(), isEnabled);

        private static ScheduleDefinition ReadSchedule(DbDataReader reader) => new(
            reader.GetString(reader.GetOrdinal("name")),
            reader.GetString(reader.GetOrdinal("cron")),
            reader.GetString(reader.GetOrdinal("time_zone")),
            reader.GetInt32(reader.GetOrdinal("is_enabled")) == 1,
            ReadOptionalString(reader, "display_name"),
            ReadOptionalString(reader, "description"),
            ReadOptionalString(reader, "options"),
            ReadOptionalString(reader, "created_by"),
            ReadOptionalString(reader, "modified_by"),
            reader.GetInt64(reader.GetOrdinal("version")),
            TenantOrNull(ReadOptionalString(reader, "tenant_id")),
            ScheduleId.From(ReadOptionalString(reader, "id")));

        // ── Notifications ─────────────────────────────────────────────────────────

        public async Task SaveNotificationAsync(NotificationDefinition notification)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO notifications (id, name, connection_name, recipient, is_enabled, display_name, description, options, created_by, modified_by, tenant_id)
                VALUES (@id, @name, @connectionName, @recipient, @isEnabled, @displayName, @description, @options, @createdBy, @modifiedBy, @tenantId)
                ON CONFLICT(tenant_id, name) DO UPDATE SET
                    connection_name = excluded.connection_name,
                    recipient      = excluded.recipient,
                    is_enabled      = excluded.is_enabled,
                    display_name    = excluded.display_name,
                    description    = excluded.description,
                    options        = excluded.options,
                    modified_by     = excluded.modified_by,
                    version        = notifications.version + 1;";
            command.AddParam("@id", NewOrExistingId(notification.Id));
            command.AddParam("@name", notification.Name);
            command.AddParam("@tenantId", TenantKey(notification.TenantId));
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

        public async Task<NotificationDefinition?> GetNotificationAsync(string? tenantId, string name)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT * FROM notifications WHERE tenant_id = @tenant AND name = @name COLLATE NOCASE;";
            command.AddParam("@tenant", TenantKey(tenantId));
            command.AddParam("@name", name);
            using var reader = await command.ExecuteReaderAsync();
            return await reader.ReadAsync() ? ReadNotification(reader) : null;
        }

        public async Task<NotificationDefinition?> GetNotificationByIdAsync(NotificationId notificationIdRef)
        {
            var notificationId = notificationIdRef.Require();
            if (string.IsNullOrWhiteSpace(notificationId)) return null;
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM notifications WHERE id = @id;";
            command.AddParam("@id", notificationId);
            using var reader = await command.ExecuteReaderAsync();
            return await reader.ReadAsync() ? ReadNotification(reader) : null;
        }

        public async Task<IReadOnlyList<NotificationDefinition>> GetNotificationsAsync(int limit = 1000, int offset = 0)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM notifications ORDER BY name LIMIT @limit OFFSET @offset;";
            command.AddParam("@limit", Math.Clamp(limit, 1, 10000));
            command.AddParam("@offset", Math.Max(0, offset));

            var results = new List<NotificationDefinition>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) results.Add(ReadNotification(reader));
            return results;
        }

        public async Task<IReadOnlyList<string>> DeleteNotificationAsync(NotificationId notificationIdRef)
        {
            var notificationId = notificationIdRef.Require();
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            var blockers = await ReadLinkedJobNamesAsync(
                connection, transaction,
                @"SELECT DISTINCT j.name FROM job_notifications jn
                   INNER JOIN jobs j ON j.id = jn.job_id
                   WHERE jn.notification_id = @id;", notificationId);
            if (blockers.Count > 0)
            {
                transaction.Rollback();
                return blockers;
            }

            using var delete = connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM notifications WHERE id = @id;";
            delete.AddParam("@id", notificationId);
            await delete.ExecuteNonQueryAsync();

            transaction.Commit();
            return Array.Empty<string>();
        }

        public Task<bool> SetNotificationEnabledAsync(NotificationId notificationId, bool isEnabled) =>
            SetEnabledAsync("notifications", notificationId.Require(), isEnabled);

        private static NotificationDefinition ReadNotification(DbDataReader reader) => new(
            reader.GetString(reader.GetOrdinal("name")),
            reader.GetString(reader.GetOrdinal("connection_name")),
            ReadOptionalString(reader, "recipient"),
            reader.GetInt32(reader.GetOrdinal("is_enabled")) == 1,
            ReadOptionalString(reader, "display_name"),
            ReadOptionalString(reader, "description"),
            ReadOptionalString(reader, "options"),
            ReadOptionalString(reader, "created_by"),
            ReadOptionalString(reader, "modified_by"),
            reader.GetInt64(reader.GetOrdinal("version")),
            TenantOrNull(ReadOptionalString(reader, "tenant_id")),
            NotificationId.From(ReadOptionalString(reader, "id")));

        // ── Job ↔ Schedule ────────────────────────────────────────────────────────

        public async Task<bool> AddJobScheduleAsync(JobId jobIdRef, ScheduleId scheduleIdRef, DateTime? nextRun)
        {
            var jobId = jobIdRef.Require();
            var scheduleId = scheduleIdRef.Require();
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            // DO NOTHING rather than DO UPDATE: re-running an export script must not reset the run
            // state of a link that is already armed and firing.
            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO job_schedules (job_id, schedule_id, last_run, next_run)
                VALUES (@jobId, @scheduleId, NULL, @nextRun)
                ON CONFLICT(job_id, schedule_id) DO NOTHING;";
            command.AddParam("@jobId", jobId);
            command.AddParam("@scheduleId", scheduleId);
            command.AddParam("@nextRun", (object?)nextRun?.ToString("O") ?? DBNull.Value);
            return await command.ExecuteNonQueryAsync() == 1;
        }

        public async Task<bool> RemoveJobScheduleAsync(JobId jobIdRef, ScheduleId scheduleIdRef)
        {
            var jobId = jobIdRef.Require();
            var scheduleId = scheduleIdRef.Require();
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                DELETE FROM job_schedules WHERE job_id = @jobId AND schedule_id = @scheduleId;";
            command.AddParam("@jobId", jobId);
            command.AddParam("@scheduleId", scheduleId);
            return await command.ExecuteNonQueryAsync() > 0;
        }

        public async Task<IReadOnlyList<JobScheduleLink>> GetJobSchedulesAsync(JobId jobIdRef = default)
        {
            var jobId = jobIdRef.IsAssigned ? jobIdRef.Value : null;
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            // Names are joined in for display only. They are never the key: two tenants may each
            // have a schedule called 'nightly', and a link belongs to exactly one of them.
            using var command = connection.CreateCommand();
            var select = @"
                SELECT js.job_id, js.schedule_id, js.last_run, js.next_run,
                       j.name AS job_name, s.name AS schedule_name
                FROM job_schedules js
                INNER JOIN jobs j ON j.id = js.job_id
                INNER JOIN schedules s ON s.id = js.schedule_id ";
            command.CommandText = jobId is null
                ? select + "ORDER BY j.name, s.name;"
                : select + "WHERE js.job_id = @jobId ORDER BY s.name;";
            if (jobId is not null) command.AddParam("@jobId", jobId);

            var results = new List<JobScheduleLink>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(new JobScheduleLink(
                    JobId.From(reader.GetString(reader.GetOrdinal("job_id"))),
                    ScheduleId.From(reader.GetString(reader.GetOrdinal("schedule_id"))),
                    ParseOptionalTimestamp(reader, "last_run"),
                    ParseOptionalTimestamp(reader, "next_run"),
                    ReadOptionalString(reader, "job_name"),
                    ReadOptionalString(reader, "schedule_name")));
            }
            return results;
        }

        public async Task UpdateJobScheduleRunAsync(JobId jobIdRef, ScheduleId scheduleIdRef, DateTime lastRun, DateTime? nextRun)
        {
            var jobId = jobIdRef.Require();
            var scheduleId = scheduleIdRef.Require();
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE job_schedules SET last_run = @lastRun, next_run = @nextRun
                WHERE job_id = @jobId AND schedule_id = @scheduleId;";
            command.AddParam("@lastRun", lastRun.ToString("O"));
            command.AddParam("@nextRun", (object?)nextRun?.ToString("O") ?? DBNull.Value);
            command.AddParam("@jobId", jobId);
            command.AddParam("@scheduleId", scheduleId);
            await command.ExecuteNonQueryAsync();
        }

        public async Task ArmJobScheduleAsync(JobId jobIdRef, ScheduleId scheduleIdRef, DateTime? nextRun)
        {
            var jobId = jobIdRef.Require();
            var scheduleId = scheduleIdRef.Require();
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE job_schedules SET next_run = @nextRun
                WHERE job_id = @jobId AND schedule_id = @scheduleId;";
            command.AddParam("@nextRun", (object?)nextRun?.ToString("O") ?? DBNull.Value);
            command.AddParam("@jobId", jobId);
            command.AddParam("@scheduleId", scheduleId);
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
                SELECT DISTINCT j.* FROM jobs j
                JOIN job_schedules js ON js.job_id = j.id
                JOIN schedules s ON s.id = js.schedule_id
                WHERE j.is_enabled = 1
                  AND s.is_enabled = 1
                  AND js.next_run IS NOT NULL
                  AND js.next_run <= @now;";
            command.AddParam("@now", nowUtc.ToString("O"));

            var jobs = new List<JobDefinition>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) jobs.Add(ReadJob(reader));
            return jobs;
        }

        // ── Job ↔ Notification ────────────────────────────────────────────────────

        public async Task<bool> AddJobNotificationAsync(JobId jobIdRef, NotificationId notificationIdRef, NotificationTrigger trigger)
        {
            var jobId = jobIdRef.Require();
            var notificationId = notificationIdRef.Require();
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            var conflicting = await FindOverlappingTriggerAsync(connection, jobId, notificationId, trigger);
            if (conflicting is not null)
                throw new InvalidOperationException(
                    $"This job already notifies that destination ON {conflicting.Value.ToString().ToUpperInvariant()}, " +
                    $"which overlaps ON {trigger.ToString().ToUpperInvariant()} — COMPLETION covers both SUCCESS and FAILURE, " +
                    "so the pair would deliver twice for the same run. Remove one before adding the other.");

            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO job_notifications (job_id, notification_id, trigger_condition)
                VALUES (@jobId, @notificationId, @trigger)
                ON CONFLICT(job_id, notification_id, trigger_condition) DO NOTHING;";
            command.AddParam("@jobId", jobId);
            command.AddParam("@notificationId", notificationId);
            command.AddParam("@trigger", trigger.ToString());
            return await command.ExecuteNonQueryAsync() == 1;
        }

        public async Task<bool> RemoveJobNotificationAsync(JobId jobIdRef, NotificationId notificationIdRef, NotificationTrigger trigger)
        {
            var jobId = jobIdRef.Require();
            var notificationId = notificationIdRef.Require();
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                DELETE FROM job_notifications
                WHERE job_id = @jobId AND notification_id = @notificationId
                  AND trigger_condition = @trigger COLLATE NOCASE;";
            command.AddParam("@jobId", jobId);
            command.AddParam("@notificationId", notificationId);
            command.AddParam("@trigger", trigger.ToString());
            return await command.ExecuteNonQueryAsync() > 0;
        }

        public async Task<IReadOnlyList<JobNotificationLink>> GetJobNotificationsAsync(JobId jobIdRef = default)
        {
            var jobId = jobIdRef.IsAssigned ? jobIdRef.Value : null;
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            var select = @"
                SELECT jn.job_id, jn.notification_id, jn.trigger_condition,
                       j.name AS job_name, n.name AS notification_name
                FROM job_notifications jn
                INNER JOIN jobs j ON j.id = jn.job_id
                INNER JOIN notifications n ON n.id = jn.notification_id ";
            command.CommandText = jobId is null
                ? select + "ORDER BY j.name, n.name, jn.trigger_condition;"
                : select + "WHERE jn.job_id = @jobId ORDER BY n.name, jn.trigger_condition;";
            if (jobId is not null) command.AddParam("@jobId", jobId);

            var results = new List<JobNotificationLink>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var raw = reader.GetString(reader.GetOrdinal("trigger_condition"));
                if (!Enum.TryParse<NotificationTrigger>(raw, ignoreCase: true, out var trigger))
                    continue;
                results.Add(new JobNotificationLink(
                    JobId.From(reader.GetString(reader.GetOrdinal("job_id"))),
                    NotificationId.From(reader.GetString(reader.GetOrdinal("notification_id"))),
                    trigger,
                    ReadOptionalString(reader, "job_name"),
                    ReadOptionalString(reader, "notification_name")));
            }
            return results;
        }

        /// <summary>
        /// Returns the already-attached trigger that would double-deliver alongside
        /// <paramref name="trigger"/>, or <c>null</c> when there is none. <c>COMPLETION</c> is the union
        /// of the other two, so it conflicts with both and they conflict with it.
        /// </summary>
        private static async Task<NotificationTrigger?> FindOverlappingTriggerAsync(
            DbConnection connection, string jobId, string notificationId, NotificationTrigger trigger)
        {
            var opposed = trigger == NotificationTrigger.Completion
                ? new[] { NotificationTrigger.Success, NotificationTrigger.Failure }
                : new[] { NotificationTrigger.Completion };

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT trigger_condition FROM job_notifications
                WHERE job_id = @jobId AND notification_id = @notificationId;";
            command.AddParam("@jobId", jobId);
            command.AddParam("@notificationId", notificationId);

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

        private async Task<bool> SetEnabledAsync(string table, string objectId, bool isEnabled)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            // The table name is a compile-time literal from this class, never caller input.
            command.CommandText = $@"
                UPDATE {table} SET is_enabled = @isEnabled, version = version + 1
                WHERE id = @id;";
            command.AddParam("@isEnabled", isEnabled ? 1 : 0);
            command.AddParam("@id", objectId);
            return await command.ExecuteNonQueryAsync() > 0;
        }

        private static async Task<List<string>> ReadLinkedJobNamesAsync(
            DbConnection connection, DbTransaction transaction, string sql, string objectId)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.AddParam("@id", objectId);

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
        private static async Task DeleteJobLinksAsync(DbConnection connection, DbTransaction transaction, string jobId)
        {
            foreach (var sql in new[]
                     {
                         "DELETE FROM job_schedules WHERE job_id = @id;",
                         "DELETE FROM job_notifications WHERE job_id = @id;"
                     })
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = sql;
                command.AddParam("@id", jobId);
                await command.ExecuteNonQueryAsync();
            }
        }
    }
}
