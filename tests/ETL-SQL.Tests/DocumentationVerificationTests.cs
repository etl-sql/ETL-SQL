using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using ETL_SQL.Core;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.App;
using ETL_SQL.Core.Common;

namespace ETL_SQL.Tests
{
    public class DocumentationVerificationTests
    {
        private async Task<object?> EvaluateExpressionAsync(string expression)
        {
            var services = DependencyInjectionSetup.BuildServiceProvider();
            var evaluator = services.GetRequiredService<Evaluator>();
            string script = $"DECLARE @result = {expression};";
            
            var lexer = new Lexer(script);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens, script);
            var parsedScript = parser.Parse();
            
            if (parsedScript.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
                throw new Exception(parsedScript.Diagnostics.First(d => d.Severity == DiagnosticSeverity.Error).Message);
                
            await evaluator.Evaluate(parsedScript);
            return evaluator.Variables["@result"];
        }

        private async Task RunScriptAsync(string script)
        {
            var services = DependencyInjectionSetup.BuildServiceProvider();
            var evaluator = services.GetRequiredService<Evaluator>();
            
            var lexer = new Lexer(script);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens, script);
            var parsedScript = parser.Parse();
            
            if (parsedScript.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
                throw new Exception(parsedScript.Diagnostics.First(d => d.Severity == DiagnosticSeverity.Error).Message);
                
            await evaluator.Evaluate(parsedScript);
        }

        [Fact]
        public async Task Test_Cast_Numeric()
        {
            var result = await EvaluateExpressionAsync("CAST('123.45' AS DECIMAL)");
            Assert.Equal(123.45m, result);
        }

        [Fact]
        public async Task Test_Cast_AliasedTypes()
        {
            var resultNvarchar = await EvaluateExpressionAsync("CAST(12345 AS NVARCHAR)");
            Assert.Equal("12345", resultNvarchar);

            var resultGuid = await EvaluateExpressionAsync("CAST('550e8400-e29b-41d4-a716-446655440000' AS GUID)");
            Assert.IsType<Guid>(resultGuid);
            Assert.Equal(Guid.Parse("550e8400-e29b-41d4-a716-446655440000"), resultGuid);
        }

        [Fact]
        public async Task Test_TryCast_Success()
        {
            var result = await EvaluateExpressionAsync("TRY_CAST('100' AS INT)");
            Assert.Equal(100, result);
        }

        [Fact]
        public async Task Test_TryCast_Failure()
        {
            var result = await EvaluateExpressionAsync("TRY_CAST('ABC' AS INT)");
            Assert.Null(result);
        }

        [Fact]
        public async Task Test_Math_Sign()
        {
            Assert.Equal(-1m, await EvaluateExpressionAsync("SIGN(-50)"));
            Assert.Equal(0m, await EvaluateExpressionAsync("SIGN(0)"));
            Assert.Equal(1m, await EvaluateExpressionAsync("SIGN(123)"));
        }

        [Fact]
        public async Task Test_Math_Trig()
        {
            var sinRes = await EvaluateExpressionAsync("SIN(3.14159 / 2)");
            Assert.InRange(Convert.ToDouble(sinRes), 0.99, 1.01);

            var asinRes = await EvaluateExpressionAsync("ASIN(1.0)");
            Assert.InRange(Convert.ToDouble(asinRes), 1.57, 1.58);
        }

        [Fact]
        public async Task Test_Math_Atan2()
        {
            var result = await EvaluateExpressionAsync("ATAN2(10.0, 10.0)");
            Assert.InRange(Convert.ToDouble(result), 0.78, 0.79);
        }

        [Fact]
        public async Task Test_SshKeyPair_Syntax()
        {
            // Just verify it doesn't crash during parsing/initial handling
            // We use a temp path to avoid polluting the workspace too much
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "test_id_rsa");
            try {
                await RunScriptAsync($"CREATE SSH_KEY_PAIR '{path}' WITH(BITS=1024);");
            } finally {
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
                if (System.IO.File.Exists(path + ".pub")) System.IO.File.Delete(path + ".pub");
            }
        }
    }
}
