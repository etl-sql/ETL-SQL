using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ETL_SQL.Core.Data;
using ETL_SQL.Orchestrator.Scheduling;
using ETL_SQL.Orchestrator.Service;
using ETL_SQL.Orchestrator.Storage;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using ETL_SQL.Tests.Integration.Connectors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ETL_SQL.Portal.Tests
{
    [Collection("SMTP collection")]
    [Trait("Category", "Integration")]
    [Trait("CompatBreak", "0.11")]
    public class SubscriptionIntegrationTests
    {
        private readonly SmtpFixture _smtp;
        private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

        public SubscriptionIntegrationTests(SmtpFixture smtp)
        {
            _smtp = smtp;
        }

        private async Task<string> GetAdminTokenAsync(HttpClient client)
        {
            var loginRes = await client.PostAsJsonAsync("/api/auth/login", new
            {
                username = "admin",
                password = "Admin@12345!"
            });
            loginRes.EnsureSuccessStatusCode();
            var body = await loginRes.Content.ReadFromJsonAsync<JsonObject>(_json);
            var token = body!["token"]!.GetValue<string>();

            using var cpReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password");
            cpReq.Headers.Authorization = new("Bearer", token);
            cpReq.Content = JsonContent.Create(new
            {
                currentPassword = "Admin@12345!",
                newPassword = "Admin@Tests99!"
            });
            var cpRes = await client.SendAsync(cpReq);
            cpRes.EnsureSuccessStatusCode();

            var reloginRes = await client.PostAsJsonAsync("/api/auth/login", new
            {
                username = "admin",
                password = "Admin@Tests99!"
            });
            reloginRes.EnsureSuccessStatusCode();
            var reloginBody = await reloginRes.Content.ReadFromJsonAsync<JsonObject>(_json);
            return reloginBody!["token"]!.GetValue<string>();
        }

        private Task<HttpResponseMessage> AuthGet(HttpClient client, string token, string url)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new("Bearer", token);
            return client.SendAsync(req);
        }

        private async Task<HttpResponseMessage> AuthPost(HttpClient client, string token, string url, object body)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new("Bearer", token);
            req.Content = JsonContent.Create(body);
            await IfMatchVersioning.StampAsync(client, req, token);
            return await client.SendAsync(req);
        }

        private async Task<HttpResponseMessage> AuthPut(HttpClient client, string token, string url, object body)
        {
            var req = new HttpRequestMessage(HttpMethod.Put, url);
            req.Headers.Authorization = new("Bearer", token);
            req.Content = JsonContent.Create(body);
            await IfMatchVersioning.StampAsync(client, req, token);
            return await client.SendAsync(req);
        }

        private async Task<HttpResponseMessage> AuthDelete(HttpClient client, string token, string url)
        {
            var req = new HttpRequestMessage(HttpMethod.Delete, url);
            req.Headers.Authorization = new("Bearer", token);
            await IfMatchVersioning.StampAsync(client, req, token);
            return await client.SendAsync(req);
        }

        private async Task<List<JobHistoryEntry>> PollHistoryUntilCountAsync(SQLiteJobHistoryStore store, string jobName, int expectedCount, int timeoutSeconds = 15)
        {
            var start = DateTime.UtcNow;
            while ((DateTime.UtcNow - start).TotalSeconds < timeoutSeconds)
            {
                var history = (await store.GetHistoryAsync(jobName, 10)).ToList();
                if (history.Count >= expectedCount && history.Take(expectedCount).All(h => h.EndTime != null))
                {
                    return history;
                }
                await Task.Delay(500);
            }
            throw new TimeoutException($"Timed out waiting for {expectedCount} completed history entries for job '{jobName}'.");
        }

        private async Task PollHistoryUntilStatusAsync(SQLiteJobHistoryStore store, string jobName, string status, int timeoutSeconds = 15)
        {
            var start = DateTime.UtcNow;
            while ((DateTime.UtcNow - start).TotalSeconds < timeoutSeconds)
            {
                var history = (await store.GetHistoryAsync(jobName, 10)).ToList();
                if (history.Any(h => string.Equals(h.Status, status, StringComparison.OrdinalIgnoreCase)))
                {
                    return;
                }
                await Task.Delay(200);
            }
            throw new TimeoutException($"Timed out waiting for job '{jobName}' to reach status '{status}'.");
        }

        private async Task PollMailPitUntilCountAsync(int expectedCount, int timeoutSeconds = 15)
        {
            var start = DateTime.UtcNow;
            while ((DateTime.UtcNow - start).TotalSeconds < timeoutSeconds)
            {
                var count = await _smtp.GetMessageCountAsync();
                if (count >= expectedCount)
                {
                    return;
                }
                await Task.Delay(500);
            }
            throw new TimeoutException($"Timed out waiting for {expectedCount} email messages in MailPit.");
        }

        private static async Task TriggerJobAsync(HttpClient orchClient, string jobName)
        {
            using var triggerReq = new HttpRequestMessage(HttpMethod.Post, $"/api/scheduled-jobs/{Uri.EscapeDataString(jobName)}/trigger");
            triggerReq.Headers.Add("X-Orchestrator-Key", "test-orch-key-12345");
            var triggerRes = await orchClient.SendAsync(triggerReq);
            Assert.Equal(HttpStatusCode.Accepted, triggerRes.StatusCode);
        }

        private static async Task PollPortalOrchestratorAsync(PortalWebFactory factory)
        {
            var poller = ActivatorUtilities.CreateInstance<OrchestratorPollerService>(factory.Services);
            await poller.PollAsync(CancellationToken.None);
        }

        private static async Task DelayGeneratedTriggerAsync(string scriptPath, int seconds = 3)
        {
            var script = await File.ReadAllTextAsync(scriptPath);
            await File.WriteAllTextAsync(
                scriptPath,
                $"WAITFOR DELAY '00:00:{seconds:00}';{Environment.NewLine}{script}");
        }

        private static string GeneratedSubscriptionScriptPath(string tempDir, int subId, string reportName)
        {
            var sanitizedReportName = new string(reportName.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
            return Path.Combine(tempDir, "scripts", "subscriptions", $"sub_{subId}_{sanitizedReportName}.etlsql");
        }

        private static void AssertNoSecretLeak(string? text, params string[] secrets)
        {
            if (text is null) return;
            foreach (var secret in secrets)
            {
                Assert.DoesNotContain(secret, text, StringComparison.OrdinalIgnoreCase);
            }
        }

        private async Task<(int folderId, int reportId, string smtpAlias, string reportName)> SetupReportAndSmtpAsync(HttpClient client, string token, string tempDir, string reportScript)
        {
            var folderRes = await AuthPost(client, token, "/api/folders", new { name = $"Sub Folder {Guid.NewGuid():N}"[..20], parentId = (int?)null });
            Assert.Equal(HttpStatusCode.Created, folderRes.StatusCode);
            var folder = await folderRes.Content.ReadFromJsonAsync<JsonObject>(_json);
            var folderId = folder!["id"]!.GetValue<int>();

            var scriptPath = Path.Combine(tempDir, "scripts", $"report_{Guid.NewGuid():N}.rptsql");
            await File.WriteAllTextAsync(scriptPath, reportScript);

            var reportName = $"Report {Guid.NewGuid():N}"[..20];
            var reportRes = await AuthPost(client, token, "/api/reports", new
            {
                folderId,
                name = reportName,
                description = "Integration test report",
                scriptPath
            });
            Assert.Equal(HttpStatusCode.Created, reportRes.StatusCode);
            var report = await reportRes.Content.ReadFromJsonAsync<JsonObject>(_json);
            var reportId = report!["id"]!.GetValue<int>();

            var smtpAlias = await CreateSmtpAliasAsync(client, token);

            return (folderId, reportId, smtpAlias, reportName);
        }

        /// <summary>
        /// Registers an SMTP connection in the governed catalog. Two steps rather than one, because
        /// the catalog stores <c>SECRET:</c> references and rejects literal credentials: the value
        /// goes to the Portal secret store first, and the connection references it by name.
        /// </summary>
        private async Task<string> CreateSmtpAliasAsync(HttpClient client, string token, string? alias = null, int? port = null, string? fromAddress = null, string? password = null)
        {
            var smtpAlias = alias ?? $"smtp-{Guid.NewGuid():N}"[..16];

            var options = new Dictionary<string, string>
            {
                ["HOST"] = _smtp.SmtpHost,
                ["PORT"] = (port ?? _smtp.SmtpPort).ToString(),
                ["DEFAULT_FROM"] = fromAddress ?? "portal@example.com",
                ["USE_SSL"] = "false"
            };

            if (password is not null)
            {
                var secretName = $"{smtpAlias}_password".Replace('-', '_');
                var secretRes = await AuthPut(client, token, $"/api/admin/secrets/{secretName}",
                    new { value = password });
                Assert.True(secretRes.IsSuccessStatusCode,
                    $"seeding secret '{secretName}' failed: {secretRes.StatusCode}");

                options["USERNAME"] = "smtp-user";
                options["PASSWORD"] = $"SECRET:{secretName}";
            }

            var smtpRes = await AuthPut(client, token, $"/api/admin/connections/{smtpAlias}", new
            {
                connectorType = "SMTP",
                options,
                sensitiveFields = new[] { "PASSWORD" }
            });
            Assert.True(smtpRes.IsSuccessStatusCode,
                $"registering connection '{smtpAlias}' failed: {smtpRes.StatusCode}");
            return smtpAlias;
        }

        [Theory]
        [InlineData("CSV", "csv", "text/csv")]
        [InlineData("Markdown", "md", "text/markdown")]
        [InlineData("PDF", "pdf", "application/pdf")]
        [InlineData("Link", null, null)]
        public async Task Verify_Subscription_E2E_Delivery(string format, string? expectedExtension, string? expectedMimeType)
        {
            using var portalFactory = new PortalWebFactory();
            using var orchestratorFactory = new OrchestratorWebFactory(portalFactory.TempDir);

            using var portalClient = portalFactory.CreateClient();
            using var orchClient = orchestratorFactory.CreateClient();

            var token = await GetAdminTokenAsync(portalClient);
            var initialMailCount = await _smtp.GetMessageCountAsync();

            var reportScript = @"
SET REPORT TITLE = 'Simple Test Report';
DECLARE @Region STRING INPUT = 'All';
SELECT @Region AS Region, 100 AS Sales INTO #data;
CREATE VISUAL SalesTable AS TABLE (SOURCE = #data, MAPPINGS (Region = Region, Sales = Sales));
CREATE PAGE Page1 AS DASHBOARD(STRUCTURE = 'A', MAP ('A' = SalesTable));
";
            var (_, reportId, smtpAlias, reportName) = await SetupReportAndSmtpAsync(portalClient, token, portalFactory.TempDir, reportScript);

            // 1. Create subscription
            var recipientEmail = $"recipient_{Guid.NewGuid():N}@example.com";
            var subRes = await AuthPost(portalClient, token, "/api/subscriptions", new
            {
                reportId,
                schedule = "Daily",
                format,
                smtpAlias,
                recipientEmail,
                atTime = "08:00"
            });
            Assert.Equal(HttpStatusCode.Created, subRes.StatusCode);
            var sub = await subRes.Content.ReadFromJsonAsync<JsonObject>(_json);
            var subId = sub!["id"]!.GetValue<int>();

            var jobName = $"SUB:{subId}:{reportName}";
            await TriggerJobAsync(orchClient, jobName);

            // 3. Wait for success history
            var store = orchestratorFactory.Services.GetRequiredService<IJobHistoryStore>() as SQLiteJobHistoryStore;
            Assert.NotNull(store);
            var history = await PollHistoryUntilCountAsync(store, jobName, 1);
            Assert.True(history[0].Status == "SUCCESS", $"Job failed with error: {history[0].ErrorMessage}");
            await PollPortalOrchestratorAsync(portalFactory);
            await PollPortalOrchestratorAsync(portalFactory);

            // 4. Assert MailPit received the email
            await PollMailPitUntilCountAsync(initialMailCount + 1);
            var msgsRoot = await _smtp.GetMessagesAsync();
            var msgsArray = msgsRoot.GetProperty("messages");
            Assert.True(msgsArray.ValueKind == JsonValueKind.Array && msgsArray.GetArrayLength() > 0);

            // Find the message sent to our recipient
            JsonElement? msgObj = null;
            for (int i = 0; i < msgsArray.GetArrayLength(); i++)
            {
                var msg = msgsArray[i];
                var toArray = msg.GetProperty("To");
                if (toArray.ValueKind == JsonValueKind.Array && toArray.GetArrayLength() > 0)
                {
                    var addr = toArray[0].GetProperty("Address").GetString();
                    if (string.Equals(addr, recipientEmail, StringComparison.OrdinalIgnoreCase))
                    {
                        msgObj = msg;
                        break;
                    }
                }
            }
            Assert.NotNull(msgObj);
            var msgId = msgObj.Value.GetProperty("ID").GetString()!;

            // Fetch detailed message to get snippet and attachments
            using var http = new HttpClient();
            var detailedRes = await http.GetStringAsync($"http://localhost:{_smtp.ApiPort}/api/v1/message/{msgId}");
            var detailed = JsonDocument.Parse(detailedRes).RootElement;

            if (expectedExtension != null)
            {
                Assert.True(detailed.TryGetProperty("Attachments", out var attachments) && attachments.ValueKind == JsonValueKind.Array);
                var attArray = attachments.EnumerateArray().ToList();
                var attachment = Assert.Single(attArray);

                var filename = attachment.GetProperty("FileName").GetString()!;
                Assert.Contains(expectedExtension, filename);
                Assert.Equal(expectedMimeType, attachment.GetProperty("ContentType").GetString());

                var partId = attachment.GetProperty("PartID").GetString()!;
                var fileBytes = await http.GetByteArrayAsync($"http://localhost:{_smtp.ApiPort}/api/v1/message/{msgId}/part/{partId}");
                Assert.NotEmpty(fileBytes);

                if (format == "CSV")
                {
                    var text = System.Text.Encoding.UTF8.GetString(fileBytes);
                    Assert.Contains("All", text);
                    Assert.Contains("100", text);
                }
                else if (format == "Markdown")
                {
                    var text = System.Text.Encoding.UTF8.GetString(fileBytes);
                    Assert.Contains("Simple Test Report", text);
                }
            }
            else
            {
                if (detailed.TryGetProperty("Attachments", out var attachments))
                {
                    Assert.True(attachments.ValueKind != JsonValueKind.Array || attachments.GetArrayLength() == 0);
                }
                var snippet = msgObj.Value.GetProperty("Snippet").GetString()!;
                Assert.Contains("View it here", snippet);
            }
        }

        [Fact]
        public async Task Verify_Subscription_With_Parameters_Runs_Correctly()
        {
            using var portalFactory = new PortalWebFactory();
            using var orchestratorFactory = new OrchestratorWebFactory(portalFactory.TempDir);

            using var portalClient = portalFactory.CreateClient();
            using var orchClient = orchestratorFactory.CreateClient();

            var token = await GetAdminTokenAsync(portalClient);
            var initialMailCount = await _smtp.GetMessageCountAsync();

            var reportScript = @"
SET REPORT TITLE = 'Parameterized Test Report';
DECLARE @Region STRING INPUT = 'All';
SELECT @Region AS Region, 250 AS Sales INTO #data;
CREATE VISUAL SalesTable AS TABLE (SOURCE = #data, MAPPINGS (Region = Region, Sales = Sales));
CREATE PAGE Page1 AS DASHBOARD(STRUCTURE = 'A', MAP ('A' = SalesTable));
";
            var (_, reportId, smtpAlias, reportName) = await SetupReportAndSmtpAsync(portalClient, token, portalFactory.TempDir, reportScript);

            // 1. Create subscription with NA parameter
            var recipientEmail = $"param_{Guid.NewGuid():N}@example.com";
            var subRes = await AuthPost(portalClient, token, "/api/subscriptions", new
            {
                reportId,
                schedule = "Daily",
                format = "CSV",
                smtpAlias,
                recipientEmail,
                atTime = "08:00",
                parameters = new Dictionary<string, string> { ["Region"] = "NA" }
            });
            Assert.Equal(HttpStatusCode.Created, subRes.StatusCode);
            var sub = await subRes.Content.ReadFromJsonAsync<JsonObject>(_json);
            var subId = sub!["id"]!.GetValue<int>();

            // The persisted scheduler script is a non-secret trigger only.
            var sanitizedReportName = new string(reportName.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
            var generatedScriptPath = Path.Combine(portalFactory.TempDir, "scripts", "subscriptions", $"sub_{subId}_{sanitizedReportName}.etlsql");
            Assert.True(File.Exists(generatedScriptPath));
            var persistedScript = await File.ReadAllTextAsync(generatedScriptPath);
            Assert.Contains($"subscription {subId}", persistedScript, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("@Region", persistedScript, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(recipientEmail, persistedScript, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("PASSWORD", persistedScript, StringComparison.OrdinalIgnoreCase);

            var jobName = $"SUB:{subId}:{reportName}";
            await TriggerJobAsync(orchClient, jobName);

            // 3. Wait for success
            var store = orchestratorFactory.Services.GetRequiredService<IJobHistoryStore>() as SQLiteJobHistoryStore;
            Assert.NotNull(store);
            var history = await PollHistoryUntilCountAsync(store, jobName, 1);
            Assert.True(history[0].Status == "SUCCESS", $"Job failed with error: {history[0].ErrorMessage}");
            await PollPortalOrchestratorAsync(portalFactory);

            // 4. Assert MailPit received the email and attachment reflects the parameter NA
            await PollMailPitUntilCountAsync(initialMailCount + 1);
            var msgsRoot = await _smtp.GetMessagesAsync();
            var msgsArray = msgsRoot.GetProperty("messages");

            JsonElement? msgObj = null;
            for (int i = 0; i < msgsArray.GetArrayLength(); i++)
            {
                var msg = msgsArray[i];
                var toArray = msg.GetProperty("To");
                if (toArray.ValueKind == JsonValueKind.Array && toArray.GetArrayLength() > 0)
                {
                    var addr = toArray[0].GetProperty("Address").GetString();
                    if (string.Equals(addr, recipientEmail, StringComparison.OrdinalIgnoreCase))
                    {
                        msgObj = msg;
                        break;
                    }
                }
            }
            Assert.NotNull(msgObj);
            var msgId = msgObj.Value.GetProperty("ID").GetString()!;

            using var http = new HttpClient();
            var detailedRes = await http.GetStringAsync($"http://localhost:{_smtp.ApiPort}/api/v1/message/{msgId}");
            var detailed = JsonDocument.Parse(detailedRes).RootElement;
            var attachments = detailed.GetProperty("Attachments");
            var attachment = attachments[0];
            var partId = attachment.GetProperty("PartID").GetString()!;
            var fileBytes = await http.GetByteArrayAsync($"http://localhost:{_smtp.ApiPort}/api/v1/message/{msgId}/part/{partId}");
            var csvText = System.Text.Encoding.UTF8.GetString(fileBytes);

            Assert.Contains("NA", csvText);
            Assert.Contains("250", csvText);
            Assert.DoesNotContain("All", csvText);
        }

        [Fact]
        public async Task Verify_Subscription_Update_Syncs_Orchestrator_And_Script()
        {
            using var portalFactory = new PortalWebFactory();
            using var orchestratorFactory = new OrchestratorWebFactory(portalFactory.TempDir);

            using var portalClient = portalFactory.CreateClient();
            using var orchClient = orchestratorFactory.CreateClient();

            var token = await GetAdminTokenAsync(portalClient);

            var reportScript = @"
SET REPORT TITLE = 'Update Test Report';
SELECT 1 AS Val INTO #data;
CREATE VISUAL SalesTable AS TABLE (SOURCE = #data, MAPPINGS (Val = Val));
CREATE PAGE Page1 AS DASHBOARD(STRUCTURE = 'A', MAP ('A' = SalesTable));
";
            var (_, reportId, smtpAlias, reportName) = await SetupReportAndSmtpAsync(portalClient, token, portalFactory.TempDir, reportScript);
            var secondSmtpAlias = await CreateSmtpAliasAsync(portalClient, token, fromAddress: "portal-updated@example.com");

            // 1. Create subscription
            var subRes = await AuthPost(portalClient, token, "/api/subscriptions", new
            {
                reportId,
                schedule = "Daily",
                format = "CSV",
                smtpAlias,
                recipientEmail = "update@test.local",
                atTime = "08:00",
                parameters = new Dictionary<string, string> { ["Region"] = "EMEA" }
            });
            Assert.Equal(HttpStatusCode.Created, subRes.StatusCode);
            var sub = await subRes.Content.ReadFromJsonAsync<JsonObject>(_json);
            var subId = sub!["id"]!.GetValue<int>();
            var jobName = $"SUB:{subId}:{reportName}";

            // 2. Perform Update via PUT
            var updateRes = await AuthPut(portalClient, token, $"/api/subscriptions/{subId}", new
            {
                schedule = "Weekly",
                format = "Markdown",
                smtpAlias = secondSmtpAlias,
                recipients = "new-recipient@test.local",
                isActive = false,
                parameters = new Dictionary<string, string> { ["Region"] = "APAC" }
            });
            Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);

            // 3. Verify changes in Portal database
            var getRes = await AuthGet(portalClient, token, $"/api/subscriptions/{subId}");
            var subBody = await getRes.Content.ReadFromJsonAsync<JsonObject>(_json);
            Assert.Equal("Weekly", subBody!["schedule"]!.GetValue<string>());
            Assert.Equal("Markdown", subBody!["format"]!.GetValue<string>());
            Assert.Equal(secondSmtpAlias, subBody!["smtpAlias"]!.GetValue<string>());
            Assert.Equal("new-recipient@test.local", subBody!["recipients"]!.GetValue<string>());
            Assert.False(subBody!["isActive"]!.GetValue<bool>());

            // 4. Verify changes in Orchestrator store
            var store = orchestratorFactory.Services.GetRequiredService<IJobHistoryStore>() as SQLiteJobHistoryStore;
            Assert.NotNull(store);
            var job = await store.GetJobAsync(jobName);
            Assert.NotNull(job);
            Assert.Equal(1, job.Interval); // Weekly parses to Interval 1, Unit WEEK
            Assert.Equal("WEEK", job.Unit);
            Assert.False(job.IsEnabled);

            // 5. Verify the persisted trigger remains free of delivery configuration.
            var generatedScriptPath = GeneratedSubscriptionScriptPath(portalFactory.TempDir, subId, reportName);
            var persistedScript = await File.ReadAllTextAsync(generatedScriptPath);
            Assert.Contains($"subscription {subId}", persistedScript, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("APAC", persistedScript, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("EMEA", persistedScript, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("portal-updated@example.com", persistedScript, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("new-recipient@test.local", persistedScript, StringComparison.OrdinalIgnoreCase);

            var reenableRes = await AuthPut(portalClient, token, $"/api/subscriptions/{subId}", new
            {
                isActive = true
            });
            Assert.Equal(HttpStatusCode.OK, reenableRes.StatusCode);

            var reenabledJob = await store.GetJobAsync(jobName);
            Assert.NotNull(reenabledJob);
            Assert.True(reenabledJob.IsEnabled);
        }

        [Fact]
        public async Task Verify_Subscription_Update_While_Running_Preserves_Active_Attempt_And_Future_Config()
        {
            using var portalFactory = new PortalWebFactory();
            using var orchestratorFactory = new OrchestratorWebFactory(portalFactory.TempDir);

            using var portalClient = portalFactory.CreateClient();
            using var orchClient = orchestratorFactory.CreateClient();

            var token = await GetAdminTokenAsync(portalClient);
            var reportScript = @"
SET REPORT TITLE = 'Concurrent Update Report';
WAITFOR DELAY '00:00:03';
SELECT 1 AS Val INTO #data;
CREATE VISUAL SalesTable AS TABLE (SOURCE = #data, MAPPINGS (Val = Val));
CREATE PAGE Page1 AS DASHBOARD(STRUCTURE = 'A', MAP ('A' = SalesTable));
";
            var (_, reportId, smtpAlias, reportName) = await SetupReportAndSmtpAsync(
                portalClient, token, portalFactory.TempDir, reportScript);

            var subRes = await AuthPost(portalClient, token, "/api/subscriptions", new
            {
                reportId,
                schedule = "Daily",
                format = "CSV",
                smtpAlias,
                recipientEmail = "before-update@test.local",
                atTime = "08:00"
            });
            Assert.Equal(HttpStatusCode.Created, subRes.StatusCode);
            var sub = await subRes.Content.ReadFromJsonAsync<JsonObject>(_json);
            var subId = sub!["id"]!.GetValue<int>();
            var jobName = $"SUB:{subId}:{reportName}";
            var store = orchestratorFactory.Services.GetRequiredService<IJobHistoryStore>() as SQLiteJobHistoryStore;
            Assert.NotNull(store);

            var generatedScriptPath = GeneratedSubscriptionScriptPath(portalFactory.TempDir, subId, reportName);
            await DelayGeneratedTriggerAsync(generatedScriptPath);
            await TriggerJobAsync(orchClient, jobName);
            await PollHistoryUntilStatusAsync(store, jobName, "RUNNING");

            var updateRes = await AuthPut(portalClient, token, $"/api/subscriptions/{subId}", new
            {
                schedule = "Weekly",
                recipients = "after-update@test.local"
            });
            Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);

            var history = await PollHistoryUntilCountAsync(store, jobName, 1);
            Assert.Equal("SUCCESS", history[0].Status);

            var getRes = await AuthGet(portalClient, token, $"/api/subscriptions/{subId}");
            var body = await getRes.Content.ReadFromJsonAsync<JsonObject>(_json);
            Assert.Equal("Weekly", body!["schedule"]!.GetValue<string>());
            Assert.Equal("after-update@test.local", body["recipients"]!.GetValue<string>());

            var job = await store.GetJobAsync(jobName);
            Assert.NotNull(job);
            Assert.Equal("WEEK", job.Unit);
            Assert.True(job.IsEnabled);

            var generatedScript = await File.ReadAllTextAsync(generatedScriptPath);
            Assert.Contains($"subscription {subId}", generatedScript, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("after-update@test.local", generatedScript, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("before-update@test.local", generatedScript, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Verify_Concurrent_Portal_And_Orchestrator_Sqlite_Writes_Preserve_All_Subscriptions()
        {
            using var portalFactory = new PortalWebFactory();
            using var orchestratorFactory = new OrchestratorWebFactory(portalFactory.TempDir);

            using var portalClient = portalFactory.CreateClient();
            using var orchClient = orchestratorFactory.CreateClient();

            var token = await GetAdminTokenAsync(portalClient);
            var reportScript = @"
SET REPORT TITLE = 'Mixed SQLite Writes Report';
WAITFOR DELAY '00:00:01';
SELECT 1 AS Val INTO #data;
CREATE VISUAL SalesTable AS TABLE (SOURCE = #data, MAPPINGS (Val = Val));
CREATE PAGE Page1 AS DASHBOARD(STRUCTURE = 'A', MAP ('A' = SalesTable));
";
            var (_, reportId, smtpAlias, reportName) = await SetupReportAndSmtpAsync(
                portalClient, token, portalFactory.TempDir, reportScript);

            var initialRes = await AuthPost(portalClient, token, "/api/subscriptions", new
            {
                reportId,
                schedule = "Daily",
                format = "CSV",
                smtpAlias,
                recipientEmail = "mixed-write-initial@test.local",
                atTime = "08:00"
            });
            Assert.Equal(HttpStatusCode.Created, initialRes.StatusCode);
            var initialSub = await initialRes.Content.ReadFromJsonAsync<JsonObject>(_json);
            var initialId = initialSub!["id"]!.GetValue<int>();
            var initialJobName = $"SUB:{initialId}:{reportName}";

            var store = orchestratorFactory.Services.GetRequiredService<IJobHistoryStore>() as SQLiteJobHistoryStore;
            Assert.NotNull(store);

            await DelayGeneratedTriggerAsync(
                GeneratedSubscriptionScriptPath(portalFactory.TempDir, initialId, reportName));
            await TriggerJobAsync(orchClient, initialJobName);
            await PollHistoryUntilStatusAsync(store, initialJobName, "RUNNING");

            var createTasks = Enumerable.Range(1, 4)
                .Select(i => AuthPost(portalClient, token, "/api/subscriptions", new
                {
                    reportId,
                    schedule = "Daily",
                    format = "CSV",
                    smtpAlias,
                    recipientEmail = $"mixed-write-{i}@test.local",
                    atTime = "08:00"
                }))
                .ToArray();

            var createResponses = await Task.WhenAll(createTasks);
            Assert.All(createResponses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));

            var createdIds = new List<int>();
            foreach (var response in createResponses)
            {
                var subscription = await response.Content.ReadFromJsonAsync<JsonObject>(_json);
                createdIds.Add(subscription!["id"]!.GetValue<int>());
            }
            Assert.Equal(4, createdIds.Distinct().Count());

            var history = await PollHistoryUntilCountAsync(store, initialJobName, 1);
            Assert.Equal("SUCCESS", history[0].Status);

            var listRes = await AuthGet(portalClient, token, "/api/subscriptions");
            Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
            var subscriptions = await listRes.Content.ReadFromJsonAsync<JsonArray>(_json);
            Assert.All(createdIds, id => Assert.Contains(subscriptions!, s => s!["id"]!.GetValue<int>() == id));

            foreach (var id in createdIds)
            {
                var jobName = $"SUB:{id}:{reportName}";
                Assert.NotNull(await store.GetJobAsync(jobName));
                Assert.True(File.Exists(GeneratedSubscriptionScriptPath(portalFactory.TempDir, id, reportName)));
            }
        }

        [Fact]
        public async Task Verify_Subscription_Delete_While_Running_Cleans_Up_Without_Stuck_Work()
        {
            using var portalFactory = new PortalWebFactory();
            using var orchestratorFactory = new OrchestratorWebFactory(portalFactory.TempDir);

            using var portalClient = portalFactory.CreateClient();
            using var orchClient = orchestratorFactory.CreateClient();

            var token = await GetAdminTokenAsync(portalClient);
            var reportScript = @"
SET REPORT TITLE = 'Concurrent Delete Report';
WAITFOR DELAY '00:00:03';
SELECT 1 AS Val INTO #data;
CREATE VISUAL SalesTable AS TABLE (SOURCE = #data, MAPPINGS (Val = Val));
CREATE PAGE Page1 AS DASHBOARD(STRUCTURE = 'A', MAP ('A' = SalesTable));
";
            var (_, reportId, smtpAlias, reportName) = await SetupReportAndSmtpAsync(
                portalClient, token, portalFactory.TempDir, reportScript);

            var subRes = await AuthPost(portalClient, token, "/api/subscriptions", new
            {
                reportId,
                schedule = "Daily",
                format = "CSV",
                smtpAlias,
                recipientEmail = "delete-running@test.local",
                atTime = "08:00"
            });
            Assert.Equal(HttpStatusCode.Created, subRes.StatusCode);
            var sub = await subRes.Content.ReadFromJsonAsync<JsonObject>(_json);
            var subId = sub!["id"]!.GetValue<int>();
            var jobName = $"SUB:{subId}:{reportName}";
            var generatedScriptPath = GeneratedSubscriptionScriptPath(portalFactory.TempDir, subId, reportName);
            var store = orchestratorFactory.Services.GetRequiredService<IJobHistoryStore>() as SQLiteJobHistoryStore;
            Assert.NotNull(store);

            await DelayGeneratedTriggerAsync(generatedScriptPath);
            await TriggerJobAsync(orchClient, jobName);
            await PollHistoryUntilStatusAsync(store, jobName, "RUNNING");

            var deleteRes = await AuthDelete(portalClient, token, $"/api/subscriptions/{subId}");
            Assert.Equal(HttpStatusCode.NoContent, deleteRes.StatusCode);

            var start = DateTime.UtcNow;
            while ((DateTime.UtcNow - start).TotalSeconds < 10)
            {
                var history = (await store.GetHistoryAsync(jobName, 10)).ToList();
                if (!history.Any(h => h.Status == "RUNNING" && h.EndTime == null))
                {
                    break;
                }
                await Task.Delay(200);
            }

            Assert.Null(await store.GetJobAsync(jobName));
            Assert.DoesNotContain(await store.GetHistoryAsync(jobName, 10), h => h.Status == "RUNNING" && h.EndTime == null);
            Assert.False(File.Exists(generatedScriptPath));

            var getRes = await AuthGet(portalClient, token, $"/api/subscriptions/{subId}");
            Assert.Equal(HttpStatusCode.NotFound, getRes.StatusCode);
        }

        [Fact]
        public async Task Verify_Subscription_Delete_Removes_Row_Script_And_Orchestrator_Job()
        {
            using var portalFactory = new PortalWebFactory();
            using var orchestratorFactory = new OrchestratorWebFactory(portalFactory.TempDir);

            using var portalClient = portalFactory.CreateClient();
            using var orchClient = orchestratorFactory.CreateClient();

            var token = await GetAdminTokenAsync(portalClient);

            var reportScript = @"
SET REPORT TITLE = 'Delete Test Report';
SELECT 1 AS Val INTO #data;
CREATE VISUAL SalesTable AS TABLE (SOURCE = #data, MAPPINGS (Val = Val));
CREATE PAGE Page1 AS DASHBOARD(STRUCTURE = 'A', MAP ('A' = SalesTable));
";
            var (_, reportId, smtpAlias, reportName) = await SetupReportAndSmtpAsync(portalClient, token, portalFactory.TempDir, reportScript);

            // 1. Create subscription
            var subRes = await AuthPost(portalClient, token, "/api/subscriptions", new
            {
                reportId,
                schedule = "Daily",
                format = "CSV",
                smtpAlias,
                recipientEmail = "delete@test.local",
                atTime = "08:00"
            });
            var sub = await subRes.Content.ReadFromJsonAsync<JsonObject>(_json);
            var subId = sub!["id"]!.GetValue<int>();
            var jobName = $"SUB:{subId}:{reportName}";

            var generatedScriptPath = GeneratedSubscriptionScriptPath(portalFactory.TempDir, subId, reportName);
            Assert.True(File.Exists(generatedScriptPath));

            var store = orchestratorFactory.Services.GetRequiredService<IJobHistoryStore>() as SQLiteJobHistoryStore;
            Assert.NotNull(store);
            Assert.NotNull(await store.GetJobAsync(jobName));

            await TriggerJobAsync(orchClient, jobName);
            var beforeDeleteHistory = await PollHistoryUntilCountAsync(store, jobName, 1);
            Assert.NotEmpty(beforeDeleteHistory);

            // 2. Delete subscription
            var delRes = await AuthDelete(portalClient, token, $"/api/subscriptions/{subId}");
            Assert.Equal(HttpStatusCode.NoContent, delRes.StatusCode);

            // 3. Assert removed from portal DB
            var goneGet = await AuthGet(portalClient, token, $"/api/subscriptions/{subId}");
            Assert.Equal(HttpStatusCode.NotFound, goneGet.StatusCode);

            // 4. Assert script file deleted
            Assert.False(File.Exists(generatedScriptPath));

            // 5. Assert Orchestrator job definition deleted
            Assert.Null(await store.GetJobAsync(jobName));
            Assert.Empty(await store.GetHistoryAsync(jobName, 10));
        }

        [Fact]
        public async Task Verify_Subscription_Failure_Scenario()
        {
            using var portalFactory = new PortalWebFactory();
            using var orchestratorFactory = new OrchestratorWebFactory(portalFactory.TempDir);

            using var portalClient = portalFactory.CreateClient();
            using var orchClient = orchestratorFactory.CreateClient();

            var token = await GetAdminTokenAsync(portalClient);

            var reportScript = @"
SET REPORT TITLE = 'Failure Test Report';
SELECT 1 AS Val INTO #data;
CREATE VISUAL SalesTable AS TABLE (SOURCE = #data, MAPPINGS (Val = Val));
CREATE PAGE Page1 AS DASHBOARD(STRUCTURE = 'A', MAP ('A' = SalesTable));
";
            var (_, reportId, smtpAlias, reportName) = await SetupReportAndSmtpAsync(portalClient, token, portalFactory.TempDir, reportScript);

            // 1. Create subscription
            var subRes = await AuthPost(portalClient, token, "/api/subscriptions", new
            {
                reportId,
                schedule = "Daily",
                format = "CSV",
                smtpAlias,
                recipientEmail = "failure@test.local",
                atTime = "08:00"
            });
            var sub = await subRes.Content.ReadFromJsonAsync<JsonObject>(_json);
            var subId = sub!["id"]!.GetValue<int>();
            var jobName = $"SUB:{subId}:{reportName}";

            // 2. Break script to trigger a controlled local failure (delete the report script on disk)
            var report = await portalFactory.Services.GetRequiredService<PortalDbContext>().Reports.FindAsync(reportId);
            Assert.NotNull(report);
            if (File.Exists(report.ScriptPath))
            {
                File.Delete(report.ScriptPath);
            }

            await TriggerJobAsync(orchClient, jobName);

            // 4. Verify job fails and logs sanitized failure
            var store = orchestratorFactory.Services.GetRequiredService<IJobHistoryStore>() as SQLiteJobHistoryStore;
            Assert.NotNull(store);
            var history = await PollHistoryUntilCountAsync(store, jobName, 1);
            Assert.Equal("SUCCESS", history[0].Status);
            await PollPortalOrchestratorAsync(portalFactory);

            // 5. The trigger succeeded; the portal records the delivery failure separately.
            var getHistRes = await AuthGet(portalClient, token, $"/api/subscriptions/{subId}/history");
            Assert.Equal(HttpStatusCode.OK, getHistRes.StatusCode);
            var portalHistory = await getHistRes.Content.ReadFromJsonAsync<List<JobHistoryEntry>>(_json);
            Assert.NotNull(portalHistory);
            var subEntry = Assert.Single(portalHistory);
            Assert.Equal("SUCCESS", subEntry.Status);

            var subGetRes = await AuthGet(portalClient, token, $"/api/subscriptions/{subId}");
            var subBody = await subGetRes.Content.ReadFromJsonAsync<JsonObject>(_json);
            Assert.Equal(1, subBody!["failCount"]!.GetValue<int>());

            var auditRes = await AuthGet(portalClient, token, "/api/admin/audit?action=SUBSCRIPTION_DELIVERY_FAILED");
            Assert.Equal(HttpStatusCode.OK, auditRes.StatusCode);
            var auditBody = await auditRes.Content.ReadFromJsonAsync<JsonObject>(_json);
            var auditItems = auditBody!["items"]!.AsArray();
            var auditEntry = Assert.Single(auditItems);
            Assert.Equal(subId.ToString(), auditEntry!["resourceId"]!.GetValue<string>());
            Assert.Contains("script file", auditEntry["detail"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase);

            var metricsRes = await AuthGet(portalClient, token, "/api/admin/metrics/usage");
            Assert.Equal(HttpStatusCode.OK, metricsRes.StatusCode);
            var metricsBody = await metricsRes.Content.ReadFromJsonAsync<JsonObject>(_json);
            Assert.Equal(1, metricsBody!["subscriptionDeliveryFailureCount"]!.GetValue<int>());
        }

        [Fact]
        public async Task Verify_Subscription_History_When_Orchestrator_Db_Is_Unavailable()
        {
            using var portalFactory = new FailingOrchestratorPortalFactory();
            var orchDbPath = Path.Combine(portalFactory.TempDir, "etlsql.db");
            File.Delete(orchDbPath);
            using var portalClient = portalFactory.CreateClient();

            var token = await GetAdminTokenAsync(portalClient);
            var reportScript = @"
SET REPORT TITLE = 'Unavailable Orchestrator Report';
SELECT 1 AS Val INTO #data;
CREATE VISUAL SalesTable AS TABLE (SOURCE = #data, MAPPINGS (Val = Val));
CREATE PAGE Page1 AS DASHBOARD(STRUCTURE = 'A', MAP ('A' = SalesTable));
";
            var (_, reportId, smtpAlias, _) = await SetupReportAndSmtpAsync(portalClient, token, portalFactory.TempDir, reportScript);
            var db = portalFactory.Services.GetRequiredService<PortalDbContext>();
            var subscription = new Subscription
            {
                ReportId = reportId,
                UserId = 1,
                Schedule = "Daily",
                Format = SubscriptionFormat.CSV,
                SmtpAlias = smtpAlias,
                Recipients = "orchestrator-unavailable@test.local",
                IsActive = true
            };
            db.Subscriptions.Add(subscription);
            await db.SaveChangesAsync();
            Assert.False(File.Exists(orchDbPath));

            var historyRes = await AuthGet(portalClient, token, $"/api/subscriptions/{subscription.Id}/history");
            Assert.Equal(HttpStatusCode.OK, historyRes.StatusCode);
            var history = await historyRes.Content.ReadFromJsonAsync<List<JobHistoryEntry>>(_json);
            Assert.NotNull(history);
            Assert.Empty(history);
            Assert.False(File.Exists(orchDbPath));

            var metricsRes = await AuthGet(portalClient, token, "/api/admin/metrics/usage");
            Assert.Equal(HttpStatusCode.OK, metricsRes.StatusCode);
            Assert.False(File.Exists(orchDbPath));
        }

        [Fact]
        public async Task Verify_Subscription_Create_Rejects_Missing_Smtp_Alias_For_Attachments()
        {
            using var portalFactory = new PortalWebFactory();
            using var portalClient = portalFactory.CreateClient();

            var token = await GetAdminTokenAsync(portalClient);

            var reportScript = @"
SET REPORT TITLE = 'Missing SMTP Alias Report';
SELECT 1 AS Val INTO #data;
CREATE VISUAL SalesTable AS TABLE (SOURCE = #data, MAPPINGS (Val = Val));
CREATE PAGE Page1 AS DASHBOARD(STRUCTURE = 'A', MAP ('A' = SalesTable));
";
            var (_, reportId, _, _) = await SetupReportAndSmtpAsync(portalClient, token, portalFactory.TempDir, reportScript);

            var subRes = await AuthPost(portalClient, token, "/api/subscriptions", new
            {
                reportId,
                schedule = "Daily",
                format = "CSV",
                recipientEmail = "missing-smtp@test.local",
                atTime = "08:00"
            });

            Assert.Equal(HttpStatusCode.BadRequest, subRes.StatusCode);
            var body = await subRes.Content.ReadFromJsonAsync<JsonObject>(_json);
            Assert.Contains("SmtpAlias is required", body!["error"]!.GetValue<string>());
        }

        [Theory]
        [InlineData("invalid-script")]
        [InlineData("unreachable-smtp")]
        public async Task Verify_Subscription_Controlled_Failure_Scenarios(string scenario)
        {
            using var portalFactory = new PortalWebFactory();
            using var orchestratorFactory = new OrchestratorWebFactory(portalFactory.TempDir);

            using var portalClient = portalFactory.CreateClient();
            using var orchClient = orchestratorFactory.CreateClient();

            var token = await GetAdminTokenAsync(portalClient);
            const string smtpSecret = "subscription-secret-should-not-leak";

            var reportScript = scenario == "invalid-script"
                ? "SET REPORT TITLE = 'Invalid Report';\nTHIS IS NOT VALID REPORT SQL;"
                : @"
SET REPORT TITLE = 'Failure Matrix Report';
SELECT 42 AS Val INTO #data;
CREATE VISUAL SalesTable AS TABLE (SOURCE = #data, MAPPINGS (Val = Val));
CREATE PAGE Page1 AS DASHBOARD(STRUCTURE = 'A', MAP ('A' = SalesTable));
";
            var (_, reportId, goodSmtpAlias, reportName) = await SetupReportAndSmtpAsync(portalClient, token, portalFactory.TempDir, reportScript);
            var smtpAlias = scenario == "unreachable-smtp"
                ? await CreateSmtpAliasAsync(portalClient, token, port: 9999, password: smtpSecret)
                : goodSmtpAlias;

            var subRes = await AuthPost(portalClient, token, "/api/subscriptions", new
            {
                reportId,
                schedule = "Daily",
                format = "CSV",
                smtpAlias,
                recipientEmail = $"{scenario}_{Guid.NewGuid():N}@example.com",
                atTime = "08:00"
            });
            Assert.Equal(HttpStatusCode.Created, subRes.StatusCode);
            var sub = await subRes.Content.ReadFromJsonAsync<JsonObject>(_json);
            var subId = sub!["id"]!.GetValue<int>();
            var jobName = $"SUB:{subId}:{reportName}";

            await TriggerJobAsync(orchClient, jobName);

            var store = orchestratorFactory.Services.GetRequiredService<IJobHistoryStore>() as SQLiteJobHistoryStore;
            Assert.NotNull(store);
            var history = await PollHistoryUntilCountAsync(store, jobName, 1);
            Assert.Equal("SUCCESS", history[0].Status);
            AssertNoSecretLeak(history[0].ErrorMessage, smtpSecret, "ENC:");
            await PollPortalOrchestratorAsync(portalFactory);

            var getHistRes = await AuthGet(portalClient, token, $"/api/subscriptions/{subId}/history");
            Assert.Equal(HttpStatusCode.OK, getHistRes.StatusCode);
            var portalHistory = await getHistRes.Content.ReadFromJsonAsync<List<JobHistoryEntry>>(_json);
            Assert.NotNull(portalHistory);
            Assert.Contains(portalHistory, h => h.Status == "SUCCESS");

            var subGetRes = await AuthGet(portalClient, token, $"/api/subscriptions/{subId}");
            var subBody = await subGetRes.Content.ReadFromJsonAsync<JsonObject>(_json);
            Assert.Equal(1, subBody!["failCount"]!.GetValue<int>());

            var auditRes = await AuthGet(
                portalClient, token, "/api/admin/audit?action=SUBSCRIPTION_DELIVERY_FAILED");
            var auditBody = await auditRes.Content.ReadFromJsonAsync<JsonObject>(_json);
            var detail = Assert.Single(auditBody!["items"]!.AsArray())!["detail"]!.GetValue<string>();
            AssertNoSecretLeak(detail, smtpSecret, "ENC:");
        }

        [Fact]
        public async Task Verify_Disabled_Subscription_Does_Not_Run_On_Scheduler_Loop()
        {
            using var portalFactory = new PortalWebFactory();
            using var orchestratorFactory = new OrchestratorWebFactory(portalFactory.TempDir);

            using var portalClient = portalFactory.CreateClient();

            var token = await GetAdminTokenAsync(portalClient);
            var reportScript = @"
SET REPORT TITLE = 'Disabled Subscription Report';
SELECT 1 AS Val INTO #data;
CREATE VISUAL SalesTable AS TABLE (SOURCE = #data, MAPPINGS (Val = Val));
CREATE PAGE Page1 AS DASHBOARD(STRUCTURE = 'A', MAP ('A' = SalesTable));
";
            var (_, reportId, smtpAlias, reportName) = await SetupReportAndSmtpAsync(portalClient, token, portalFactory.TempDir, reportScript);
            var subRes = await AuthPost(portalClient, token, "/api/subscriptions", new
            {
                reportId,
                schedule = "Hourly",
                format = "CSV",
                smtpAlias,
                recipientEmail = "disabled@test.local",
                atTime = "08:00"
            });
            Assert.Equal(HttpStatusCode.Created, subRes.StatusCode);
            var sub = await subRes.Content.ReadFromJsonAsync<JsonObject>(_json);
            var subId = sub!["id"]!.GetValue<int>();
            var jobName = $"SUB:{subId}:{reportName}";

            var updateRes = await AuthPut(portalClient, token, $"/api/subscriptions/{subId}", new { isActive = false });
            Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);

            var store = orchestratorFactory.Services.GetRequiredService<IJobHistoryStore>() as SQLiteJobHistoryStore;
            Assert.NotNull(store);
            var job = await store.GetJobAsync(jobName);
            Assert.NotNull(job);
            Assert.False(job.IsEnabled);

            var start = DateTime.UtcNow;
            while ((DateTime.UtcNow - start).TotalSeconds < 3)
            {
                Assert.Empty(await store.GetHistoryAsync(jobName, 10));
                await Task.Delay(500);
            }
        }
    }

    internal sealed class FailingOrchestratorPortalFactory : PortalWebFactory
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                // Create an isolated lock/epoch store so it doesn't touch the deleted etlsql.db
                var lockDbPath = Path.Combine(TempDir, "lock.db");
                var lockStore = new SQLiteJobHistoryStore(lockDbPath);

                services.RemoveAll<IClusterLockStore>();
                services.AddSingleton<IClusterLockStore>(lockStore);

                services.RemoveAll<IWriteEpochStore>();
                services.AddSingleton<IWriteEpochStore>(lockStore);

                // Re-register the store factory to throw on Create(), simulating database outage
                services.RemoveAll<IOrchestratorStoreFactory>();
                services.AddSingleton<IOrchestratorStoreFactory, FailingOrchestratorStoreFactory>();
            });
        }
    }

    internal sealed class FailingOrchestratorStoreFactory : IOrchestratorStoreFactory
    {
        public ETL_SQL.Common.DatabaseProvider Provider => ETL_SQL.Common.DatabaseProvider.Sqlite;
        public IJobHistoryStore Create(string? dbPath = null)
        {
            throw new Exception("Database connection failed (simulated outage).");
        }
    }
}
