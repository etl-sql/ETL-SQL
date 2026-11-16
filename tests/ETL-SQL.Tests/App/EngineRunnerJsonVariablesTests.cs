using System.Collections.Generic;
using System.Linq;
using ETL_SQL.App;
using ETL_SQL.Core.Common;
using ETL_SQL.Data;
using Xunit;

namespace ETL_SQL.Tests.CliCommands
{
    /// <summary>
    /// The <c>--json</c> run streams a "variables" event every 500ms while the script runs, before
    /// the end-of-run secret purge. SECRET and SENSITIVE values must never reach that stream.
    /// </summary>
    public class EngineRunnerJsonVariablesTests
    {
        [Fact]
        public void VariablesPayload_MasksSecretAndSensitiveAndRedactsTheRest()
        {
            var vars = new Dictionary<string, (object? Value, VariableMetadata Metadata)>
            {
                ["@apiKey"] = ("resolved-plaintext-key", new VariableMetadata { IsSecret = true }),
                ["@pwd"] = ("hunter2-plaintext", new VariableMetadata { IsSensitive = true }),
                ["@conn"] = ("Server=db;Password=leaky;", new VariableMetadata()),
                ["@rows"] = (42m, new VariableMetadata()),
                ["@nothing"] = (null, new VariableMetadata { IsSecret = true }),
            };

            var payload = EngineRunner.BuildVariablesPayload(vars).ToDictionary(v => v.Name);
            var json = System.Text.Json.JsonSerializer.Serialize(payload.Values);

            Assert.Equal(SecretRedactor.Mask, payload["@apiKey"].Value);
            Assert.Equal(SecretRedactor.Mask, payload["@pwd"].Value);
            Assert.DoesNotContain("resolved-plaintext-key", json);
            Assert.DoesNotContain("hunter2-plaintext", json);
            Assert.DoesNotContain("leaky", json);
            Assert.Equal("42", payload["@rows"].Value);
            Assert.Equal("Decimal", payload["@rows"].Type);
            Assert.Equal("null", payload["@nothing"].Value);
            // Wire shape the VS Code extension reads.
            Assert.Contains("\"name\":\"@rows\"", json);
            Assert.Contains("\"value\":\"42\"", json);
            Assert.Contains("\"type\":\"Decimal\"", json);
        }
    }
}
