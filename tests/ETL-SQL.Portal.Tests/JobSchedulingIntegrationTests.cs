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
using ETL_SQL.Orchestrator.Scheduling;
using ETL_SQL.Orchestrator.Service;
using ETL_SQL.Orchestrator.Storage;
using ETL_SQL.Tests.Integration.Connectors;
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
            var start = DateTime.UtcNow;
            while ((DateTime.UtcNow - start).TotalSeconds < timeoutSeconds)
            {
                using var req = Authorized(HttpMethod.Get, $"/api/scheduled-jobs/{Uri.EscapeDataString(jobName)}/history");
                var res = await client.SendAsync(req);
                if (res.StatusCode == HttpStatusCode.OK)
                {
                    var history = await res.Content.ReadFromJsonAsync<List<JobHistoryEntry>>(_jsonOptions);
                    if (history != null && history.Count >= expectedCount && history.Take(expectedCount).All(h => h.EndTime != null))
                    {
                        return history;
                    }
                }
                await Task.Delay(500);
            }
            throw new TimeoutException($"Timed out waiting for {expectedCount} completed history entries for job '{jobName}'.");
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
            var start = DateTime.UtcNow;
            while ((DateTime.UtcNow - start).TotalSeconds < timeoutSeconds)
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, "/metrics");
                var res = await client.SendAsync(req);
                if (res.StatusCode == HttpStatusCode.OK)
                {
                    using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
                    var root = doc.RootElement;
                    if (root.GetProperty("active_jobs").GetInt32() == 0 &&
                        root.GetProperty("queued_jobs").GetInt32() == 0)
                    {
                        return;
                    }
                }
                await Task.Delay(300);
            }

            throw new TimeoutException("Timed out waiting for scheduler metrics to report no active or queued jobs.");
        }

        private static async Task<JobDefinition> PollJobDefinitionUpdatedAsync(
            IJobHistoryStore store,
            string jobName,
            int timeoutSeconds = 30)
        {
            var start = DateTime.UtcNow;
            while ((DateTime.UtcNow - start).TotalSeconds < timeoutSeconds)
            {
                var job = await store.GetJobAsync(jobName);
                if (job?.LastRun is not null && job.NextRun is not null)
                {
                    return job;
                }
                await Task.Delay(200);
            }

            throw new TimeoutException($"Timed out waiting for job definition '{jobName}' to persist LastRun and NextRun.");
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
            var start = DateTime.UtcNow;
            while ((DateTime.UtcNow - start).TotalSeconds < 30)
            {
                var messages = await _smtp.GetMessagesAsync();
                var messageList = messages.GetProperty("messages");
                for (int i = 0; i < messageList.GetArrayLength(); i++)
                {
                    var msg = messageList[i];
                    if (msg.GetProperty("Subject").GetString() == subject)
                    {
                        return msg.Clone();
                    }
                }
                await Task.Delay(300);
            }

            return null;
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
            JobHistoryEntry? runningEntry = null;
            var start = DateTime.UtcNow;
            while ((DateTime.UtcNow - start).TotalSeconds < 10)
            {
                var history = (await store.GetHistoryAsync(jobName, 10)).ToList();
                runningEntry = history.FirstOrDefault(h => h.Status == "RUNNING");
                if (runningEntry != null) break;
                await Task.Delay(300);
            }
            Assert.NotNull(runningEntry);

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
            JobHistoryEntry? runningEntry = null;
            var start = DateTime.UtcNow;
            while ((DateTime.UtcNow - start).TotalSeconds < 10)
            {
                var history = (await store.GetHistoryAsync(jobName, 10)).ToList();
                runningEntry = history.FirstOrDefault(h => h.Status == "RUNNING" && h.EndTime == null);
                if (runningEntry != null) break;
                await Task.Delay(200);
            }
            Assert.NotNull(runningEntry);

            using var triggerReq = Authorized(HttpMethod.Post, $"/api/scheduled-jobs/{Uri.EscapeDataString(jobName)}/trigger");
            using var disableReq = Authorized(HttpMethod.Put, $"/api/scheduled-jobs/{Uri.EscapeDataString(jobName)}", new { IsEnabled = false });
            disableReq.Headers.TryAddWithoutValidation(
                "If-Match", $"\"{(await store.GetJobAsync(jobName))!.Version}\"");
            var controlResponses = await Task.WhenAll(client.SendAsync(triggerReq), client.SendAsync(disableReq));
            Assert.Contains(controlResponses, r => r.StatusCode == HttpStatusCode.Accepted);
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
            var start = DateTime.UtcNow;
            while ((DateTime.UtcNow - start).TotalSeconds < 10)
            {
                var history = (await store.GetHistoryAsync(jobName, 10)).ToList();
                if (history.Any(h => h.Status == "RUNNING" && h.EndTime == null)) break;
                await Task.Delay(200);
            }
            Assert.Contains(await store.GetHistoryAsync(jobName, 10), h => h.Status == "RUNNING" && h.EndTime == null);

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
CREATE CONNECTION mail_conn AS SMTP(HOST = 'localhost', PORT = 9999, USE_SSL = FALSE);
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
    }

    [CollectionDefinition("SMTP collection")]
    public class SmtpCollection : ICollectionFixture<SmtpFixture> { }
}
