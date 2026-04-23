using ETL_SQL.Core;
using ETL_SQL.Engine;
using ETL_SQL.Data;
using ETL_SQL.App;
using ETL_SQL.Connectors.Email;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

namespace ETL_SQL.Tests.Integration
{
    public class EmailTests
    {
        private readonly Evaluator _evaluator;
        private readonly ServiceProvider _serviceProvider;

        public EmailTests()
        {
            _serviceProvider = (ServiceProvider)DependencyInjectionSetup.BuildServiceProvider();
            _evaluator = _serviceProvider.GetRequiredService<Evaluator>();
            
        }

        [Fact]
        public async Task TestEmailStatementParsing()
        {
            string script = @"
                CREATE CONNECTION MyEmail TYPE SMTP TARGET 'localhost'
                WITH (PORT = 25);

                 SEND EMAIL FROM 'sender@example.com'
                    TO 'test@example.com'
                    SUBJECT 'Test Alert'
                    BODY 'This is a test message'
                    AT MyEmail;
            ";

            // If we can parse it, it means the grammar is correct
            var tokens = new Lexer(script).Tokenize();
            var parser = new Parser(tokens);
            var program = parser.Parse();

            Assert.Equal(2, program.Statements.Count);
            Assert.IsType<CreateConnectionStatement>(program.Statements[0]);
            Assert.IsType<EmailStatement>(program.Statements[1]);

            var emailStmt = (EmailStatement)program.Statements[1];
            Assert.Equal("'test@example.com'", emailStmt.To.ToSql());
            Assert.Equal("'Test Alert'", emailStmt.Subject.ToSql());
            Assert.Equal("'This is a test message'", emailStmt.Body.ToSql());
            Assert.Equal("MyEmail", emailStmt.ConnectionName.ToSql());
        }

        [Fact]
        public async Task TestEmailStatementWithCcAndAttachments()
        {
            string script = @"
                 SEND EMAIL FROM 'a@b.com' TO 'a@b.com'
                    SUBJECT 'S'
                    BODY 'B'
                    CC ['c@d.com', 'e@f.com']
                    BCC 'g@h.com'
                    ATTACH ['file1.txt', 'file2.txt'];
            ";

            var tokens = new Lexer(script).Tokenize();
            var parser = new Parser(tokens);
            var program = parser.Parse();

            var emailStmt = (EmailStatement)program.Statements[0];
            Assert.Equal(2, emailStmt.Cc.Count);
            Assert.Single(emailStmt.Bcc);
            Assert.Equal(2, emailStmt.Attachments.Count);
        }

        [Fact]
        public async Task TestEmailExecution()
        {
            // We'll mock a data source and check if WriteBatches is called
            var mockSmtp = new MockSmtpDataSource();
            _evaluator.Connections["TestSMTP"] = mockSmtp;

             string script = "SEND EMAIL FROM 'f@f.com' TO 't@t.com' SUBJECT 'S' BODY 'B' AT TestSMTP;";
            var tokens = new Lexer(script).Tokenize();
            var parser = new Parser(tokens);
            var program = parser.Parse();

            await _evaluator.Evaluate(program);

            Assert.True(mockSmtp.Sent);
            Assert.Equal("t@t.com", mockSmtp.LastTo);
        }
    }

    public class MockSmtpDataSource : IDataSource
    {
        public bool Sent { get; private set; }
        public string LastTo { get; private set; }
        public string Path => "mock";
        public Dictionary<string, string>? Options => null;
        public string ConnectorType => "SMTP";

        public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) { yield break; }

        public async Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false)
        {
            await foreach (var b in batches)
            {
                foreach (var r in b.Rows)
                {
                    Sent = true;
                    LastTo = r["To"]?.ToString();
                }
            }
        }

        public Task TruncateAsync() => Task.CompletedTask;
        public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult(Enumerable.Empty<string>());
        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }
        public IDataSource WithTable(string tableName) => this;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
