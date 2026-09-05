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
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, expected);
            return;
        }

        Assert.True(File.Exists(path),
            $"{path} is missing. Generate it with: "
            + "ETLSQL_UPDATE_BROWSER_CONTRACTS=1 dotnet test tests/ETL-SQL.Portal.Tests "
            + "--filter FullyQualifiedName~BrowserContractsGeneratorTests");

        var actual = File.ReadAllText(path).ReplaceLineEndings("\n");
        Assert.True(expected == actual,
            "types/etlsql-contracts.generated.d.ts is out of date with the C# DTOs and enums it is "
            + "generated from. The browser type gate is therefore checking a shape the server no "
            + "longer sends. Regenerate with: ETLSQL_UPDATE_BROWSER_CONTRACTS=1 dotnet test "
            + "tests/ETL-SQL.Portal.Tests --filter FullyQualifiedName~BrowserContractsGeneratorTests");
    }

    /// <summary>
    /// The kinds the palette offers are the kinds the host can write.
    /// </summary>
    /// <remarks>
    /// One direction of this — a chip naming a kind that does not exist — is now the type gate's
    /// job: <c>studio-pipeline-canvas.js</c> declares its palette as <c>PipelineTaskKind</c>, so a
    /// bad id fails <c>tsc</c> at the line that wrote it. The other direction cannot be a type
    /// check, because nothing makes an array exhaustive over a union, so it stays here.
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
}
