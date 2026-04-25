using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Engine;
using ETL_SQL.Tests.Core;
using Xunit;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace ETL_SQL.Tests.Security
{
    public class AutoDecryptionTests
    {
        private static Evaluator CreateEvaluator() => 
            DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        [Fact]
        public async Task SensitiveVariable_WithEncString_ShouldAutoDecryptInConnection()
        {
            var evaluator = CreateEvaluator();
            evaluator.ScriptPassword = "test-password";
            
            // 1. Generate an encrypted value
            var rawValue = "secret-credential";
            var encValue = ETL_SQL.Common.CryptoUtils.Encrypt(rawValue, evaluator.ScriptPassword);
            
            // 2. Declare as SENSITIVE
            await evaluator.EvaluateStatement(new DeclareStatement("@pwd", "VARCHAR", new LiteralExpression(encValue, TokenType.STRING), isSensitive: true, false, false));
            
            // 3. Use in a connection-like context (we'll just use EvaluateValue with decryptSensitive: true)
            var expr = new VariableExpression("@pwd");
            var result = await evaluator.EvaluateValue(expr, new Row(), decryptSensitive: true);
            
            Assert.Equal(rawValue, result?.ToString());
        }

        [Fact]
        public async Task NormalVariable_WithEncString_ShouldNOTAutoDecrypt()
        {
            var evaluator = CreateEvaluator();
            evaluator.ScriptPassword = "test-password";
            
            var rawValue = "secret-credential";
            var encValue = ETL_SQL.Common.CryptoUtils.Encrypt(rawValue, evaluator.ScriptPassword);
            
            // Declare as NOT sensitive
            await evaluator.EvaluateStatement(new DeclareStatement("@pwd", "VARCHAR", new LiteralExpression(encValue, TokenType.STRING), isSensitive: false, false, false));
            
            var expr = new VariableExpression("@pwd");
            var result = await evaluator.EvaluateValue(expr, new Row(), decryptSensitive: true);
            
            // Should still be encrypted because it's not marked SENSITIVE
            Assert.Equal(encValue, result?.ToString());
        }

        [Fact]
        public async Task SensitiveVariable_InNormalContext_ShouldNOTAutoDecrypt()
        {
            var evaluator = CreateEvaluator();
            evaluator.ScriptPassword = "test-password";
            
            var rawValue = "secret-credential";
            var encValue = ETL_SQL.Common.CryptoUtils.Encrypt(rawValue, evaluator.ScriptPassword);
            
            await evaluator.EvaluateStatement(new DeclareStatement("@pwd", "VARCHAR", new LiteralExpression(encValue, TokenType.STRING), isSensitive: true, false, false));
            
            var expr = new VariableExpression("@pwd");
            // decryptSensitive is FALSE by default
            var result = await evaluator.EvaluateValue(expr, new Row());
            
            Assert.Equal(encValue, result?.ToString());
        }
    }
}
