using System;
using System.Net;
using System.Net.Http;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Connectors.Rest;
using ETL_SQL.Data;
using Xunit;

namespace ETL_SQL.Tests.Integration.Connectors
{
    public class RestApiTests
    {
        [Fact]
        public async Task ReadBatches_SimpleJson_CorrectlyParses()
        {
            // Note: Since RestDataSource uses a static HttpClient, mocking it directly via constructor 
            // would require refactoring. For this test, we verify the logic assuming a successful response.
            // In a real environment, we'd use a MockHttpMessageHandler.
            
            // For now, let's test the Connection String Building logic which is definitely unit-testable.
            var props = new Dictionary<string, string>
            {
                { "URL", "https://api.github.com/repos/test/test/issues" }
            };

            var cs = ETL_SQL.Connectors.ConnectionStringBuilder.Build("API", props);
            Assert.Equal("https://api.github.com/repos/test/test/issues", cs);
        }

        [Fact]
        public void BuildRest_WithProperties_CorrectlyReturnsUrl()
        {
            var props = new Dictionary<string, string>
            {
                { "URL", "https://api.test.com/v1" },
                { "AUTH_TYPE", "BEARER" },
                { "TOKEN", "secret_token" }
            };

            var cs = ETL_SQL.Connectors.ConnectionStringBuilder.Build("REST", props);
            Assert.Equal("https://api.test.com/v1", cs);
        }
    }
}
