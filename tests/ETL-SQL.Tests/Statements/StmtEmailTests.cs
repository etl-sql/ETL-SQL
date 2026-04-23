using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.App;
using ETL_SQL.Core.Common;
using ETL_SQL.Common;

namespace ETL_SQL.Tests.Statements
{
    public class EmailSyntaxTests
    {
        private class MockSmtpDataSource : IDataSource, IConnector
        {
            public List<Row> SentEmails { get; } = new();
            public string Name => "MOCK_SMTP";
            public IReadOnlyList<string> Aliases => new[] { "SMTP" };
            public string Path => "smtp://localhost";
            public Dictionary<string, string>? Options => null;
            public string ConnectorType => "SMTP";

            public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) => throw new NotSupportedException();
            public async Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false)
            {
                await foreach (var batch in batches)
                {
                    SentEmails.AddRange(batch.Rows);
                }
            }
            public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult((IEnumerable<string>)new[] { "To", "From", "Subject", "Body", "Cc", "Bcc", "Attachments" });
            public object? Snapshot() => null;
            public void Restore(object? snapshot) { }
            public IDataSource WithTable(string tableName) => this;
            public Task<string> GetVersionAsync(IExecutionContext context, string connectionString) => Task.FromResult("Mock SMTP 1.0");
            public HashSet<string> GetSupportedFunctions() => new();
            public HashSet<string> GetSupportedKeywords() => new();
            public Dictionary<string, string[]> GetSupportedOptions() => new();
            public Dictionary<string, string[]> GetOptionValues() => new();
            public string GetHelp() => "Mock SMTP";
            public IDataSource CreateDataSource(IExecutionContext context, string connectionString, Dictionary<string, string>? options = null) => this;
            public Task<IEnumerable<string>> GetTablesAsync(IExecutionContext context, string connectionString) => Task.FromResult(Enumerable.Empty<string>());
            public Task<IEnumerable<string>> GetViewsAsync(IExecutionContext context, string connectionString) => Task.FromResult(Enumerable.Empty<string>());
            public Task<IEnumerable<string>> GetColumnsAsync(IExecutionContext context, string connectionString, string tableName) => Task.FromResult(Enumerable.Empty<string>());
            public Task<IEnumerable<string>> GetProceduresAsync(IExecutionContext context, string connectionString) => Task.FromResult(Enumerable.Empty<string>());

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        private async Task RunScriptAsync(Evaluator evaluator, string sql)
        {
            var lexer = new Lexer(sql);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens, sql);
            var script = parser.Parse();
            if (script.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
                throw new Exception(script.Diagnostics.First(d => d.Severity == DiagnosticSeverity.Error).Message);
            await evaluator.Evaluate(script);
        }

        [Fact]
        public async Task Test_SqlStyle_SendEmail()
        {
            var services = DependencyInjectionSetup.BuildServiceProvider();
            var evaluator = services.GetRequiredService<Evaluator>();
            var mockSmtp = new MockSmtpDataSource();
            evaluator.Connections["MYSMTP"] = mockSmtp;

            string script = @"
                SEND EMAIL TO 'user@test.com'
                FROM 'sender@test.com'
                SUBJECT 'Test Email'
                BODY 'Hello from ETL-SQL'
                CC 'cc1@test.com'
                ATTACH 'C:\temp\test.txt'
                AT MYSMTP;
            ";

            await RunScriptAsync(evaluator, script);

            Assert.Single(mockSmtp.SentEmails);
            var email = mockSmtp.SentEmails[0];
            Assert.Equal("user@test.com", email["To"]);
            Assert.Equal("sender@test.com", email["From"]);
            Assert.Equal("Test Email", email["Subject"]);
            Assert.Equal("Hello from ETL-SQL", email["Body"]);
            Assert.Equal("cc1@test.com", email["Cc"]);
            Assert.Equal("C:\\temp\\test.txt", email["Attachments"]);
        }

        [Fact]
        public async Task Test_FunctionStyle_SendEmail()
        {
            var services = DependencyInjectionSetup.BuildServiceProvider();
            var evaluator = services.GetRequiredService<Evaluator>();
            var mockSmtp = new MockSmtpDataSource();
            evaluator.Connections["MYSMTP"] = mockSmtp;

            string script = "SEND_EMAIL(MYSMTP, 'to@test.com', 'from@test.com', 'Subj', 'Body');";

            await RunScriptAsync(evaluator, script);

            Assert.Single(mockSmtp.SentEmails);
            var email = mockSmtp.SentEmails[0];
            Assert.Equal("to@test.com", email["To"]);
            Assert.Equal("from@test.com", email["From"]);
            Assert.Equal("Subj", email["Subject"]);
            Assert.Equal("Body", email["Body"]);
        }

        [Fact]
        public async Task Test_SqlStyle_AnyOrder()
        {
            var services = DependencyInjectionSetup.BuildServiceProvider();
            var evaluator = services.GetRequiredService<Evaluator>();
            var mockSmtp = new MockSmtpDataSource();
            evaluator.Connections["MYSMTP"] = mockSmtp;

            string script = @"
                SEND EMAIL 
                FROM 'sender@test.com'
                SUBJECT 'Subject First'
                TO 'user@test.com'
                BODY 'Body Last'
                AT MYSMTP;
            ";

            await RunScriptAsync(evaluator, script);

            Assert.Single(mockSmtp.SentEmails);
            var email = mockSmtp.SentEmails[0];
            Assert.Equal("user@test.com", email["To"]);
            Assert.Equal("sender@test.com", email["From"]);
            Assert.Equal("Subject First", email["Subject"]);
            Assert.Equal("Body Last", email["Body"]);
        }

        [Fact]
        public async Task Test_MandatoryCheck_To()
        {
            var services = DependencyInjectionSetup.BuildServiceProvider();
            var evaluator = services.GetRequiredService<Evaluator>();
            evaluator.Connections["MYSMTP"] = new MockSmtpDataSource();

            string script = "SEND EMAIL FROM 'a' SUBJECT 'b' BODY 'c' AT MYSMTP;";
            await Assert.ThrowsAnyAsync<Exception>(async () => await RunScriptAsync(evaluator, script));
        }

        [Fact]
        public async Task Test_MandatoryCheck_From()
        {
            var services = DependencyInjectionSetup.BuildServiceProvider();
            var evaluator = services.GetRequiredService<Evaluator>();
            evaluator.Connections["MYSMTP"] = new MockSmtpDataSource();

            string script = "SEND EMAIL TO 'a' SUBJECT 'b' BODY 'c' AT MYSMTP;";
            await Assert.ThrowsAnyAsync<Exception>(async () => await RunScriptAsync(evaluator, script));
        }
    }
}
