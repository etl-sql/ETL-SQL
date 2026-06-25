using System;
using System.IO;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Functions;
using ETL_SQL.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Hardening
{
    public class HardeningWave3Tests
    {
        [Fact]
        public async Task SetMaxParallelDegree_Unauthorized_ThrowsSecurityException()
        {
            var services = DependencyInjectionSetup.BuildServiceProvider();
            var eval = services.GetRequiredService<Evaluator>();
            var security = services.GetRequiredService<SecurityService>();

            // Set global limit to 16
            security.MaxParallelDegree = 16;

            // Ensure we are NOT in a safe zone (null script path)
            eval.CurrentScriptPath = null;

            // CRITICAL: Disable TestMode to trigger REAL security checks
            security.IsTestMode = false;

            // Attempt to set to 32 (exceeding 16)
            var ex = await Assert.ThrowsAsync<SecurityException>(() =>
                eval.EvaluateStatement(new SetThresholdStatement(ThresholdType.MaxParallelDegree, new LiteralExpression(32, TokenType.NUMBER))));

            Assert.Contains("exceeds the global limit of 16", ex.Message);
            Assert.Contains("Approved Safe Zone", ex.Message);
        }

        [Fact]
        public async Task SetMaxParallelDegree_AuthorizedInSafeZone_Succeeds()
        {
            var services = DependencyInjectionSetup.BuildServiceProvider();
            var eval = services.GetRequiredService<Evaluator>();
            var security = services.GetRequiredService<SecurityService>();

            // Set global limit to 16
            security.MaxParallelDegree = 16;

            // Configure a safe zone
            string safeDir = Path.Combine(Path.GetTempPath(), "SafeZone_" + Guid.NewGuid().ToString());
            Directory.CreateDirectory(safeDir);
            security.ApprovedSafeZones.Add(safeDir);

            security.IsTestMode = false;

            try
            {
                // Set current script path within safe zone
                eval.CurrentScriptPath = Path.Combine(safeDir, "script.etlsql");

                // Attempt to set to 32 (exceeding 16)
                await eval.EvaluateStatement(new SetThresholdStatement(ThresholdType.MaxParallelDegree, new LiteralExpression(32, TokenType.NUMBER)));

                Assert.Equal(32, eval.MaxParallelDegree);
            }
            finally
            {
                if (Directory.Exists(safeDir)) Directory.Delete(safeDir, true);
            }
        }

        [Fact]
        public async Task SetMaxParallelDegree_LoweringLimit_AlwaysSucceeds()
        {
            var services = DependencyInjectionSetup.BuildServiceProvider();
            var eval = services.GetRequiredService<Evaluator>();
            var security = services.GetRequiredService<SecurityService>();

            security.MaxParallelDegree = 32;
            eval.CurrentScriptPath = null; // No safe zone
            security.IsTestMode = false;

            // Lowering from 32 to 8 should succeed even outside safe zone
            await eval.EvaluateStatement(new SetThresholdStatement(ThresholdType.MaxParallelDegree, new LiteralExpression(8, TokenType.NUMBER)));

            Assert.Equal(8, eval.MaxParallelDegree);
        }

        [Fact]
        public async Task MaxStringResultSize_Enforced_ThrowsWhenExceeded()
        {
            var services = DependencyInjectionSetup.BuildServiceProvider();
            var eval = services.GetRequiredService<Evaluator>();
            var security = services.GetRequiredService<SecurityService>();

            // Set a very small limit: 1 KB
            security.MaxStringResultSize = 1024;
            eval.MaxStringResultSize = 1024;
            eval.CurrentScriptPath = null;
            security.IsTestMode = false;

            // REPLICATE('A', 2000) should exceed 1024
            var ex = await Assert.ThrowsAsync<SecurityException>(() =>
                eval.EvaluateValue(new FunctionCallExpression("REPLICATE", new List<Expression> {
                     new LiteralExpression("A", TokenType.STRING),
                     new LiteralExpression(2000, TokenType.NUMBER)
                }), new Row()).AsTask());

            Assert.Contains("Memory Safety Guardrail", ex.Message);
            Assert.Contains("1024 bytes", ex.Message);
        }

        [Fact]
        public async Task RegexTimeout_Enforced_ReturnsNullAndLogsWarning()
        {
            var services = DependencyInjectionSetup.BuildServiceProvider();
            var eval = services.GetService<Evaluator>()!;
            var security = services.GetService<SecurityService>()!;

            // Set a tiny timeout: 1ms (enough to fail any real regex)
            security.RegexMatchTimeout = TimeSpan.FromMilliseconds(1);
            eval.RegexMatchTimeoutMs = 1;
            eval.RedirectOutput = true;

            // Catastrophic backtracking regex: (a+)+ with long 'a' string and a 'b' at the end
            string pattern = "^(a+)+$";
            string input = new string('a', 30) + "b";

            // REGEXP_LIKE(input, pattern)
            var result = await eval.EvaluateValue(new FunctionCallExpression("REGEXP_LIKE", new List<Expression> {
                 new LiteralExpression(input, TokenType.STRING),
                 new LiteralExpression(pattern, TokenType.STRING)
             }), new Row());

            // The function catches the timeout and returns null
            Assert.Null(result);

            // Check for warning message in evaluator
            Assert.Contains(eval.Messages, m => m.Message.Contains("Regex timeout exceeded"));
        }

        [Fact]
        public async Task StandardRegexFunctions_EnforceTimeoutWithoutRegexFunctionRegistration()
        {
            var services = DependencyInjectionSetup.BuildServiceProvider();
            var eval = services.GetService<Evaluator>()!;
            var registry = new FunctionRegistry();
            StandardFunctions.Register(registry);

            eval.RegexMatchTimeoutMs = 1;
            eval.RedirectOutput = true;

            var input = new string('a', 30) + "b";
            var result = await registry.ExecuteAsync("REGEXP_LIKE", new List<object?> { input, "^(a+)+$" }, eval);

            Assert.Null(result);
            Assert.Contains(eval.Messages, m => m.Message.Contains("Regex timeout exceeded"));
        }

        [Fact]
        public async Task SetOperatorMemoryGrant_Succeeds()
        {
            var services = DependencyInjectionSetup.BuildServiceProvider();
            var eval = services.GetRequiredService<Evaluator>();

            // default is 256
            Assert.Equal(256, eval.OperatorMemoryGrantMB);

            // Execute SET OPERATOR_MEMORY_GRANT = 512
            await eval.EvaluateStatement(new SetThresholdStatement(ThresholdType.OperatorMemoryGrant, new LiteralExpression(512, TokenType.NUMBER)));

            Assert.Equal(512, eval.OperatorMemoryGrantMB);
        }

        [Fact]
        public async Task SetConnectionPreviewLimit_Succeeds()
        {
            var services = DependencyInjectionSetup.BuildServiceProvider();
            var eval = services.GetRequiredService<Evaluator>();

            // default is null
            Assert.Null(eval.PreviewLimit);

            // Execute SET CONNECTION_PREVIEW_LIMIT = 25
            await eval.EvaluateStatement(new SetThresholdStatement(ThresholdType.ConnectionPreviewLimit, new LiteralExpression(25, TokenType.NUMBER)));

            Assert.Equal(25, eval.PreviewLimit);
        }
    }
}
