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
    public void SecurityEvents_ValidatesHttpsCollectorAndFlattensTransportSettings()
    {
        var document = new OrganizationPolicyDocument
        {
            SecurityEvents = new SecurityEventPolicySection
            {
                CollectorEndpoint = "https://siem.example.test/etl-sql/events",
                BatchSize = 250,
                IntervalSeconds = 15,
                LeaseSeconds = 90
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

    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal);
}
