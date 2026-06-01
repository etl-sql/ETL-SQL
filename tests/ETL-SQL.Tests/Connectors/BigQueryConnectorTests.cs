using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Google.Cloud.BigQuery.V2;
using Xunit;
using ETL_SQL.Connectors.BigQuery;

namespace ETL_SQL.Tests.Connectors
{
    [Trait("Connector", "BIGQUERY")]
    [Trait("CertificationClass", "MetadataOnly")]
    public class BigQueryConnectorTests
    {
        private readonly BigQueryConnector _connector = new();

        // ── BuildConnectionString ─────────────────────────────────────────────

        [Fact]
        public void BuildConnectionString_AllOptions_ProducesCorrectParts()
        {
            var props = new Dictionary<string, string>
            {
                { "PROJECT_ID",      "my-gcp-project" },
                { "DATASET",         "analytics" },
                { "LOCATION",        "US" },
                { "CREDENTIAL_FILE", "/etc/sa/key.json" }
            };

            var cs = _connector.BuildConnectionString(props);

            Assert.Contains("project=my-gcp-project", cs);
            Assert.Contains("dataset=analytics", cs);
            Assert.Contains("location=US", cs);
            Assert.Contains("credential_file=/etc/sa/key.json", cs);
        }

        [Fact]
        public void BuildConnectionString_AdcMode_OmitsCredentialFile()
        {
            var props = new Dictionary<string, string>
            {
                { "PROJECT_ID", "my-gcp-project" },
                { "DATASET",    "analytics" }
            };

            var cs = _connector.BuildConnectionString(props);

            Assert.Contains("project=my-gcp-project", cs);
            Assert.DoesNotContain("credential_file", cs);
        }

        [Fact]
        public void BuildConnectionString_EmptyCredentialFile_OmitsCredentialFile()
        {
            var props = new Dictionary<string, string>
            {
                { "PROJECT_ID",      "my-gcp-project" },
                { "CREDENTIAL_FILE", "" }
            };

            var cs = _connector.BuildConnectionString(props);
            Assert.DoesNotContain("credential_file", cs);
        }

        [Fact]
        public void BuildConnectionString_MinimalProperties_DoesNotThrow()
        {
            var props = new Dictionary<string, string> { { "PROJECT_ID", "proj" } };
            var cs = _connector.BuildConnectionString(props);
            Assert.NotEmpty(cs);
        }

        // ── ParseField ───────────────────────────────────────────────────────

        [Fact]
        public void ParseField_FindsProjectId()
        {
            var proj = BigQueryConnector.ParseField("project=my-proj;dataset=ds;", "project");
            Assert.Equal("my-proj", proj);
        }

        [Fact]
        public void ParseField_CaseInsensitive()
        {
            var proj = BigQueryConnector.ParseField("PROJECT=my-proj;", "project");
            Assert.Equal("my-proj", proj);
        }

        [Fact]
        public void ParseField_MissingKey_ReturnsNull()
        {
            var val = BigQueryConnector.ParseField("project=my-proj;", "nonexistent");
            Assert.Null(val);
        }

        [Fact]
        public void ParseField_ValueWithEquals_PreservesFullValue()
        {
            var val = BigQueryConnector.ParseField("dataset=my_ds;credential_file=path=with=equals;", "credential_file");
            Assert.Equal("path=with=equals", val);
        }

        // ── GetHost ──────────────────────────────────────────────────────────

        [Fact]
        public void GetHostStatic_AlwaysReturnsBigQueryEndpoint()
        {
            var host = BigQueryConnector.GetHostStatic("project=proj;dataset=ds;");
            Assert.Equal("bigquery.googleapis.com", host);
        }

        [Fact]
        public void GetHostStatic_WithOptions_StillReturnsBigQueryEndpoint()
        {
            var opts = new Dictionary<string, string> { { "PROJECT_ID", "proj" } };
            var host = BigQueryConnector.GetHostStatic("project=proj;", opts);
            Assert.Equal("bigquery.googleapis.com", host);
        }

        [Fact]
        public void GetHost_InstanceMethod_DelegatesToStatic()
        {
            var host = _connector.GetHost("project=proj;");
            Assert.Equal("bigquery.googleapis.com", host);
        }

        // ── Connector metadata ────────────────────────────────────────────────

        [Fact]
        public void Name_IsBigQuery()
        {
            Assert.Equal("BIGQUERY", _connector.Name);
        }

        [Fact]
        public void GetSupportedOptions_ContainsAllExpectedKeys()
        {
            var opts = _connector.GetSupportedOptions();
            Assert.True(opts.ContainsKey("PROJECT_ID"));
            Assert.True(opts.ContainsKey("DATASET"));
            Assert.True(opts.ContainsKey("CREDENTIAL_FILE"));
            Assert.True(opts.ContainsKey("LOCATION"));
        }

        [Fact]
        public void GetSupportedFunctions_DelegatesToBigQuerySyntax()
        {
            Assert.Equal(BigQuerySyntax.Functions, _connector.GetSupportedFunctions());
        }

        [Fact]
        public void GetSupportedKeywords_DelegatesToBigQuerySyntax()
        {
            Assert.Equal(BigQuerySyntax.Additions, _connector.GetSupportedKeywords());
        }

        [Fact]
        public void GetExcludedKeywords_DelegatesToBigQuerySyntax()
        {
            Assert.Equal(BigQuerySyntax.Exclusions, _connector.GetExcludedKeywords());
        }

        [Fact]
        public void BuildParameters_InfersNativeBigQueryTypes()
        {
            var method = typeof(BigQueryDataSource).GetMethod("BuildParameters", BindingFlags.Static | BindingFlags.NonPublic)!;
            var parameters = ((IEnumerable<BigQueryParameter>)method.Invoke(null, new object?[]
            {
                new object?[]
                {
                    1,
                    2L,
                    true,
                    1.5d,
                    12.34m,
                    new DateTime(2026, 5, 25, 12, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 5, 25, 12, 0, 0, DateTimeKind.Unspecified),
                    "text",
                    null
                }
            })!).ToList();

            Assert.Equal(BigQueryDbType.Int64, parameters[0].Type);
            Assert.Equal(BigQueryDbType.Int64, parameters[1].Type);
            Assert.Equal(BigQueryDbType.Bool, parameters[2].Type);
            Assert.Equal(BigQueryDbType.Float64, parameters[3].Type);
            Assert.Equal(BigQueryDbType.Numeric, parameters[4].Type);
            Assert.Equal(BigQueryDbType.Timestamp, parameters[5].Type);
            Assert.Equal(BigQueryDbType.DateTime, parameters[6].Type);
            Assert.Equal(BigQueryDbType.String, parameters[7].Type);
            Assert.Equal(BigQueryDbType.String, parameters[8].Type);
            Assert.Null(parameters[8].Value);
        }
    }

    [Trait("Connector", "BIGQUERY")]
    [Trait("CertificationClass", "MetadataOnly")]
    public class BigQuerySyntaxTests
    {
        [Fact]
        public void Functions_ContainsBigQuerySpecificFunctions()
        {
            Assert.Contains("IF",               BigQuerySyntax.Functions);
            Assert.Contains("IFNULL",           BigQuerySyntax.Functions);
            Assert.Contains("SAFE_DIVIDE",      BigQuerySyntax.Functions);
            Assert.Contains("DATE_DIFF",        BigQuerySyntax.Functions);
            Assert.Contains("TIMESTAMP_TRUNC",  BigQuerySyntax.Functions);
            Assert.Contains("STRING_AGG",       BigQuerySyntax.Functions);
            Assert.Contains("COUNTIF",          BigQuerySyntax.Functions);
            Assert.Contains("APPROX_COUNT_DISTINCT", BigQuerySyntax.Functions);
            Assert.Contains("TO_JSON_STRING",   BigQuerySyntax.Functions);
            Assert.Contains("GENERATE_ARRAY",   BigQuerySyntax.Functions);
            Assert.Contains("SAFE_CAST",        BigQuerySyntax.Functions);
            Assert.Contains("FARM_FINGERPRINT", BigQuerySyntax.Functions);
        }

        [Fact]
        public void Functions_ContainsDateTimeFunctions()
        {
            Assert.Contains("CURRENT_DATE",      BigQuerySyntax.Functions);
            Assert.Contains("CURRENT_DATETIME",  BigQuerySyntax.Functions);
            Assert.Contains("CURRENT_TIMESTAMP", BigQuerySyntax.Functions);
            Assert.Contains("FORMAT_DATE",        BigQuerySyntax.Functions);
            Assert.Contains("PARSE_DATE",         BigQuerySyntax.Functions);
        }

        [Fact]
        public void Additions_ContainsBigQueryKeywords()
        {
            Assert.Contains("QUALIFY",   BigQuerySyntax.Additions);
            Assert.Contains("LIMIT",     BigQuerySyntax.Additions);
            Assert.Contains("UNNEST",    BigQuerySyntax.Additions);
            Assert.Contains("STRUCT",    BigQuerySyntax.Additions);
            Assert.Contains("SAFE_CAST", BigQuerySyntax.Additions);
        }

        [Fact]
        public void Exclusions_ContainsTSqlAndOracleKeywords()
        {
            Assert.Contains("TOP",     BigQuerySyntax.Exclusions);
            Assert.Contains("NOLOCK",  BigQuerySyntax.Exclusions);
            Assert.Contains("ISNULL",  BigQuerySyntax.Exclusions);
            Assert.Contains("GETDATE", BigQuerySyntax.Exclusions);
            Assert.Contains("ROWNUM",  BigQuerySyntax.Exclusions);
        }

        [Fact]
        public void Exclusions_NotPresentInAdditions()
        {
            foreach (var kw in BigQuerySyntax.Exclusions)
                Assert.DoesNotContain(kw, BigQuerySyntax.Additions);
        }

        [Fact]
        public void AllSets_AreCaseInsensitive()
        {
            Assert.Contains("if",       BigQuerySyntax.Functions);
            Assert.Contains("qualify",  BigQuerySyntax.Additions);
            Assert.Contains("top",      BigQuerySyntax.Exclusions);
        }

        [Fact]
        public void Functions_NotEmpty() => Assert.NotEmpty(BigQuerySyntax.Functions);

        [Fact]
        public void Additions_NotEmpty() => Assert.NotEmpty(BigQuerySyntax.Additions);

        [Fact]
        public void Exclusions_NotEmpty() => Assert.NotEmpty(BigQuerySyntax.Exclusions);
    }
}
