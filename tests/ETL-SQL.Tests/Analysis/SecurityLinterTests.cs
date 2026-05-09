using Xunit;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.Analysis.Linting.Rules;
using ETL_SQL.Core.Parser;

namespace ETL_SQL.Tests.Analysis
{
    public class SecurityLinterTests
    {
        private Script Parse(string sql)
        {
            var lexer = new Lexer(sql);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens);
            return parser.Parse();
        }

        [Fact]
        public async Task TestAbsolutePathRule()
        {
            var linter = new Linter();
            linter.AddRule(new AbsolutePathRule());

            var sql = @"
                CREATE CONNECTION c1 ON FLATFILE('C:\Data\file.csv'); -- Absolute (OK)
                CREATE CONNECTION c2 ON FLATFILE('data\file.csv');   -- Relative (Warn)
                RUN SCRIPT 'scripts\setup.etlsql';                   -- Relative (Warn)
                COPY FILE 'C:\temp\a.txt' TO 'b.txt';                -- Destination Relative (Warn)
                BULK INSERT INTO #t FROM 'data.csv';                 -- Relative (Warn)
            ";

            var script = Parse(sql);
            var results = await linter.AnalyzeAsync(script, new DefaultLintContext());

            // Expect 4 warnings
            var messages = results.Select(r => r.Message).ToList();
            Assert.True(results.Count(r => r.Severity == LintSeverity.Warning) == 4, $"Expected 4 warnings, but got {results.Count}. Messages: {string.Join(", ", messages)}");
            Assert.Contains(results, r => r.Message.Contains("Relative path detected: 'data\\file.csv'"));
            Assert.Contains(results, r => r.Message.Contains("Relative path detected: 'scripts\\setup.etlsql'"));
            Assert.Contains(results, r => r.Message.Contains("Relative path detected: 'b.txt'"));
            Assert.Contains(results, r => r.Message.Contains("Relative path detected: 'data.csv'"));
        }

        [Fact]
        public async Task TestFileSystemSecurityRule_ForbiddenFolders()
        {
            var linter = new Linter();
            linter.AddRule(new FileSystemSecurityRule());

            var sql = @"
                CREATE CONNECTION c1 ON FLATFILE('C:\Windows\System32\drivers\etc\hosts');
                RUN SCRIPT '/etc/shadow';
                COPY FILE '.git\config' TO 'C:\backups\git_config.txt';
                BULK INSERT INTO #t FROM 'C:\bin\tools.csv';
            ";

            var script = Parse(sql);
            var results = await linter.AnalyzeAsync(script, new DefaultLintContext());

            // Expect 4 security warnings
            var messages = results.Select(r => r.Message).ToList();
            Assert.True(results.Count(r => r.Severity == LintSeverity.Warning) == 4, $"Expected 4 security warnings, but got {results.Count}. Messages: {string.Join(", ", messages)}");
            Assert.Contains(results, r => r.Message.Contains("system directory 'C:\\Windows\\System32\\drivers\\etc\\hosts'"));
            Assert.Contains(results, r => r.Message.Contains("system directory '/etc/shadow'"));
            Assert.Contains(results, r => r.Message.Contains("system directory '.git\\config'"));
            Assert.Contains(results, r => r.Message.Contains("system directory 'C:\\bin\\tools.csv'"));
        }

        [Fact]
        public async Task TestFileSystemSecurityRule_DriveRoot()
        {
            var linter = new Linter();
            linter.AddRule(new FileSystemSecurityRule());

            var sql = @"
                CREATE CONNECTION c1 ON FLATFILE('C:\');
                CREATE CONNECTION c2 ON FLATFILE('D:/');
                CREATE CONNECTION c3 ON FLATFILE('C:\SafeFolder\'); -- OK
            ";

            var script = Parse(sql);
            var results = await linter.AnalyzeAsync(script, new DefaultLintContext());

            // Expect 2 security warnings for C:\ and D:/
            Assert.Equal(2, results.Count(r => r.Severity == LintSeverity.Warning));
            Assert.Contains(results, r => r.Message.Contains("drive root 'C:\\'"));
            Assert.Contains(results, r => r.Message.Contains("drive root 'D:/'"));
        }

        [Fact]
        public async Task TestAbsolutePathRule_Exemptions()
        {
            var linter = new Linter();
            linter.AddRule(new AbsolutePathRule());

            var sql = @"
                CREATE CONNECTION c1 ON FLATFILE('ENC:base64stuff'); -- Secret (OK)
                CREATE CONNECTION c2 ON FLATFILE('s3://bucket/file.csv'); -- URL (OK)
                CREATE CONNECTION c3 ON MSSQL() WITH(SERVER='localhost'); -- Not a file connector (OK)
            ";

            var script = Parse(sql);
            var results = await linter.AnalyzeAsync(script, new DefaultLintContext());

            Assert.Empty(results);
        }
    }
}
