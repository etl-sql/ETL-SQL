using System;
using System.Collections.Generic;
using Xunit;
using ETL_SQL.Connectors.Snowflake;

namespace ETL_SQL.Tests.Integration.Connectors
{
    public class SnowflakeConnectorTests
    {
        private readonly SnowflakeConnector _connector = new();

        // ── BuildConnectionString ─────────────────────────────────────────────

        [Fact]
        public void BuildConnectionString_UsernamePassword_ProducesCorrectParts()
        {
            var props = new Dictionary<string, string>
            {
                { "HOST",     "myorg-myaccount" },
                { "USERNAME", "alice" },
                { "PASSWORD", "s3cr3t" },
                { "DATABASE", "PROD_DB" },
                { "SCHEMA",   "PUBLIC" },
                { "WAREHOUSE","COMPUTE_WH" }
            };

            var cs = _connector.BuildConnectionString(props);

            Assert.Contains("account=myorg-myaccount", cs);
            Assert.Contains("user=alice", cs);
            Assert.Contains("password=s3cr3t", cs);
            Assert.Contains("db=PROD_DB", cs);
            Assert.Contains("schema=PUBLIC", cs);
            Assert.Contains("warehouse=COMPUTE_WH", cs);
            Assert.DoesNotContain("authenticator=snowflake_jwt", cs);
        }

        [Fact]
        public void BuildConnectionString_HostWithSuffix_NormalizesAccount()
        {
            var props = new Dictionary<string, string>
            {
                { "HOST",     "myorg-myaccount.snowflakecomputing.com" },
                { "USERNAME", "alice" },
                { "PASSWORD", "s3cr3t" }
            };

            var cs = _connector.BuildConnectionString(props);

            Assert.Contains("account=myorg-myaccount;", cs);
            Assert.DoesNotContain(".snowflakecomputing.com", cs);
        }

        [Fact]
        public void BuildConnectionString_PrivateKeyAuth_ProducesJwtParts()
        {
            var props = new Dictionary<string, string>
            {
                { "HOST",             "myorg-myaccount" },
                { "USERNAME",         "alice" },
                { "PRIVATE_KEY_FILE", "/etc/certs/rsa_key.p8" }
            };

            var cs = _connector.BuildConnectionString(props);

            Assert.Contains("authenticator=snowflake_jwt", cs);
            Assert.Contains("private_key_file=/etc/certs/rsa_key.p8", cs);
            Assert.DoesNotContain("password=", cs);
        }

        [Fact]
        public void BuildConnectionString_PrivateKeyTakesPrecedenceOverPassword()
        {
            var props = new Dictionary<string, string>
            {
                { "HOST",             "myorg-myaccount" },
                { "USERNAME",         "alice" },
                { "PASSWORD",         "ignored" },
                { "PRIVATE_KEY_FILE", "/etc/certs/rsa_key.p8" }
            };

            var cs = _connector.BuildConnectionString(props);

            Assert.Contains("authenticator=snowflake_jwt", cs);
            Assert.DoesNotContain("password=ignored", cs);
        }

        [Fact]
        public void BuildConnectionString_MinimalProperties_DoesNotThrow()
        {
            var props = new Dictionary<string, string>
            {
                { "HOST",     "acct" },
                { "USERNAME", "u" },
                { "PASSWORD", "p" }
            };
            var cs = _connector.BuildConnectionString(props);
            Assert.NotEmpty(cs);
        }

        // ── GetHost / GetHostStatic ───────────────────────────────────────────

        [Fact]
        public void GetHostStatic_FromOptions_ReturnsHostOption()
        {
            var options = new Dictionary<string, string> { { "HOST", "myorg-myaccount" } };
            var host = SnowflakeConnector.GetHostStatic("account=other;user=x;", options);
            Assert.Equal("myorg-myaccount", host);
        }

        [Fact]
        public void GetHostStatic_FromConnectionString_ParsesAccount()
        {
            var cs = "account=myorg-myaccount;user=alice;password=s3cr3t;";
            var host = SnowflakeConnector.GetHostStatic(cs);
            Assert.Equal("myorg-myaccount", host);
        }

        [Fact]
        public void GetHostStatic_NoAccount_ReturnsNull()
        {
            var host = SnowflakeConnector.GetHostStatic("user=alice;password=s3cr3t;");
            Assert.Null(host);
        }

        [Fact]
        public void GetHostStatic_OptionsPreferredOverConnectionString()
        {
            var options = new Dictionary<string, string> { { "HOST", "from-options" } };
            var host = SnowflakeConnector.GetHostStatic("account=from-cs;user=x;", options);
            Assert.Equal("from-options", host);
        }

        [Fact]
        public void GetHost_InstanceMethod_DelegatesToStatic()
        {
            var options = new Dictionary<string, string> { { "HOST", "myorg-myaccount" } };
            var host = _connector.GetHost("account=other;", options);
            Assert.Equal("myorg-myaccount", host);
        }

        // ── NormalizeAccount ─────────────────────────────────────────────────

        [Fact]
        public void NormalizeAccount_StripsSuffix()
        {
            var normalized = SnowflakeConnector.NormalizeAccount("myorg-myaccount.snowflakecomputing.com");
            Assert.Equal("myorg-myaccount", normalized);
        }

        [Fact]
        public void NormalizeAccount_PlainIdentifier_Unchanged()
        {
            var normalized = SnowflakeConnector.NormalizeAccount("myorg-myaccount");
            Assert.Equal("myorg-myaccount", normalized);
        }

        [Fact]
        public void NormalizeAccount_CaseInsensitiveSuffix()
        {
            var normalized = SnowflakeConnector.NormalizeAccount("ACCT.SnowflakeComputing.COM");
            Assert.Equal("ACCT", normalized);
        }

        // ── Connector metadata ────────────────────────────────────────────────

        [Fact]
        public void Name_IsSnowflake()
        {
            Assert.Equal("SNOWFLAKE", _connector.Name);
        }

        [Fact]
        public void GetSupportedOptions_ContainsAllExpectedKeys()
        {
            var opts = _connector.GetSupportedOptions();
            Assert.True(opts.ContainsKey("HOST"));
            Assert.True(opts.ContainsKey("DATABASE"));
            Assert.True(opts.ContainsKey("SCHEMA"));
            Assert.True(opts.ContainsKey("WAREHOUSE"));
            Assert.True(opts.ContainsKey("USERNAME"));
            Assert.True(opts.ContainsKey("PASSWORD"));
            Assert.True(opts.ContainsKey("PRIVATE_KEY_FILE"));
        }

        [Fact]
        public void GetSupportedFunctions_DelegatesToSnowflakeSyntax()
        {
            Assert.Equal(SnowflakeSyntax.Functions, _connector.GetSupportedFunctions());
        }

        [Fact]
        public void GetSupportedKeywords_DelegatesToSnowflakeSyntax()
        {
            Assert.Equal(SnowflakeSyntax.Additions, _connector.GetSupportedKeywords());
        }

        [Fact]
        public void GetExcludedKeywords_DelegatesToSnowflakeSyntax()
        {
            Assert.Equal(SnowflakeSyntax.Exclusions, _connector.GetExcludedKeywords());
        }
    }

    public class SnowflakeSyntaxTests
    {
        [Fact]
        public void Functions_ContainsSnowflakeSpecificFunctions()
        {
            Assert.Contains("IFF", SnowflakeSyntax.Functions);
            Assert.Contains("NVL", SnowflakeSyntax.Functions);
            Assert.Contains("ZEROIFNULL", SnowflakeSyntax.Functions);
            Assert.Contains("ARRAY_AGG", SnowflakeSyntax.Functions);
            Assert.Contains("OBJECT_CONSTRUCT", SnowflakeSyntax.Functions);
            Assert.Contains("PARSE_JSON", SnowflakeSyntax.Functions);
            Assert.Contains("FLATTEN", SnowflakeSyntax.Functions);
            Assert.Contains("QUALIFY", SnowflakeSyntax.Additions);
        }

        [Fact]
        public void Functions_ContainsCurrentVersionForSchemaIntrospection()
        {
            Assert.Contains("CURRENT_VERSION", SnowflakeSyntax.Functions);
            Assert.Contains("CURRENT_DATABASE", SnowflakeSyntax.Functions);
            Assert.Contains("CURRENT_SCHEMA", SnowflakeSyntax.Functions);
        }

        [Fact]
        public void Additions_ContainsSnowflakeOnlyKeywords()
        {
            Assert.Contains("QUALIFY", SnowflakeSyntax.Additions);
            Assert.Contains("ILIKE", SnowflakeSyntax.Additions);
            Assert.Contains("RLIKE", SnowflakeSyntax.Additions);
            Assert.Contains("TRY_CAST", SnowflakeSyntax.Additions);
            Assert.Contains("LATERAL", SnowflakeSyntax.Additions);
        }

        [Fact]
        public void Exclusions_ContainsTSqlOnlyKeywords()
        {
            Assert.Contains("TOP", SnowflakeSyntax.Exclusions);
            Assert.Contains("NOLOCK", SnowflakeSyntax.Exclusions);
        }

        [Fact]
        public void Exclusions_NotPresentInAdditions()
        {
            foreach (var kw in SnowflakeSyntax.Exclusions)
                Assert.DoesNotContain(kw, SnowflakeSyntax.Additions);
        }

        [Fact]
        public void AllSets_AreCaseInsensitive()
        {
            Assert.Contains("iff", SnowflakeSyntax.Functions);
            Assert.Contains("qualify", SnowflakeSyntax.Additions);
            Assert.Contains("top", SnowflakeSyntax.Exclusions);
        }

        [Fact]
        public void Functions_NotEmpty()
        {
            Assert.NotEmpty(SnowflakeSyntax.Functions);
        }

        [Fact]
        public void Additions_NotEmpty()
        {
            Assert.NotEmpty(SnowflakeSyntax.Additions);
        }
    }
}
