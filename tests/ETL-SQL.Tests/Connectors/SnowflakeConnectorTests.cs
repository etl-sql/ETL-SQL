using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Connectors.Snowflake;
using ETL_SQL.Services;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Connectors
{
    [Trait("Connector", "SNOWFLAKE")]
    [Trait("CertificationClass", "MetadataOnly")]
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
        public void BuildConnectionString_LocalEmulator_ProducesHostPortProtocolParts()
        {
            var props = new Dictionary<string, string>
            {
                { "HOST",     "127.0.0.1" },
                { "ACCOUNT",  "test" },
                { "PORT",     "8080" },
                { "PROTOCOL", "http" },
                { "USERNAME", "test" },
                { "PASSWORD", "test" },
                { "DATABASE", "TEST_DB" },
                { "SCHEMA",   "PUBLIC" }
            };

            var cs = _connector.BuildConnectionString(props);

            Assert.Contains("account=test", cs);
            Assert.Contains("host=127.0.0.1", cs);
            Assert.Contains("port=8080", cs);
            Assert.Contains("scheme=http", cs);
            Assert.Contains("user=test", cs);
            Assert.Contains("password=test", cs);
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
        public void GetHostStatic_HostKey_TakesPrecedenceOverAccount()
        {
            var host = SnowflakeConnector.GetHostStatic("account=test;host=127.0.0.1;port=8080;");
            Assert.Equal("127.0.0.1", host);
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

    [Trait("Connector", "SNOWFLAKE")]
    [Trait("CertificationClass", "MetadataOnly")]
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

    /// <summary>
    /// Mock-based verification of SnowflakeDataSource security and auth string construction.
    ///
    /// Full production sign-off for JWT key-pair auth and OAuth/ADC requires:
    ///   1. A real Snowflake trial account (free tier available at signup.snowflake.com).
    ///   2. A 2048-bit RSA key pair registered in the Snowflake user profile.
    ///   3. Run: CREATE CONNECTION MySnow TYPE SNOWFLAKE TARGET 'myorg-myaccount'
    ///              WITH (USERNAME='testuser', PRIVATE_KEY_FILE='rsa_key.p8');
    ///           SELECT CURRENT_VERSION() AT MySnow;
    ///   4. Verify: no SnowflakeDbException leaks through (should be ExecutionException).
    ///   5. Verify: PRIVATE_KEY_FILE path uses ResolvePath so path traversal is rejected.
    ///   6. For CI: set env vars SNOWFLAKE_ACCOUNT, SNOWFLAKE_USER, SNOWFLAKE_PRIVATE_KEY_PATH
    ///      and guard the tests with a Skip when vars are absent.
    /// </summary>
    [Trait("Connector", "SNOWFLAKE")]
    [Trait("CertificationClass", "MockedIntegration")]
    public class SnowflakeDataSourceTests
    {
        private static IExecutionContext MakePermissiveContext()
        {
            var security = new SecurityService(NullLogger.Instance);
            var ctx = new Mock<IExecutionContext>();
            ctx.Setup(c => c.SecurityService).Returns(security);
            ctx.Setup(c => c.Logger).Returns(NullLogger.Instance);
            return ctx.Object;
        }

        // ── Host allowlist enforcement ─────────────────────────────────────────

        [Fact]
        public void BlockedHost_ThrowsSecurityException()
        {
            var security = new SecurityService(NullLogger.Instance);
            security.IsTestMode = false;
            security.AllowedHosts.Clear();

            var ctx = new Mock<IExecutionContext>();
            ctx.Setup(c => c.SecurityService).Returns(security);
            ctx.Setup(c => c.Logger).Returns(NullLogger.Instance);

            // Constructor calls ValidateHost — should throw before any network attempt.
            Assert.Throws<SecurityException>(() =>
                new SnowflakeDataSource(ctx.Object,
                    "account=myorg-myaccount;user=alice;password=s3cr3t;",
                    null, null));
        }

        [Fact]
        public void PermissiveContext_ConstructorDoesNotThrow()
        {
            // Baseline: in test mode (or with * allowed), construction is always safe.
            var ds = new SnowflakeDataSource(MakePermissiveContext(),
                "account=myorg-myaccount;user=alice;password=s3cr3t;",
                null, null);
            Assert.NotNull(ds);
        }

        // ── Host normalisation in security check ──────────────────────────────

        [Fact]
        public void JwtAuth_HostOptionWithoutSuffix_AddsSuffixForValidation()
        {
            // When HOST option is a bare account identifier (no dots), the DataSource
            // appends .snowflakecomputing.com before passing it to ValidateHost.
            var security = new SecurityService(NullLogger.Instance);
            security.IsTestMode = false;
            security.AllowedHosts.Clear();
            security.AllowedHosts.Add("myorg-myaccount.snowflakecomputing.com");

            var ctx = new Mock<IExecutionContext>();
            ctx.Setup(c => c.SecurityService).Returns(security);
            ctx.Setup(c => c.Logger).Returns(NullLogger.Instance);

            // Should NOT throw — the allowlist contains the suffixed host.
            var ds = new SnowflakeDataSource(ctx.Object,
                "account=myorg-myaccount;user=alice;",
                null,
                new Dictionary<string, string> { ["HOST"] = "myorg-myaccount" });
            Assert.NotNull(ds);
        }

        [Fact]
        public void AccountOptionWithoutLocalEndpoint_DoesNotBypassSuffixValidation()
        {
            var security = new SecurityService(NullLogger.Instance);
            security.IsTestMode = false;
            security.AllowedHosts.Clear();
            security.AllowedHosts.Add("myorg-myaccount.snowflakecomputing.com");

            var ctx = new Mock<IExecutionContext>();
            ctx.Setup(c => c.SecurityService).Returns(security);
            ctx.Setup(c => c.Logger).Returns(NullLogger.Instance);

            var ds = new SnowflakeDataSource(ctx.Object,
                "account=myorg-myaccount;user=alice;",
                null,
                new Dictionary<string, string> { ["ACCOUNT"] = "myorg-myaccount" });
            Assert.NotNull(ds);
        }

        [Fact]
        public void JwtAuth_HostOptionWithSuffix_PassedThroughAsIs()
        {
            var security = new SecurityService(NullLogger.Instance);
            security.IsTestMode = false;
            security.AllowedHosts.Clear();
            security.AllowedHosts.Add("myorg-myaccount.snowflakecomputing.com");

            var ctx = new Mock<IExecutionContext>();
            ctx.Setup(c => c.SecurityService).Returns(security);
            ctx.Setup(c => c.Logger).Returns(NullLogger.Instance);

            var ds = new SnowflakeDataSource(ctx.Object,
                "account=myorg-myaccount;user=alice;",
                null,
                new Dictionary<string, string> { ["HOST"] = "myorg-myaccount.snowflakecomputing.com" });
            Assert.NotNull(ds);
        }

        // ── JWT connection string → auth properties ───────────────────────────

        [Fact]
        public void JwtConnectionString_ContainsSnowflakeJwtAuthenticator()
        {
            var connector = new SnowflakeConnector();
            var cs = connector.BuildConnectionString(new Dictionary<string, string>
            {
                ["HOST"]             = "myorg-myaccount",
                ["USERNAME"]         = "alice",
                ["PRIVATE_KEY_FILE"] = "/certs/rsa_key.p8"
            });

            Assert.Contains("authenticator=snowflake_jwt", cs, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("private_key_file=/certs/rsa_key.p8", cs, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("password=", cs, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void PasswordAuth_ConnectionString_NoJwtAuthenticator()
        {
            var connector = new SnowflakeConnector();
            var cs = connector.BuildConnectionString(new Dictionary<string, string>
            {
                ["HOST"]     = "myorg-myaccount",
                ["USERNAME"] = "alice",
                ["PASSWORD"] = "mysecret"
            });

            Assert.DoesNotContain("authenticator=snowflake_jwt", cs, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("password=mysecret", cs, StringComparison.OrdinalIgnoreCase);
        }

        // ── PRIVATE_KEY_FILE (.p8) zero-trust validation ──────────────────────

        // Builds a context whose ResolvePath echoes the input so the constructor's
        // ValidatePath/ValidateFileType run against the literal key path we pass in.
        private static IExecutionContext MakeKeyFileContext()
        {
            var security = new SecurityService(NullLogger.Instance) { IsTestMode = true };
            var ctx = new Mock<IExecutionContext>();
            ctx.Setup(c => c.SecurityService).Returns(security);
            ctx.Setup(c => c.Logger).Returns(NullLogger.Instance);
            ctx.Setup(c => c.ResolvePath(It.IsAny<string>())).Returns<string>(p => p);
            return ctx.Object;
        }

        [Fact]
        public void PrivateKeyFile_DocumentedP8Extension_IsAccepted()
        {
            // '.p8' is not in the global connector whitelist but is allowed for Snowflake key-pair auth.
            // A temp-dir path is an approved safe zone under test mode, so ValidatePath passes and the
            // only thing under test is the file-type override.
            var keyPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rsa_key_{Guid.NewGuid():N}.p8");

            // Construction succeeding proves ValidateFileType accepted the '.p8' key path
            // (a disallowed extension would have thrown a SecurityException here).
            var ds = new SnowflakeDataSource(MakeKeyFileContext(),
                "account=myorg-myaccount;user=alice;",
                null,
                new Dictionary<string, string> { ["PRIVATE_KEY_FILE"] = keyPath });

            Assert.NotNull(ds);
        }

        [Fact]
        public void PrivateKeyFile_FromConnectionString_P8Accepted()
        {
            var keyPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rsa_key_{Guid.NewGuid():N}.p8");

            var ds = new SnowflakeDataSource(MakeKeyFileContext(),
                $"account=myorg-myaccount;user=alice;private_key_file={keyPath};",
                null,
                null);

            Assert.NotNull(ds);
        }

        [Fact]
        public void PrivateKeyFile_BlockedExtension_StillRejected()
        {
            // The .p8 override must not weaken the blacklist: an executable extension is still denied.
            var keyPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rsa_key_{Guid.NewGuid():N}.exe");

            Assert.Throws<SecurityException>(() =>
                new SnowflakeDataSource(MakeKeyFileContext(),
                    "account=myorg-myaccount;user=alice;",
                    null,
                    new Dictionary<string, string> { ["PRIVATE_KEY_FILE"] = keyPath }));
        }

        [Fact]
        public void PrivateKeyFile_TraversalPath_StillRejected()
        {
            // Path traversal is blocked before file-type checks, even for a .p8 key.
            Assert.Throws<SecurityException>(() =>
                new SnowflakeDataSource(MakeKeyFileContext(),
                    "account=myorg-myaccount;user=alice;",
                    null,
                    new Dictionary<string, string> { ["PRIVATE_KEY_FILE"] = "../../etc/rsa_key.p8" }));
        }

        [Fact]
        public void PrivateKeyFile_SystemPath_StillRejected()
        {
            // A .p8 key under a protected system directory is still denied by ValidatePath.
            var sysKey = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? @"C:\Windows\System32\rsa_key.p8"
                : "/etc/rsa_key.p8";

            var security = new SecurityService(NullLogger.Instance) { IsTestMode = false, ProtectionMode = PathProtectionMode.Restricted };
            var ctx = new Mock<IExecutionContext>();
            ctx.Setup(c => c.SecurityService).Returns(security);
            ctx.Setup(c => c.Logger).Returns(NullLogger.Instance);
            ctx.Setup(c => c.ResolvePath(It.IsAny<string>())).Returns<string>(p => p);

            Assert.Throws<SecurityException>(() =>
                new SnowflakeDataSource(ctx.Object,
                    "account=myorg-myaccount;user=alice;",
                    null,
                    new Dictionary<string, string> { ["PRIVATE_KEY_FILE"] = sysKey }));
        }
    }
}
