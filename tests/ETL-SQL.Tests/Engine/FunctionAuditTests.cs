using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Engine
{
    public class FunctionAuditTests
    {
        private static Evaluator Eval() =>
            DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        private static Script Parse(string sql) =>
            new Parser(new Lexer(sql).Tokenize()).Parse();

        private static async Task<Evaluator> RunAndGetEval(string sql)
        {
            var eval = Eval();
            await eval.Evaluate(Parse(sql));
            return eval;
        }

        [Fact]
        public async Task TestBitwiseFunctions()
        {
            var sql = @"
                SELECT 
                    BITAND(12, 9) AS r1,
                    BITOR(12, 9) AS r2,
                    BITXOR(12, 9) AS r3,
                    BITNOT(0) AS r4,
                    BITSHIFTLEFT(4, 2) AS r5,
                    BITSHIFTRIGHT(16, 2) AS r6,
                    BIT_COUNT(9) AS r7,
                    BIT_COUNT(-1) AS r8;
            ";
            var eval = await RunAndGetEval(sql);
            var result = eval.LastResult;
            Assert.NotNull(result);
            Assert.Single(result.Rows);
            var row = result.Rows[0];

            Assert.Equal(8m, Convert.ToDecimal(row["r1"]));
            Assert.Equal(13m, Convert.ToDecimal(row["r2"]));
            Assert.Equal(5m, Convert.ToDecimal(row["r3"]));
            Assert.Equal(-1m, Convert.ToDecimal(row["r4"]));
            Assert.Equal(16m, Convert.ToDecimal(row["r5"]));
            Assert.Equal(4m, Convert.ToDecimal(row["r6"]));
            Assert.Equal(2m, Convert.ToDecimal(row["r7"]));
            Assert.Equal(64m, Convert.ToDecimal(row["r8"]));
        }

        [Fact]
        public async Task TestBitCountFunction()
        {
            var sql = @"
                SELECT 
                    BIT_COUNT(0) AS r0,
                    BIT_COUNT(1) AS r1,
                    BIT_COUNT(2) AS r2,
                    BIT_COUNT(7) AS r3,
                    BIT_COUNT(255) AS r4,
                    BIT_COUNT(256) AS r5,
                    BIT_COUNT(9223372036854775807) AS r6; -- Int64.MaxValue
            ";
            var eval = await RunAndGetEval(sql);
            var result = eval.LastResult;
            Assert.NotNull(result);
            Assert.Single(result.Rows);
            var row = result.Rows[0];

            Assert.Equal(0m, Convert.ToDecimal(row["r0"]));
            Assert.Equal(1m, Convert.ToDecimal(row["r1"]));
            Assert.Equal(1m, Convert.ToDecimal(row["r2"]));
            Assert.Equal(3m, Convert.ToDecimal(row["r3"]));
            Assert.Equal(8m, Convert.ToDecimal(row["r4"]));
            Assert.Equal(1m, Convert.ToDecimal(row["r5"]));
            Assert.Equal(63m, Convert.ToDecimal(row["r6"]));
        }

        [Fact]
        public async Task TestTrigAndConstants()
        {
            var sql = @"
                SELECT 
                    PI() AS r1,
                    DEGREES(PI()) AS r2,
                    RADIANS(180) AS r3,
                    COT(0.5) AS r4;
            ";
            var eval = await RunAndGetEval(sql);
            var result = eval.LastResult;
            Assert.NotNull(result);
            Assert.Single(result.Rows);
            var row = result.Rows[0];

            Assert.Equal((decimal)Math.PI, Convert.ToDecimal(row["r1"]));
            Assert.Equal(180m, Convert.ToDecimal(row["r2"]));
            Assert.Equal((decimal)Math.PI, Convert.ToDecimal(row["r3"]));
            Assert.Equal((decimal)(1.0 / Math.Tan(0.5)), Convert.ToDecimal(row["r4"]));
        }

        [Fact]
        public async Task TestStringPaddingAndRepeat()
        {
            var sql = @"
                SELECT 
                    LPAD('hello', 8, 'xy') AS r1,
                    LPAD('hello', 3) AS r2,
                    RPAD('hello', 8, 'xy') AS r3,
                    RPAD('hello', 3) AS r4,
                    REPEAT('abc', 3) AS r5;
            ";
            var eval = await RunAndGetEval(sql);
            var result = eval.LastResult;
            Assert.NotNull(result);
            Assert.Single(result.Rows);
            var row = result.Rows[0];

            Assert.Equal("xyxhello", row["r1"]);
            Assert.Equal("hel", row["r2"]);
            Assert.Equal("helloxyx", row["r3"]);
            Assert.Equal("hel", row["r4"]);
            Assert.Equal("abcabcabc", row["r5"]);
        }

        [Fact]
        public async Task TestFileMetadataAndPath()
        {
            string tempFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"temp_{Guid.NewGuid():N}.txt");
            try
            {
                File.WriteAllText(tempFile, "Hello ETL-SQL");

                // Note: since path may contain backslashes on Windows, we'll replace them or declare a variable.
                var sql = $@"
                    DECLARE @filePath = '{tempFile.Replace("\\", "\\\\")}';
                    SELECT 
                        FILE_SIZE(@filePath) AS r1,
                        FILE_HASH(@filePath, 'MD5') AS r2,
                        FILE_HASH(@filePath, 'SHA256') AS r3,
                        PATH_COMBINE('C:\Data', 'SubDir', 'file.txt') AS r4,
                        PATH_FILENAME('C:\Data\file.txt') AS r5,
                        PATH_EXTENSION('C:\Data\file.txt') AS r6,
                        PATH_DIRECTORY('C:\Data\file.txt') AS r7;
                ";

                var eval = await RunAndGetEval(sql);
                var result = eval.LastResult;
                Assert.NotNull(result);
                Assert.Single(result.Rows);
                var row = result.Rows[0];

                Assert.Equal(13m, Convert.ToDecimal(row["r1"]));

                // MD5 of "Hello ETL-SQL" is: d5dc69873f2942a0fe13613d4b85d7c7
                Assert.Equal("d5dc69873f2942a0fe13613d4b85d7c7", row["r2"]?.ToString()?.ToLowerInvariant());

                // SHA256 of "Hello ETL-SQL" is: d192f9f0fad9d6098342e795119a5a5b1f1205ef65119b5ad06e5acc587bb06d
                Assert.Equal("d192f9f0fad9d6098342e795119a5a5b1f1205ef65119b5ad06e5acc587bb06d", row["r3"]?.ToString()?.ToLowerInvariant());

                Assert.Equal(Path.Combine("C:\\Data", "SubDir", "file.txt"), row["r4"]);
                Assert.Equal("file.txt", row["r5"]);
                Assert.Equal(".txt", row["r6"]);
                Assert.Equal("C:\\Data", row["r7"]);
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }

        [Fact]
        public async Task TestConnectionProperty()
        {
            var sql = @"
                CREATE CONNECTION test_conn AS FLATFILE(PATH='C:\temp\myfile.csv', PASSWORD='my_secret_password', USER='tester');
                SELECT 
                    CONNECTION_PROPERTY('test_conn', 'PATH') AS r1,
                    CONNECTION_PROPERTY('test_conn', 'USER') AS r2,
                    CONNECTION_PROPERTY('test_conn', 'PASSWORD') AS r3,
                    CONNECTION_PROPERTY('test_conn', 'INVALID') AS r4;
            ";

            var eval = await RunAndGetEval(sql);
            var result = eval.LastResult;
            Assert.NotNull(result);
            Assert.Single(result.Rows);
            var row = result.Rows[0];

            Assert.Equal(@"C:\temp\myfile.csv", row["r1"]);
            Assert.Equal("tester", row["r2"]);
            Assert.Equal("********", row["r3"]);
            Assert.Null(row["r4"]);
        }
    }
}
