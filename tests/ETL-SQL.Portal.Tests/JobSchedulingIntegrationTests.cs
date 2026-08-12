using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Governance;
using ETL_SQL.Orchestrator.Scheduling;
using ETL_SQL.Orchestrator.Service;
using ETL_SQL.Orchestrator.Storage;
using ETL_SQL.Tests.Integration.Connectors;
using ETL_SQL.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Portal.Tests
{
    [Collection("SMTP collection")]
    [Trait("Category", "Integration")]
    public class JobSchedulingIntegrationTests
    {
        private readonly SmtpFixture _smtp;
        private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

        public JobSchedulingIntegrationTests(SmtpFixture smtp)
        {
            _smtp = smtp;
        }

        private async Task<List<JobHistoryEntry>> PollHistoryUntilCountAsync(HttpClient client, string jobName, int expectedCount, int timeoutSeconds = 90)
        {
            var observed = await LoadAwareWait.UntilAsync(
                $"{expectedCount} completed Orchestrator history entries for job '{jobName}'",
                async _ =>
                {
                    using var req = Authorized(HttpMethod.Get, $"/api/scheduled-jobs/{Uri.EscapeDataString(jobName)}/history");
                    var res = await client.SendAsync(req);
                    var history = res.StatusCode == HttpStatusCode.OK
                        ? await res.Content.ReadFromJsonAsync<List<JobHistoryEntry>>(_jsonOptions) ?? []
                        : [];
                    return (res.StatusCode, History: history);
                },
                state => state.StatusCode == HttpStatusCode.OK
                         && state.History.Count >= expectedCount
                         && state.History.Take(expectedCount).All(h => h.EndTime != null),
                TimeSpan.FromSeconds(timeoutSeconds),
                TimeSpan.FromMilliseconds(500),
                state => $"HTTP={(int)state.StatusCode}; count={state.History.Count}; statuses=[{string.Join(',', state.History.Select(h => h.Status))}]");
            return observed.History;
        }

        private static HttpRequestMessage Authorized(HttpMethod method, string uri, object? body = null)
        {
            var req = new HttpRequestMessage(method, uri);
            req.Headers.Add("X-Orchestrator-Key", "test-orch-key-12345");
            if (body != null) req.Content = JsonContent.Create(body);
            return req;
        }

        private static async Task CreateJobAsync(HttpClient client, string jobName, string scriptText, int interval = 10, string unit = "MINUTE", int maxRetries = 0, int retryDelaySeconds = 30)
        {
            var request = new
            {
                Name = jobName,
                ScriptText = scriptText,
                Interval = interval,
                Unit = unit,
                MaxRetries = maxRetries,
                RetryDelaySeconds = retryDelaySeconds,
                HashPolicy = "Warn"
            };

            using var createReq = Authorized(HttpMethod.Post, "/api/scheduled-jobs", request);
            var createRes = await client.SendAsync(createReq);
            Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        }

        private static async Task TriggerJobAsync(HttpClient client, string jobName)
        {
            using var triggerReq = Authorized(HttpMethod.Post, $"/api/scheduled-jobs/{Uri.EscapeDataString(jobName)}/trigger");
            var triggerRes = await client.SendAsync(triggerReq);
            Assert.Equal(HttpStatusCode.Accepted, triggerRes.StatusCode);
        }

        private static async Task<JsonElement> GetHistoryJsonAsync(HttpClient client, string jobName)
        {
            using var req = Authorized(HttpMethod.Get, $"/api/scheduled-jobs/{Uri.EscapeDataString(jobName)}/history");
            var res = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            return doc.RootElement.Clone();
        }

        private static async Task PollSchedulerIdleAsync(HttpClient client, int timeoutSeconds = 30)
        {
            await LoadAwareWait.UntilAsync(
                "scheduler metrics to report no active or queued jobs",
                async _ =>
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, "/metrics");
                    var res = await client.SendAsync(req);
                    if (res.StatusCode == HttpStatusCode.OK)
                    {
                        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
                        var root = doc.RootElement;
                        return (res.StatusCode,
                            Active: root.GetProperty("active_jobs").GetInt32(),
                            Queued: root.GetProperty("queued_jobs").GetInt32());
                    }
                    return (res.StatusCode, Active: -1, Queued: -1);
                },
                state => state.StatusCode == HttpStatusCode.OK && state.Active == 0 && state.Queued == 0,
                TimeSpan.FromSeconds(timeoutSeconds),
                TimeSpan.FromMilliseconds(300),
                state => $"HTTP={(int)state.StatusCode}; active={state.Active}; queued={state.Queued}");
        }

        private static async Task<JobDefinition> PollJobDefinitionUpdatedAsync(
            IJobHistoryStore store,
            string jobName,
            int timeoutSeconds = 30)
        {
            return (await LoadAwareWait.UntilAsync(
                $"job definition '{jobName}' to persist LastRun and NextRun",
                _ => store.GetJobAsync(jobName),
                job => job?.LastRun is not null && job.NextRun is not null,
                TimeSpan.FromSeconds(timeoutSeconds),
                TimeSpan.FromMilliseconds(200),
                job => job is null
                    ? "job missing"
                    : $"LastRun={job.LastRun:O}; NextRun={job.NextRun:O}"))!;
        }

        private static async Task<JobHistoryEntry> PollRunningEntryAsync(
            IJobHistoryStore store,
            string jobName,
            int timeoutSeconds = 10)
        {
            return (await LoadAwareWait.UntilAsync(
                $"job '{jobName}' to enter RUNNING",
                async _ => (await store.GetHistoryAsync(jobName, 10))
                    .FirstOrDefault(h => h.Status == "RUNNING" && h.EndTime == null),
                entry => entry is not null,
                TimeSpan.FromSeconds(timeoutSeconds),
                TimeSpan.FromMilliseconds(200),
                entry => entry is null ? "no running entry" : $"history id={entry.Id}"))!;
        }

        private static void AssertNoSecretLeak(string? text, params string[] secrets)
        {
            Assert.NotNull(text);
            foreach (var secret in secrets)
            {
                Assert.DoesNotContain(secret, text, StringComparison.OrdinalIgnoreCase);
            }
        }

        private async Task<JsonElement?> FindMailPitMessageAsync(string subject)
        {
            var found = await LoadAwareWait.UntilAsync(
                $"MailPit message with subject '{subject}'",
                async _ =>
                {
                    var messages = await _smtp.GetMessagesAsync();
                    var messageList = messages.GetProperty("messages");
                    for (int i = 0; i < messageList.GetArrayLength(); i++)
                    {
                        var msg = messageList[i];
                        if (msg.GetProperty("Subject").GetString() == subject)
                        {
                            return (Message: (JsonElement?)msg.Clone(), Count: messageList.GetArrayLength());
                        }
                    }
                    return (Message: (JsonElement?)null, Count: messageList.GetArrayLength());
                },
                state => state.Message.HasValue,
                TimeSpan.FromSeconds(30),
                TimeSpan.FromMilliseconds(300),
                state => $"message count={state.Count}; subject found={state.Message.HasValue}");
            return found.Message;
        }

        [Fact]
        public async Task Verify_Core_Success_Path()
        {
            using var factory = new OrchestratorWebFactory();
            using var client = factory.CreateClient();

            var jobName = "SuccessJob";
            await CreateJobAsync(client, jobName, "SELECT 1 AS Answer;", interval: 1, unit: "SECOND");

            var history = await PollHistoryUntilCountAsync(client, jobName, 1);
            var entry = Assert.Single(history);
            Assert.Equal("SUCCESS", entry.Status);
            Assert.Null(entry.ErrorMessage);
            Assert.True(entry.RowsProcessed >= 0);
            Assert.True(entry.PeakMemoryBytes > 0);
            Assert.True(entry.CpuTimeSeconds >= 0);

            var store = factory.Services.GetRequiredService<IJobHistoryStore>();
            var allHistory = (await store.GetHistoryAsync(jobName, 10)).ToList();
            var dbEntry = Assert.Single(allHistory);
            Assert.Equal("SUCCESS", dbEntry.Status);
            Assert.Equal(entry.RowsProcessed, dbEntry.RowsProcessed);
            Assert.Equal(entry.PeakMemoryBytes, dbEntry.PeakMemoryBytes);

            var jobDef = await PollJobDefinitionUpdatedAsync(store, jobName);
            Assert.NotNull(jobDef.LastRun);
            Assert.NotNull(jobDef.NextRun);

            var apiHistory = await GetHistoryJsonAsync(client, jobName);
            var apiEntry = apiHistory.EnumerateArray().Single();
            Assert.Equal("SUCCESS", apiEntry.GetProperty("status").GetString());
            Assert.True(apiEntry.TryGetProperty("rowsProcessed", out _));
            Assert.True(apiEntry.TryGetProperty("peakMemoryBytes", out _));
            Assert.True(apiEntry.TryGetProperty("cpuTimeSeconds", out _));
        }

        [Fact]
        public async Task Verify_Multiple_Jobs_Due_Together_Complete_And_Drain_Queue()
        {
            using var factory = new OrchestratorWebFactory();
            using var client = factory.CreateClient();

            var jobNames = Enumerable.Range(1, 6)
                .Select(i => $"ConcurrentDueJob{i}")
                .ToArray();

            await Task.WhenAll(jobNames.Select(name =>
                CreateJobAsync(client, name, $"SELECT '{name}' AS JobName;", interval: 1, unit: "SECOND")));

            var histories = await Task.WhenAll(jobNames.Select(name =>
                PollHistoryUntilCountAsync(client, name, 1)));

            Assert.All(histories, history =>
            {
                var entry = Assert.Single(history);
                Assert.Equal("SUCCESS", entry.Status);
                Assert.NotNull(entry.EndTime);
            });

            await PollSchedulerIdleAsync(client);

            var store = factory.Services.GetRequiredService<IJobHistoryStore>();
            foreach (var jobName in jobNames)
            {
                var job = await PollJobDefinitionUpdatedAsync(store, jobName);
                Assert.NotNull(job.LastRun);
                Assert.NotNull(job.NextRun);
            }
        }

        [Fact]
        public async Task Verify_Failure_Path_And_Retries()
        {
            using var factory = new OrchestratorWebFactory();
            using var client = factory.CreateClient();

            var jobName = "FailJob";
            await CreateJobAsync(client, jobName, "THROW 'Intentionally failed';", interval: 2, unit: "SECOND", maxRetries: 1, retryDelaySeconds: 1);

            var history = await PollHistoryUntilCountAsync(client, jobName, 2);
            Assert.All(history, entry =>
            {
                Assert.Equal("FAILURE", entry.Status);
                Assert.Contains("Intentionally failed", entry.ErrorMessage);
            });

            var store = factory.Services.GetRequiredService<IJobHistoryStore>();
            var jobDef = await store.GetJobAsync(jobName);
            Assert.NotNull(jobDef);
            Assert.NotNull(jobDef.LastRun);
            Assert.NotNull(jobDef.NextRun);
            Assert.True(jobDef.NextRun > jobDef.LastRun);
        }

        [Fact]
        public async Task Verify_Failure_Path_Sanitizes_Secrets()
        {
            using var factory = new OrchestratorWebFactory();
            using var client = factory.CreateClient();

            var jobName = "SanitizedFailureJob";
            const string secret = "smtp-secret-should-not-leak";
            var script = $@"
CREATE CONNECTION mail_conn AS SMTP(HOST = 'localhost', PORT = 9999, USER = 'scheduler-user', PASSWORD = '{secret}', USE_SSL = FALSE);
SELECT 'recipient@example.com' AS [To], 'sender@example.com' AS [From], 'Scheduler Notification' AS [Subject], 'Job failed' AS [Body]
INTO mail_conn.Email;
";
            await CreateJobAsync(client, jobName, script, interval: 10, unit: "MINUTE");
            await TriggerJobAsync(client, jobName);

            var history = await PollHistoryUntilCountAsync(client, jobName, 1);
            Assert.Equal("FAILURE", history[0].Status);
            AssertNoSecretLeak(history[0].ErrorMessage, secret, "PASSWORD =", "ENC:");
        }

        [Fact]
        public async Task ManualTrigger_VariableOverridesApplyWithoutEditingTheSavedJob()
        {
            using var factory = new OrchestratorWebFactory();
            using var client = factory.CreateClient();
            var jobName = $"BackfillOverride_{Guid.NewGuid():N}";
            const string script = "DECLARE @mode = 'scheduled'; DECLARE @access_token = 'none'; IF (@mode = 'backfill') PRINT 'override applied'; ELSE THROW 'override was not applied';";
            var store = factory.Services.GetRequiredService<IJobHistoryStore>();
            await store.SaveJobAsync(new JobDefinition(
                jobName, script, 1, "HOUR", null, null, DateTime.Now.AddHours(1)));

            var securityEvents = new RecordingSecurityEventSink();
            using var securityEventScope = new SecurityEventSinkScope(securityEvents);

            using var trigger = Authorized(
                HttpMethod.Post,
                $"/api/scheduled-jobs/{Uri.EscapeDataString(jobName)}/trigger",
                new
                {
                    variables = new Dictionary<string, string>
                    {
                        ["@mode"] = "backfill",
                        ["@access_token"] = "SECRET:tenant-backfill-token"
                    }
                });
            var response = await client.SendAsync(trigger);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            var audit = Assert.Single(securityEvents.Events, entry =>
                entry.Type == SecurityEventType.OverrideAttempt && entry.JobId == jobName);
            Assert.Contains("@access_token", audit.Reason, StringComparison.Ordinal);
            Assert.DoesNotContain("tenant-backfill-token", audit.Reason, StringComparison.Ordinal);

            var history = await PollHistoryUntilCountAsync(client, jobName, 1);
            Assert.True(
                string.Equals("SUCCESS", history[0].Status, StringComparison.OrdinalIgnoreCase),
                $"Status={history[0].Status}; Error={history[0].ErrorMessage ?? "<null>"}");

            var saved = await store.GetJobAsync(jobName);
            Assert.NotNull(saved);
            Assert.Equal(script, saved.Script);
        }

        [Fact]
        public async Task ManualTrigger_RejectsInvalidOrOversizedVariableOverrides()
        {
            using var factory = new OrchestratorWebFactory();
            using var client = factory.CreateClient();
            var jobName = $"BackfillValidation_{Guid.NewGuid():N}";
            await factory.Services.GetRequiredService<IJobHistoryStore>().SaveJobAsync(new JobDefinition(
                jobName, "SELECT 1;", 1, "HOUR", null, null, DateTime.Now.AddHours(1)));

            using var invalid = Authorized(
                HttpMethod.Post,
                $"/api/scheduled-jobs/{Uri.EscapeDataString(jobName)}/trigger",
                new { variables = new Dictionary<string, string> { ["@bad-name"] = "SECRET:must-not-echo" } });
            var invalidResponse = await client.SendAsync(invalid);
            Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
            Assert.DoesNotContain("must-not-echo", await invalidResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            var tooMany = Enumerable.Range(0, 33).ToDictionary(i => $"@v{i}", i => i.ToString());
            using var oversized = Authorized(
                HttpMethod.Post,
                $"/api/scheduled-jobs/{Uri.EscapeDataString(jobName)}/trigger",
                new { variables = tooMany });
            Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(oversized)).StatusCode);
        }

        [Fact]
        public async Task ManualTrigger_RejectsOverridesWhileTheSameJobIsAlreadyRunning()
        {
            using var factory = new OrchestratorWebFactory();
            using var client = factory.CreateClient();
            var jobName = $"BackfillConflict_{Guid.NewGuid():N}";
            await factory.Services.GetRequiredService<IJobHistoryStore>().SaveJobAsync(new JobDefinition(
                jobName,
                "DECLARE @mode = 'scheduled'; WAITFOR DELAY '00:00:02';",
                1,
                "HOUR",
                null,
                null,
                DateTime.Now.AddHours(1)));

            using var first = Authorized(
                HttpMethod.Post,
                $"/api/scheduled-jobs/{Uri.EscapeDataString(jobName)}/trigger",
                new { variables = new Dictionary<string, string> { ["@mode"] = "first" } });
            Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(first)).StatusCode);

            using var second = Authorized(
                HttpMethod.Post,
                $"/api/scheduled-jobs/{Uri.EscapeDataString(jobName)}/trigger",
                new { variables = new Dictionary<string, string> { ["@mode"] = "must-not-be-dropped" } });
            var conflict = await client.SendAsync(second);

            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            Assert.DoesNotContain("must-not-be-dropped", await conflict.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        [Fact]
        public async Task RunHistory_ExposesCheckpointLabelButNotOpaqueSessionId()
        {
            using var factory = new OrchestratorWebFactory();
            using var client = factory.CreateClient();
            var store = factory.Services.GetRequiredService<IJobHistoryStore>();
            await store.SaveJobAsync(new JobDefinition(
                "resume_history", "SELECT 1;", 1, "HOUR", null, null, DateTime.Now.AddHours(1)));
            var runId = await store.LogJobStartAsync("resume_history");
            await store.LogJobEndAsync(runId, "FAILURE", "boom");
            await store.UpdateJobResumeMetadataAsync(runId, "private-session-handle", "load_complete");

            using var request = Authorized(HttpMethod.Get, "/api/scheduled-jobs/resume_history/history");
            using var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var payload = await response.Content.ReadAsStringAsync();

            Assert.Contains("load_complete", payload, StringComparison.Ordinal);
            Assert.Contains("hasResumeSession", payload, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("private-session-handle", payload, StringComparison.Ordinal);
            Assert.DoesNotContain("sessionId", payload, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ResumeRun_ExplainsWhenNoNamedCheckpointExists()
        {
            using var factory = new OrchestratorWebFactory();
            using var client = factory.CreateClient();
            var store = factory.Services.GetRequiredService<IJobHistoryStore>();
            await store.SaveJobAsync(new JobDefinition(
                "ordinary_failure", "SELECT 1;", 1, "HOUR", null, null, DateTime.Now.AddHours(1)));
            var runId = await store.LogJobStartAsync("ordinary_failure");
            await store.LogJobEndAsync(runId, "FAILURE", "boom");

            using var request = Authorized(HttpMethod.Post, $"/api/job-runs/{runId}/resume");
            using var response = await client.SendAsync(request);
            var payload = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Contains("not a persistent session", payload, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Verify_Resume_Restart_Behavior()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"orch_restart_{Guid.NewGuid():N}");
            try
            {
                // 1. Start first factory and register a job with a very long interval (1 hour)
                var jobName = "ResumeJob";
                using (var factory1 = new OrchestratorWebFactory(tempDir))
                {
                    using (var client1 = factory1.CreateClient())
                    {
                        var request = new
                        {
                            Name = jobName,
                            ScriptText = "SELECT 100 AS Answer;",
                            Interval = 1,
                            Unit = "HOUR",
                            MaxRetries = 0,
                            RetryDelaySeconds = 30,
                            HashPolicy = "Warn"
                        };

                        using var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/scheduled-jobs")
                        {
                            Content = JsonContent.Create(request)
                        };
                        createReq.Headers.Add("X-Orchestrator-Key", "test-orch-key-12345");
                        var createRes = await client1.SendAsync(createReq);
                        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);

                        // Wait for first execution to record
                        await PollHistoryUntilCountAsync(client1, jobName, 1);
                    }
                } // factory1 disposed, background scheduler stops

                // 2. Access store directly in the database file and set NextRun in the past
                var store = new SQLiteJobHistoryStore(Path.Combine(tempDir, "etlsql.db"));
                await store.InitializeAsync();
                var job = await store.GetJobAsync(jobName);
                Assert.NotNull(job);

                // Artificially move NextRun back
                var dueJob = job with { NextRun = DateTime.Now.AddMinutes(-10) };
                await store.SaveJobAsync(dueJob);

                // Verify DB reflects it
                var updatedJob = await store.GetJobAsync(jobName);
                Assert.NotNull(updatedJob);
                Assert.True(updatedJob.NextRun <= DateTime.Now);

                // 3. Start a second factory using the same DB
                using (var factory2 = new OrchestratorWebFactory(tempDir))
                {
                    using (var client2 = factory2.CreateClient())
                    {
                        // Because the job was due, starting the host must execute it immediately
                        var history = await PollHistoryUntilCountAsync(client2, jobName, 2);
                        Assert.Equal(2, history.Count);
                    }
                }
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, recursive: true); } catch { }
                }
            }
        }

        [Fact]
        public async Task Verify_Cancellation()
        {
            using var factory = new OrchestratorWebFactory();
            using var client = factory.CreateClient();

            var jobName = "CancelJob";
            await CreateJobAsync(client, jobName, "WAITFOR DELAY '00:00:10';");

            var store = factory.Services.GetRequiredService<IJobHistoryStore>();
            var runningEntry = await PollRunningEntryAsync(store, jobName);

            using var killReq = Authorized(HttpMethod.Post, $"/api/scheduled-jobs/{Uri.EscapeDataString(jobName)}/kill");
            var killRes = await client.SendAsync(killReq);
            Assert.Equal(HttpStatusCode.OK, killRes.StatusCode);

            var finalHistory = await PollHistoryUntilCountAsync(client, jobName, 1);
            var finishedEntry = finalHistory.First(h => h.Id == runningEntry.Id);
            Assert.Equal("FAILURE", finishedEntry.Status);
            Assert.Contains("cancel", finishedEntry.ErrorMessage?.ToLowerInvariant());

            await PollSchedulerIdleAsync(client);
            var postKillHistory = (await store.GetHistoryAsync(jobName, 10)).ToList();
            Assert.DoesNotContain(postKillHistory, h => h.Status == "RUNNING" && h.EndTime == null);
        }

        [Fact]
        public async Task Verify_Trigger_And_Disable_While_Running_Drain_Without_Stuck_Work()
        {
            using var factory = new OrchestratorWebFactory();
            using var client = factory.CreateClient();

            var jobName = "ConcurrentControlJob";
            await CreateJobAsync(client, jobName, "WAITFOR DELAY '00:00:05';");
            await TriggerJobAsync(client, jobName);

            var store = factory.Services.GetRequiredService<IJobHistoryStore>();
            var runningEntry = await PollRunningEntryAsync(store, jobName);

            using var triggerReq = Authorized(HttpMethod.Post, $"/api/scheduled-jobs/{Uri.EscapeDataString(jobName)}/trigger");
            using var disableReq = Authorized(HttpMethod.Put, $"/api/scheduled-jobs/{Uri.EscapeDataString(jobName)}", new { IsEnabled = false });
            disableReq.Headers.TryAddWithoutValidation(
                "If-Match", $"\"{(await store.GetJobAsync(jobName))!.Version}\"");
            var controlResponses = await Task.WhenAll(client.SendAsync(triggerReq), client.SendAsync(disableReq));
            Assert.Contains(controlResponses, r => r.StatusCode == HttpStatusCode.Conflict);
            Assert.Contains(controlResponses, r => r.StatusCode == HttpStatusCode.OK);

            using var killReq = Authorized(HttpMethod.Post, $"/api/scheduled-jobs/{Uri.EscapeDataString(jobName)}/kill");
            var killRes = await client.SendAsync(killReq);
            Assert.Equal(HttpStatusCode.OK, killRes.StatusCode);

            await PollSchedulerIdleAsync(client, timeoutSeconds: 20);

            var job = await store.GetJobAsync(jobName);
            Assert.NotNull(job);
            Assert.False(job.IsEnabled);
            var finalHistory = (await store.GetHistoryAsync(jobName, 10)).ToList();
            Assert.DoesNotContain(finalHistory, h => h.Status == "RUNNING" && h.EndTime == null);
        }

        [Fact]
        public async Task Verify_Delete_While_Running_Removes_Schedule_And_Drains_Active_Work()
        {
            using var factory = new OrchestratorWebFactory();
            using var client = factory.CreateClient();

            var jobName = "DeleteRunningJob";
            await CreateJobAsync(client, jobName, "WAITFOR DELAY '00:00:02';");
            await TriggerJobAsync(client, jobName);

            var store = factory.Services.GetRequiredService<IJobHistoryStore>();
            _ = await PollRunningEntryAsync(store, jobName);

            using var deleteReq = Authorized(HttpMethod.Delete, $"/api/scheduled-jobs/{Uri.EscapeDataString(jobName)}");
            deleteReq.Headers.TryAddWithoutValidation(
                "If-Match", $"\"{(await store.GetJobAsync(jobName))!.Version}\"");
            var deleteRes = await client.SendAsync(deleteReq);
            Assert.Equal(HttpStatusCode.OK, deleteRes.StatusCode);

            Assert.Null(await store.GetJobAsync(jobName));
            Assert.Empty(await store.GetHistoryAsync(jobName, 10));

            await PollSchedulerIdleAsync(client, timeoutSeconds: 15);

            Assert.Null(await store.GetJobAsync(jobName));
            Assert.Empty(await store.GetHistoryAsync(jobName, 10));
        }

        [Fact]
        public async Task Verify_Email_Notification_Behavior()
        {
            using var factory = new OrchestratorWebFactory();
            using var client = factory.CreateClient();

            int initialEmailCount = await _smtp.GetMessageCountAsync();

            var jobName = "EmailNotificationJob";
            var script = $@"
CREATE CONNECTION mail_conn AS SMTP(HOST = 'localhost', PORT = {_smtp.SmtpPort}, USE_SSL = FALSE);
SELECT 'recipient@example.com' AS [To], 'sender@example.com' AS [From], 'Scheduler Notification' AS [Subject], 'Job succeeded' AS [Body]
INTO mail_conn.Email;
";
            await CreateJobAsync(client, jobName, script);
            await TriggerJobAsync(client, jobName);

            var history = await PollHistoryUntilCountAsync(client, jobName, 1);
            Assert.True(history[0].Status == "SUCCESS", $"Job failed with error: {history[0].ErrorMessage}");

            int finalEmailCount = await _smtp.GetMessageCountAsync();
            Assert.True(finalEmailCount > initialEmailCount, "No email was sent to MailPit");

            var message = await FindMailPitMessageAsync("Scheduler Notification");
            Assert.NotNull(message);
            AssertNoSecretLeak(message.Value.GetRawText(), "ENC:", "PASSWORD=");
        }

        [Fact]
        public async Task Verify_Email_Failure_Notification_Behavior()
        {
            using var factory = new OrchestratorWebFactory();
            using var client = factory.CreateClient();

            const string secret = "failure-email-secret";
            var jobName = "EmailFailureNotificationJob";
            var script = $@"
BEGIN TRY
    THROW 'Simulated failure before notification';
END TRY
BEGIN CATCH
    CREATE CONNECTION mail_conn AS SMTP(HOST = 'localhost', PORT = {_smtp.SmtpPort}, USE_SSL = FALSE, PASSWORD = '{secret}');
    SELECT 'recipient@example.com' AS [To], 'sender@example.com' AS [From], 'Scheduler Failure Notification' AS [Subject], 'Job failed without secrets' AS [Body]
    INTO mail_conn.Email;
    THROW;
END CATCH
";
            await CreateJobAsync(client, jobName, script);
            await TriggerJobAsync(client, jobName);

            var history = await PollHistoryUntilCountAsync(client, jobName, 1);
            Assert.Equal("FAILURE", history[0].Status);
            AssertNoSecretLeak(history[0].ErrorMessage, secret, "PASSWORD =", "ENC:");

            var message = await FindMailPitMessageAsync("Scheduler Failure Notification");
            Assert.NotNull(message);
            AssertNoSecretLeak(message.Value.GetRawText(), secret, "PASSWORD=", "ENC:");
        }

        [Fact]
        public async Task Verify_Dependency_Outage_And_Fault_Tolerance()
        {
            using var factory = new OrchestratorWebFactory();
            using var client = factory.CreateClient();

            var jobName1 = "OutageJob";
            var badScript = @"
CREATE CONNECTION mail_conn AS SMTP(HOST = 'localhost', PORT = 9999, USE_SSL = FALSE, TIMEOUT_SECONDS = 2);
SELECT 'recipient@example.com' AS [To], 'sender@example.com' AS [From], 'Scheduler Notification' AS [Subject], 'Job succeeded' AS [Body]
INTO mail_conn.Email;
";
            await CreateJobAsync(client, jobName1, badScript, interval: 1, unit: "SECOND");

            var apiOutageJob = "ApiOutageJob";
            var apiOutageScript = @"
CREATE CONNECTION api_conn AS REST(URL = 'http://localhost:9/unavailable', TIMEOUT_SECONDS = 1);
SELECT * FROM api_conn.ENDPOINT;
";
            await CreateJobAsync(client, apiOutageJob, apiOutageScript, interval: 1, unit: "SECOND");

            var blockedPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "C:\\Windows\\System32\\config\\SAM"
                : "/etc/shadow";
            var blockedFileJob = "BlockedFileJob";
            var blockedFileScript = $"SELECT FILE_EXISTS('{blockedPath.Replace("'", "''")}') AS ExistsFlag;";
            await CreateJobAsync(client, blockedFileJob, blockedFileScript, interval: 1, unit: "SECOND");

            var jobName2 = "LaterJob";
            await CreateJobAsync(client, jobName2, "SELECT 1;", interval: 1, unit: "SECOND");

            var history1 = await PollHistoryUntilCountAsync(client, jobName1, 1);
            Assert.Equal("FAILURE", history1[0].Status);

            var apiHistory = await PollHistoryUntilCountAsync(client, apiOutageJob, 1);
            Assert.Equal("FAILURE", apiHistory[0].Status);

            var fileHistory = await PollHistoryUntilCountAsync(client, blockedFileJob, 1);
            Assert.Equal("FAILURE", fileHistory[0].Status);

            var history2 = await PollHistoryUntilCountAsync(client, jobName2, 1);
            Assert.Equal("SUCCESS", history2[0].Status);
        }

        private sealed class SecurityEventSinkScope : IDisposable
        {
            private readonly ISecurityEventSink _previous;

            public SecurityEventSinkScope(ISecurityEventSink sink)
            {
                _previous = SecurityEventRuntime.Sink;
                SecurityEventRuntime.Sink = sink;
            }

            public void Dispose() => SecurityEventRuntime.Sink = _previous;
        }

        private sealed class RecordingSecurityEventSink : ISecurityEventSink
        {
            public List<SecurityEvent> Events { get; } = [];
            public void Emit(SecurityEvent securityEvent) => Events.Add(securityEvent);
        }
    }

    [CollectionDefinition("SMTP collection")]
    public class SmtpCollection : ICollectionFixture<SmtpFixture> { }
}
