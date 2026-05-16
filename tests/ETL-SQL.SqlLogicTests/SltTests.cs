using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace ETL_SQL.SqlLogicTests
{
    public class SltTests
    {
        [Fact]
        public async Task Debug_Select4_ExceptUnionChain()
        {
            var root = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "slt_data");
            if (!Directory.Exists(root))
                root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "slt_data");

            var filePath = Path.Combine(root, "corpus", "select4.test");
            using var runner = new SltRunner();
            runner.CurrentFile = filePath;
            var records = SltParser.ParseFile(filePath).ToList();

            foreach (var record in records)
            {
                if (record.LineNumber == 3229 && record.Type == SltRecordType.Query)
                {
                    // Run but capture result instead of verifying
                    var tokens = new ETL_SQL.Core.Parser.Lexer(record.Sql!).Tokenize();
                    var script = new ETL_SQL.Core.Parser.Parser(tokens, record.Sql!).Parse();
                    await runner.RunStatementDirectly(script);
                    var actual = runner.LastResult;
                    if (actual != null)
                    {
                        var values = actual.Rows
                            .SelectMany(r => actual.ColumnNames.Select(c => r[c]?.ToString() ?? "NULL"))
                            .OrderBy(v => v, StringComparer.Ordinal)
                            .ToList();
                        Assert.True(false, $"Got {values.Count} values (sorted): [{string.Join(", ", values)}]");
                    }
                    break;
                }
                if (record.Type == SltRecordType.Statement || record.Type == SltRecordType.Query)
                {
                    await runner.RunTestAsync(record);
                    if (record.LineNumber >= 3229) break;
                }
            }
        }

        [Theory]
        [MemberData(nameof(GetTestFiles))]
        public async Task RunSltTestFile(string filePath)
        {
            using var runner = new SltRunner();
            runner.CurrentFile = filePath;
            var records = SltParser.ParseFile(filePath);

            foreach (var record in records)
            {
                await runner.RunTestAsync(record);
            }
        }

        public static IEnumerable<object[]> GetTestFiles()
        {
            // Traverse the tests/slt_data directory
            // bin/Debug/net10.0 -> bin/Debug -> bin -> ETL-SQL.SqlLogicTests -> tests
            var root = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "slt_data");
            if (!Directory.Exists(root))
            {
                // Fallback for different build environments
                root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "slt_data");
            }

            if (!Directory.Exists(root)) return Enumerable.Empty<object[]>();

            return Directory.GetFiles(root, "*.test", SearchOption.AllDirectories)
                .Select(f => new object[] { f });
        }
    }
}
