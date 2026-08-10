using System;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Tests.Core;
using Xunit;

namespace ETL_SQL.Tests.Hardening
{
    public class OutputRedactionAndArithmeticSafetyTests
    {
        [Fact]
        public async Task MathOverflow_ReturnsNull()
        {
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

            // Decimal.MaxValue is ~7.9e28. Multiplying it by 10 should overflow.
            await TestHelpers.Execute(eval, "DECLARE @Large DECIMAL = 79228162514264337593543950335; SELECT @Large * 10 AS Res;");
            var result = eval.LastResult?.Rows[0]["Res"];
            Assert.Null(result); // Should be NULL due to overflow protection
        }

        [Fact]
        public async Task CredentialScrubbing_MasksPasswords()
        {
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            eval.RedirectOutput = true;

            // Intentional leak in PRINT
            await TestHelpers.Execute(eval, "PRINT 'Connecting with password=Secret123; and token=ABC';");

            var msg = eval.Messages[0].Message;
            Assert.Contains("password=********", msg);
            Assert.Contains("token=********", msg);
            Assert.DoesNotContain("Secret123", msg);
        }

        [Fact]
        public async Task EncryptedScrubbing_MasksEncConstants()
        {
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            eval.RedirectOutput = true;

            await TestHelpers.Execute(eval, "PRINT 'The value is ENC:aGVsbG8=';");

            var msg = eval.Messages[0].Message;
            Assert.Contains("ENC:********", msg);
            Assert.DoesNotContain("aGVsbG8=", msg);
        }

    }
}
