using ETL_SQL.Core.Governance;
using Xunit;

namespace ETL_SQL.Tests.Core;

public class OrganizationPolicySchemaTests
{
    [Fact]
    public void ParseAndValidateJson_AcceptsCurrentSchemaVersion()
    {
        var root = OperatingSystem.IsWindows() ? "C:\\Data" : "/var/data";
        var json = $$"""
        {
          "schemaVersion": "1.0",
          "connectors": {
            "allowedTypes": [ "MSSQL", "POSTGRES", "FLATFILE" ]
          },
          "filesystem": {
            "approvedRoots": [ "{{Escape(root)}}" ]
          },
          "execution": {
            "allowedModes": [ "Batch", "Scheduled" ],
            "maxParallelDegree": 8,
            "maxFileOperationsPerScript": 250
          },
          "remoteExecution": {
            "mode": "AllowedHosts",
            "allowedHosts": [ "orchestrator.internal" ]
          },
          "mutationGuardrails": {
            "requireWhatIfForDestructiveStatements": true,
            "requireTransactionForMutations": true,
            "requireRemoteAuditForMutations": true
          }
        }
        """;

        var document = OrganizationPolicySchema.ParseAndValidateJson(json);
        var values = document.ToPolicyValues();

        Assert.Equal("1.0", document.SchemaVersion);
        Assert.Contains("MSSQL", document.Connectors.AllowedTypes);
        Assert.Equal(8, values["Security:MaxParallelDegree"]);
        Assert.Equal(true, values["Audit:RemoteDeliveryRequired"]);
    }

    [Fact]
    public void Validate_RejectsUnsupportedSchemaVersion()
    {
        var result = OrganizationPolicySchema.Validate(new OrganizationPolicyDocument
        {
            SchemaVersion = "2.0"
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Unsupported organization policy schema version"));
    }

    [Fact]
    public void Validate_RejectsDuplicateConnectorTypes()
    {
        var result = OrganizationPolicySchema.Validate(new OrganizationPolicyDocument
        {
            Connectors = new ConnectorPolicySection
            {
                AllowedTypes = new[] { "MSSQL", "mssql" }
            }
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("duplicated"));
    }

    [Fact]
    public void Validate_RejectsRelativeFilesystemRoots()
    {
        var result = OrganizationPolicySchema.Validate(new OrganizationPolicyDocument
        {
            Filesystem = new FilesystemPolicySection
            {
                ApprovedRoots = new[] { "relative\\data" }
            }
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("must be absolute"));
    }

    [Fact]
    public void Validate_RejectsInvalidWriteExtension()
    {
        var result = OrganizationPolicySchema.Validate(new OrganizationPolicyDocument
        {
            Filesystem = new FilesystemPolicySection
            {
                AllowedWriteExtensions = new[] { "csv", "bad/slash" }
            }
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("not a valid extension"));
    }

    [Fact]
    public void Validate_RejectsNegativeSpillCeiling()
    {
        var result = OrganizationPolicySchema.Validate(new OrganizationPolicyDocument
        {
            Execution = new ExecutionPolicySection { MaxSpillBytesPerScript = -1 }
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("max spill bytes"));
    }

    [Fact]
    public void Validate_RejectsBlankDockerImage()
    {
        var result = OrganizationPolicySchema.Validate(new OrganizationPolicyDocument
        {
            Process = new ProcessPolicySection { AllowedDockerImages = new[] { "postgres", "  " } }
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Docker images cannot contain blank"));
    }

    [Fact]
    public void ToPolicyValues_FlattensDockerImages()
    {
        var document = new OrganizationPolicyDocument
        {
            Process = new ProcessPolicySection { AllowedDockerImages = new[] { "postgres:15", "myreg.io/*" } }
        };

        var flat = EnterprisePolicyConfiguration.Flatten(document.ToPolicyValues());

        Assert.Equal("postgres:15", flat["Security:AllowedDockerImages:0"]);
        Assert.Equal("myreg.io/*", flat["Security:AllowedDockerImages:1"]);
    }

    [Fact]
    public void ToPolicyValues_FlattensWriteExtensionsAndSpillCeiling()
    {
        var document = new OrganizationPolicyDocument
        {
            Filesystem = new FilesystemPolicySection { AllowedWriteExtensions = new[] { ".csv", "txt" } },
            Execution = new ExecutionPolicySection { MaxSpillBytesPerScript = 1024 }
        };

        var flat = EnterprisePolicyConfiguration.Flatten(document.ToPolicyValues());

        Assert.Equal(".csv", flat["Security:AllowedWriteExtensions:0"]);
        Assert.Equal("txt", flat["Security:AllowedWriteExtensions:1"]);
        Assert.Equal("1024", flat["Security:MaxSpillBytesPerScript"]);
    }

    [Fact]
    public void Validate_RejectsAllowedHostsModeWithoutHosts()
    {
        var result = OrganizationPolicySchema.Validate(new OrganizationPolicyDocument
        {
            RemoteExecution = new RemoteExecutionPolicySection
            {
                Mode = RemoteExecutionMode.AllowedHosts
            }
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("requires at least one allowed host"));
    }

    [Fact]
    public void MetadataPolicy_AcceptsKnownScopesAndRejectsMalformedRequirements()
    {
        var valid = OrganizationPolicySchema.Validate(new OrganizationPolicyDocument
        {
            Metadata = new MetadataGovernancePolicySection
            {
                RequiredTags = [new OrganizationRequiredTagRule { Tag = "@classification", Scopes = ["DATASET", "COLUMN"] }]
            }
        });
        var invalid = OrganizationPolicySchema.Validate(new OrganizationPolicyDocument
        {
            Metadata = new MetadataGovernancePolicySection
            {
                RequiredTags =
                [
                    new OrganizationRequiredTagRule { Tag = "classification", Scopes = ["PIPELINE"] },
                    new OrganizationRequiredTagRule { Tag = "classification", Scopes = [] }
                ]
            }
        });

        Assert.True(valid.IsValid);
        Assert.False(invalid.IsValid);
        Assert.Contains(invalid.Errors, error => error.Contains("start with '@'", StringComparison.Ordinal));
        Assert.Contains(invalid.Errors, error => error.Contains("unsupported scope", StringComparison.Ordinal));
        Assert.Contains(invalid.Errors, error => error.Contains("at least one scope", StringComparison.Ordinal));
    }

    [Fact]
    public void SecurityEvents_ValidatesHttpsCollectorAndFlattensTransportSettings()
    {
        var document = new OrganizationPolicyDocument
        {
            SecurityEvents = new SecurityEventPolicySection
            {
                CollectorEndpoint = "https://siem.example.test/etl-sql/events",
                BatchSize = 250,
                IntervalSeconds = 15,
                LeaseSeconds = 90,
                MinimumForwardedSeverity = SecurityEventSeverity.Error,
                FailClosedMaxTerminalFailures = 2,
                FailClosedMaxOldestEventSeconds = 300,
                FailClosedMaxPendingEvents = 1_000,
                FailClosedMaxOutboxBytes = 64 * 1024 * 1024
            }
        };

        var result = OrganizationPolicySchema.Validate(document);
        var flat = EnterprisePolicyConfiguration.Flatten(document.ToPolicyValues());

        Assert.True(result.IsValid);
        Assert.Equal("https://siem.example.test/etl-sql/events",
            flat["SecurityEvents:CollectorEndpoint"]);
        Assert.Equal("250", flat["SecurityEvents:BatchSize"]);
        Assert.Equal("15", flat["SecurityEvents:IntervalSeconds"]);
        Assert.Equal("90", flat["SecurityEvents:LeaseSeconds"]);
        Assert.Equal("Error", flat["SecurityEvents:MinimumForwardedSeverity"]);
        Assert.Equal("2", flat["SecurityEvents:FailClosedMaxTerminalFailures"]);
        Assert.Equal("300", flat["SecurityEvents:FailClosedMaxOldestEventSeconds"]);
        Assert.Equal("1000", flat["SecurityEvents:FailClosedMaxPendingEvents"]);
        Assert.Equal("67108864", flat["SecurityEvents:FailClosedMaxOutboxBytes"]);
    }

    [Theory]
    [InlineData("http://siem.example.test/events", 100, 30, 120)]
    [InlineData("https://user:password@siem.example.test/events", 100, 30, 120)]
    [InlineData("https://siem.example.test/events", 0, 30, 120)]
    [InlineData("https://siem.example.test/events", 100, 0, 120)]
    [InlineData("https://siem.example.test/events", 100, 30, 1)]
    public void SecurityEvents_RejectsUnsafeOrInvalidSettings(
        string endpoint,
        int batchSize,
        int intervalSeconds,
        int leaseSeconds)
    {
        var result = OrganizationPolicySchema.Validate(new OrganizationPolicyDocument
        {
            SecurityEvents = new SecurityEventPolicySection
            {
                CollectorEndpoint = endpoint,
                BatchSize = batchSize,
                IntervalSeconds = intervalSeconds,
                LeaseSeconds = leaseSeconds
            }
        });

        Assert.False(result.IsValid);
    }

    [Fact]
    public void SecurityEvents_RejectsNonPositiveFailClosedThresholds()
    {
        var result = OrganizationPolicySchema.Validate(new OrganizationPolicyDocument
        {
            SecurityEvents = new SecurityEventPolicySection
            {
                FailClosedMaxTerminalFailures = 0,
                FailClosedMaxOldestEventSeconds = 0,
                FailClosedMaxPendingEvents = 0,
                FailClosedMaxOutboxBytes = 0
            }
        });

        Assert.False(result.IsValid);
        Assert.Equal(4, result.Errors.Count(error =>
            error.Contains("fail-closed", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void SaasOnboardingAuthorizationIsTypedValidatedAndFlattened()
    {
        var expires = DateTimeOffset.Parse("2026-08-11T00:00:00Z");
        var document = new OrganizationPolicyDocument
        {
            SaasOnboarding = new SaasOnboardingAuthorizationPolicySection
            {
                Enabled = true,
                TenantId = "tenant-alpha",
                OperatorPrincipal = "provisioner@platform.test",
                AuthorizationReference = "change-42",
                Reason = "create dedicated boundary",
                ExpiresUtc = expires
            }
        };

        var result = OrganizationPolicySchema.Validate(document);
        var flat = EnterprisePolicyConfiguration.Flatten(document.ToPolicyValues());

        Assert.True(result.IsValid);
        Assert.Equal("tenant-alpha", flat["SaaS:Onboarding:TenantId"]);
        Assert.Equal("change-42", flat["SaaS:Onboarding:AuthorizationReference"]);
        Assert.Equal(expires.ToString("O"), flat["SaaS:Onboarding:ExpiresUtc"]);
    }

    [Theory]
    [InlineData("", "operator", "change", "reason")]
    [InlineData("Tenant With Spaces", "operator", "change", "reason")]
    [InlineData("tenant-alpha", "", "change", "reason")]
    [InlineData("tenant-alpha", "operator", "", "reason")]
    [InlineData("tenant-alpha", "operator", "change", "")]
    public void SaasOnboardingAuthorizationRejectsIncompleteOrNoncanonicalAuthority(
        string tenant, string principal, string reference, string reason)
    {
        var result = OrganizationPolicySchema.Validate(new OrganizationPolicyDocument
        {
            SaasOnboarding = new SaasOnboardingAuthorizationPolicySection
            {
                Enabled = true,
                TenantId = tenant,
                OperatorPrincipal = principal,
                AuthorizationReference = reference,
                Reason = reason,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1)
            }
        });

        Assert.False(result.IsValid);
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal);
}
