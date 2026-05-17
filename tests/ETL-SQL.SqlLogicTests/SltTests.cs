using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace ETL_SQL.SqlLogicTests
{
    [Trait("Category", "SLT")]
    public class SltTests
    {
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
