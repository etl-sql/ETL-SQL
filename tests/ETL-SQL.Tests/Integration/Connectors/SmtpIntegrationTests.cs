using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Connectors.Email;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using ETL_SQL.Services;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Integration.Connectors
{
    /// <summary>
    /// Live SMTP integration tests using an axllent/mailpit container.  Requires Docker — run with:
    ///   dotnet test --filter "Category=Integration"
    ///
    /// The fixture starts one container per collection; all tests in this class share it.
    /// MailPit accepts all SMTP traffic without authentication and exposes an HTTP API that
    /// lets tests verify received messages.
    /// </summary>
    [Collection("SMTP collection")]
    [Trait("Category", "Integration")]
    [Trait("Connector", "SMTP")]
    [Trait("CertificationClass", "DockerRealIntegration")]
    public class SmtpIntegrationTests
    {
        private readonly SmtpFixture _smtp;

        public SmtpIntegrationTests(SmtpFixture smtp) => _smtp = smtp;

        // ── Helpers ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a minimal mock IExecutionContext with a permissive SecurityService (localhost is always
        /// allowed) and a NullLogger — sufficient for SmtpDataSource constructor + SendEmail.
        /// </summary>
        private static IExecutionContext MakeContext()
        {
            var security = new SecurityService(NullLogger.Instance);
            // IsTestMode is already true (test process detected); localhost is always allowed regardless.

            var ctx = new Mock<IExecutionContext>();
            ctx.Setup(c => c.SecurityService).Returns(security);
            ctx.Setup(c => c.Logger).Returns(NullLogger.Instance);
            ctx.Setup(c => c.RecordSmtpEmailSend());
            ctx.Setup(c => c.ResolvePath(It.IsAny<string>())).Returns<string>(p => p);
            return ctx.Object;
        }

        private SmtpDataSource MakeDataSource() =>
            new SmtpDataSource(MakeContext(), new Dictionary<string, string>
            {
                ["HOST"]    = _smtp.SmtpHost,
                ["PORT"]    = _smtp.SmtpPort.ToString(),
                ["USE_SSL"] = "false"
            });

        private static async Task<DataTable> OneRowBatch(string to, string subject, string body)
        {
            var table = new DataTable();
            table.SetColumns(new[] { "To", "From", "Subject", "Body" });
            var row = table.NewRow();
            row["To"]      = to;
            row["From"]    = "sender@etl-sql.test";
            row["Subject"] = subject;
            row["Body"]    = body;
            await table.AddRowAsync(row);
            return table;
        }

        // ── 1. Successful send — MailPit receives the message ─────────────────────

        [Fact]
        public async Task Send_ValidMessage_MailPitReceivesIt()
        {
            int beforeCount = await _smtp.GetMessageCountAsync();

            var ds = MakeDataSource();
            var batch = await OneRowBatch(
                to:      "recipient@example.com",
                subject: "Integration test email",
                body:    "Hello from SmtpIntegrationTests");

            await ds.WriteBatches(new[] { batch }.ToAsyncEnumerable());

            int afterCount = await _smtp.GetMessageCountAsync();
            Assert.Equal(beforeCount + 1, afterCount);
        }

        // ── 2. Multiple messages in one batch ─────────────────────────────────────

        [Fact]
        public async Task Send_TwoRows_MailPitReceivesBoth()
        {
            int beforeCount = await _smtp.GetMessageCountAsync();

            var ds = MakeDataSource();
            var table = new DataTable();
            table.SetColumns(new[] { "To", "From", "Subject", "Body" });

            for (int i = 1; i <= 2; i++)
            {
                var row = table.NewRow();
                row["To"]      = $"user{i}@example.com";
                row["From"]    = "sender@etl-sql.test";
                row["Subject"] = $"Batch message {i}";
                row["Body"]    = $"Body {i}";
                await table.AddRowAsync(row);
            }

            await ds.WriteBatches(new[] { table }.ToAsyncEnumerable());

            int afterCount = await _smtp.GetMessageCountAsync();
            Assert.Equal(beforeCount + 2, afterCount);
        }

        // ── 3. Connection refused — wraps as ExecutionException ───────────────────

        [Fact]
        public async Task Send_UnreachablePort_WrapsAsExecutionException()
        {
            var ds = new SmtpDataSource(MakeContext(), new Dictionary<string, string>
            {
                ["HOST"]    = "127.0.0.1",
                ["PORT"]    = "1",          // port 1 is always refused
                ["USE_SSL"] = "false"
            });

            var batch = await OneRowBatch("r@example.com", "Fail", "body");
            var ex = await Assert.ThrowsAsync<ExecutionException>(
                () => ds.WriteBatches(new[] { batch }.ToAsyncEnumerable()));
            Assert.Contains("SMTP", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // ── 4. Host not in allowlist — SecurityException before any connection ────

        [Fact]
        public void BlockedHost_ThrowsSecurityException()
        {
            var security = new SecurityService(NullLogger.Instance);
            security.IsTestMode = false;   // disable auto-bypass so the allowlist is enforced
            security.AllowedHosts.Clear(); // no hosts permitted (not even localhost override is hit for non-loopback)

            var ctx = new Mock<IExecutionContext>();
            ctx.Setup(c => c.SecurityService).Returns(security);
            ctx.Setup(c => c.Logger).Returns(NullLogger.Instance);

            Assert.Throws<SecurityException>(() =>
                new SmtpDataSource(ctx.Object, new Dictionary<string, string>
                {
                    ["HOST"] = "smtp.blocked.example.com",
                    ["PORT"] = "587"
                }));
        }

        // ── 5. Credential masking — exception message does not expose password ────

        [Fact]
        public async Task Send_WithCredentials_ExceptionDoesNotLeakPassword()
        {
            const string password = "super-secret-smtp-password";

            var ds = new SmtpDataSource(MakeContext(), new Dictionary<string, string>
            {
                ["HOST"]     = "127.0.0.1",
                ["PORT"]     = "1",
                ["USERNAME"] = "user@example.com",
                ["PASSWORD"] = password,
                ["USE_SSL"]  = "false"
            });

            var batch = await OneRowBatch("r@example.com", "Creds test", "body");
            var ex = await Assert.ThrowsAsync<ExecutionException>(
                () => ds.WriteBatches(new[] { batch }.ToAsyncEnumerable()));

            Assert.DoesNotContain(password, ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Send_AttachmentOutsideSafeZone_ThrowsSecurityExceptionBeforeConnect()
        {
            var security = new SecurityService(NullLogger.Instance);
            security.IsTestMode = false;

            var ctx = new Mock<IExecutionContext>();
            ctx.Setup(c => c.SecurityService).Returns(security);
            ctx.Setup(c => c.Logger).Returns(NullLogger.Instance);
            ctx.Setup(c => c.RecordSmtpEmailSend());
            ctx.Setup(c => c.ResolvePath(It.IsAny<string>())).Returns<string>(p => p);

            var ds = new SmtpDataSource(ctx.Object, new Dictionary<string, string>
            {
                ["HOST"] = "127.0.0.1",
                ["PORT"] = "1",
                ["USE_SSL"] = "false"
            });

            var table = new DataTable();
            table.SetColumns(new[] { "To", "From", "Subject", "Body", "Attachments" });
            var row = table.NewRow();
            row["To"] = "recipient@example.com";
            row["From"] = "sender@etl-sql.test";
            row["Subject"] = "Blocked attachment";
            row["Body"] = "body";
            row["Attachments"] = OperatingSystem.IsWindows() ? @"C:\Windows\system.ini" : "/etc/passwd";
            await table.AddRowAsync(row);

            await Assert.ThrowsAsync<SecurityException>(
                () => ds.WriteBatches(new[] { table }.ToAsyncEnumerable()));
        }
    }
}
