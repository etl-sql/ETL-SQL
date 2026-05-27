using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using ETL_SQL.Data;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using ETL_SQL.Common;
using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;

namespace ETL_SQL.Tests.Integration
{
    public class ConnectionAlterTests : IDisposable
    {
        private readonly string _tempFile1;
        private readonly string _tempFile2;

        public ConnectionAlterTests()
        {
            _tempFile1 = Path.Combine(Path.GetTempPath(), $"test_alter_1_{Guid.NewGuid()}.csv");
            _tempFile2 = Path.Combine(Path.GetTempPath(), $"test_alter_2_{Guid.NewGuid()}.csv");
            File.WriteAllText(_tempFile1, "id,name\n1,Alice\n2,Bob");
            File.WriteAllText(_tempFile2, "id|name\n3|Charlie\n4|David");
        }

        public void Dispose()
        {
            if (File.Exists(_tempFile1)) File.Delete(_tempFile1);
            if (File.Exists(_tempFile2)) File.Delete(_tempFile2);
        }

        [Fact]
        public async Task TestAlterConnectionOptionsOnly()
        {
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            string script = $@"
                CREATE CONNECTION test_conn AS FLATFILE('{_tempFile1.Replace("\\", "/")}', HEADER = ON, DELIMITER = ',');
                
                -- Verify initial connection works
                SELECT * FROM test_conn;
                
                -- Alter options only (change delimiter - though this will fail reading existing file, but we test the merge)
                -- Actually, let's test a harmless option first
                ALTER CONNECTION test_conn WITH (TEST_OPT = 'ABC');
                
                -- Verify it still exists and has merged options
            ";

            await eval.Evaluate(new Lexer(script).TokenizeToScript());
            
            var ds = eval.Connections["test_conn"];
            Assert.NotNull(ds);
            Assert.Equal("FLATFILE", ds.ConnectorType);
            Assert.Contains("TEST_OPT", ds.Options.Keys);
            Assert.Equal("ABC", ds.Options["TEST_OPT"]);
            Assert.Equal("ON", ds.Options["HEADER"]);
        }

        [Fact]
        public async Task TestAlterConnectionPathAndOptions()
        {
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            string script = $@"
                CREATE CONNECTION test_conn AS FLATFILE('{_tempFile1.Replace("\\", "/")}', HEADER = ON, DELIMITER = ',');
                
                -- Alter path and delimiter
                ALTER CONNECTION test_conn AS FLATFILE('{_tempFile2.Replace("\\", "/")}', DELIMITER = 'PIPE');
                
                SELECT * FROM test_conn;
            ";

            await eval.Evaluate(new Lexer(script).TokenizeToScript());
            
            var result = eval.LastResult;
            Assert.NotNull(result);
            Assert.Equal(2, result.Rows.Count);
            Assert.Equal("Charlie", result.Rows[0]["name"]);
            
            var ds = eval.Connections["test_conn"];
            Assert.Equal("PIPE", ds.Options["DELIMITER"]);
            Assert.Equal("ON", ds.Options["HEADER"]); // Preserved from CREATE
        }

        [Fact]
        public async Task TestCreateOrAlterConnection()
        {
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            
            // 1. Create using CREATE OR ALTER
            string script1 = $@"
                CREATE OR ALTER CONNECTION test_coa AS FLATFILE('{_tempFile1.Replace("\\", "/")}', HEADER = ON);
                SELECT * FROM test_coa;
            ";
            await eval.Evaluate(new Lexer(script1).TokenizeToScript());
            Assert.Equal(2, eval.LastResult.Rows.Count);

            // 2. Alter using CREATE OR ALTER
            string script2 = $@"
                CREATE OR ALTER CONNECTION test_coa AS FLATFILE('{_tempFile2.Replace("\\", "/")}', DELIMITER = 'PIPE');
                SELECT * FROM test_coa;
            ";
            await eval.Evaluate(new Lexer(script2).TokenizeToScript());
            
            var result = eval.LastResult;
            Assert.Equal("Charlie", result.Rows[0]["name"]);
            
            var ds = eval.Connections["test_coa"];
            Assert.Equal("PIPE", ds.Options["DELIMITER"]);
            Assert.Equal("ON", ds.Options["HEADER"]); // Preserved
        }

        [Fact]
        public async Task TestAlterConnectionWithEncryptedString()
        {
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            string password = "TestPass123!";
            
            // Encrypt the second path
            string encryptedPath = CryptoUtils.Encrypt(_tempFile2.Replace("\\", "/"), password);

            string script = $@"
                USE PASSWORD = '{password}';
                CREATE CONNECTION test_enc AS FLATFILE('{_tempFile1.Replace("\\", "/")}', HEADER = ON);
                
                -- Alter with encrypted path
                ALTER CONNECTION test_enc AS FLATFILE('{encryptedPath}', DELIMITER = 'PIPE');
                
                SELECT * FROM test_enc;
            ";

            await eval.Evaluate(new Lexer(script).TokenizeToScript());
            
            var result = eval.LastResult;
            Assert.NotNull(result);
            Assert.Equal("Charlie", result.Rows[0]["name"]);
        }
    }
}
