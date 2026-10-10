namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Keeps <c>types/etlsql-contracts.generated.d.ts</c> the same shape as the C# it is generated from.
///
/// <para>The declarations are what the browser type gate checks the browser sources against, so a
/// DTO that changes without them changing means the gate is checking last release's shape. This is
/// the same ratchet the asset sync uses: the check fails and names the command that fixes it.</para>
/// </summary>
[Trait("Category", "Portal")]
public sealed class BrowserContractsGeneratorTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ETL-SQL.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string OutputPath() =>
        Path.Combine(RepoRoot(), "types", "etlsql-contracts.generated.d.ts");

    [Fact]
    public void GeneratedDeclarationsMatchTheCSharpTypes()
    {
        // LF regardless of platform: the file is read by tsc on every OS and a CRLF checkout would
        // otherwise present as drift on Windows alone.
        var expected = BrowserContractsGenerator.Generate().ReplaceLineEndings("\n");
        var path = OutputPath();

        if (Environment.GetEnvironmentVariable("ETLSQL_UPDATE_BROWSER_CONTRACTS") == "1")
        {
            path = BrowserContractsGeneratorStaging.OutputPath(RepoRoot(), "types/etlsql-contracts.generated.d.ts");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, expected);
            return;
        }

        Assert.True(File.Exists(path),
            $"{path} is missing. Generate it with: "
            + "node scripts/generate-browser-contracts.mjs");

        var actual = File.ReadAllText(path).ReplaceLineEndings("\n");
        Assert.True(expected == actual,
            "types/etlsql-contracts.generated.d.ts is out of date with the C# DTOs and enums it is "
            + "generated from. The browser type gate is therefore checking a shape the server no "
            + "longer sends. Regenerate with: node scripts/generate-browser-contracts.mjs");
    }

    /// <summary>
    /// The kinds the palette offers are the kinds the host can write.
    /// </summary>
    /// <remarks>
    /// The strict TypeScript palette coverage assertion checks both invalid and missing chip IDs.
    /// This test protects the generator's source vocabulary independently of its checked-in output.
    /// </remarks>
    [Fact]
    public void EveryKindTheHostCanWriteAppearsInTheGeneratedUnion()
    {
        var generated = BrowserContractsGenerator.Generate();
        var missing = Enum.GetNames<ETL_SQL.Analysis.Services.PipelineTaskKind>()
            .Where(name => !generated.Contains($"'{name.ToLowerInvariant()}'", StringComparison.Ordinal))
            .ToList();

        Assert.True(missing.Count == 0,
            "These kinds are missing from the generated PipelineTaskKind union, so the browser "
            + "sources would be checked against a vocabulary smaller than the host's: "
            + string.Join(", ", missing));
    }

    [Fact]
    public void NullableSerializedValuesIncludeNull()
    {
        var generated = BrowserContractsGenerator.Generate();
        Assert.Contains("refreshToken: string;", generated);
        Assert.Contains("snapshot?: ReportDependencySnapshotDto | null;", generated);
        Assert.Contains("reportId?: number | null;", generated);
        Assert.Contains("interface PagedResult<T>", generated);
        Assert.Contains("items: T[];", generated);
        Assert.DoesNotContain("sessionId:", generated);
        Assert.Contains("jobType: 0 | 1;", generated);
        Assert.Contains("id: string | null;", generated);
        var options = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        Assert.Equal("0", System.Text.Json.JsonSerializer.Serialize(ETL_SQL.Core.Data.JobTargetKind.Script, options));
        Assert.Equal("null", System.Text.Json.JsonSerializer.Serialize(ETL_SQL.Core.Data.JobId.None, options));
        Assert.Equal("\"job-id\"", System.Text.Json.JsonSerializer.Serialize(ETL_SQL.Core.Data.JobId.From("job-id"), options));
    }

    [Fact]
    public void NullableSnapshotCellsMatchNestedCollectionDeclarations()
    {
        var generated = BrowserContractsGenerator.Generate();
        Assert.Contains("sampleRows: Record<string, (string | null)[][]>;", generated);
        var response = new ETL_SQL.Portal.Models.DesignerSnapshotResponse("Report", DateTime.UtcNow,
            new Dictionary<string, IReadOnlyList<IReadOnlyList<string?>>>
            {
                ["Sales"] = [new string?[] { "North", null }]
            }, new Dictionary<string, IReadOnlyList<string>> { ["Sales"] = ["Region", "Revenue"] },
            new ETL_SQL.Portal.Services.DesignerSnapshotMetadata(false, false, 1, 1));
        var json = System.Text.Json.JsonSerializer.Serialize(response,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        using var document = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal(System.Text.Json.JsonValueKind.Null,
            document.RootElement.GetProperty("sampleRows").GetProperty("Sales")[0][1].ValueKind);
    }

    [Fact]
    public void TriageStatementClassRetainsItsSerializedMetricNames()
    {
        var generated = BrowserContractsGenerator.Generate();
        Assert.Contains("statements: StatementMetricsPayload[];", generated);
        Assert.Contains("duration_ms: number;", generated);
        Assert.Contains("queue_wait_ms: number;", generated);
        Assert.Contains("index_used?: string | null;", generated);
        var metrics = new ETL_SQL.Core.Profiling.StatementMetricsPayload
        {
            Statement = "SELECT ?",
            DurationMs = 1200,
            QueueWaitMs = 2,
            Failed = true
        };
        using var document = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(metrics,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
        Assert.Equal(1200, document.RootElement.GetProperty("duration_ms").GetInt64());
        Assert.Equal(2, document.RootElement.GetProperty("queue_wait_ms").GetInt64());
        Assert.True(document.RootElement.GetProperty("failed").GetBoolean());
        Assert.False(document.RootElement.TryGetProperty("durationMs", out _));
    }
}
