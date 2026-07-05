using ETL_SQL.Core;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Tests.Core;

/// <summary>
/// Proves a file connector's write boundary re-checks the resolved destination against enterprise
/// policy — so a connection created before policy tightened (or via a deferred/placeholder target)
/// cannot be used to write outside the approved roots.
/// </summary>
public sealed class FileConnectorWriteBoundaryTests : IDisposable
{
    private readonly string _root;
    private readonly string _outside;

    public FileConnectorWriteBoundaryTests()
    {
        _root = Path.GetFullPath($"fcw_root_{Guid.NewGuid():N}");
        _outside = Path.GetFullPath($"fcw_outside_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_outside);
    }

    public void Dispose()
    {
        EnterprisePolicyRuntime.SetCurrent(EffectiveEnterprisePolicy.Standalone);
        try { Directory.Delete(_root, recursive: true); } catch { }
        try { Directory.Delete(_outside, recursive: true); } catch { }
    }

    [Fact]
    public async Task Write_ToConnectionOutsideRootsAfterEnrolling_IsDeniedAtWriteBoundary()
    {
        var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
        var target = Path.Combine(_outside, "out.csv").Replace("\\", "\\\\");

        // Standalone: create the source rows and a CSV connection pointing outside the (soon)
        // approved roots — creation is unrestricted while unenrolled.
        EnterprisePolicyRuntime.SetCurrent(EffectiveEnterprisePolicy.Standalone);
        await Run(evaluator,
            $"CREATE TABLE #src (id INT); INSERT INTO #src VALUES (1); " +
            $"CREATE CONNECTION csv_out AS FLATFILE('{target}', HEADER = 'ON');");

        // Enroll with approved roots that exclude the connection's destination.
        EnterprisePolicyRuntime.SetCurrent(EnrolledWithRoot(_root));
        var denied = await Assert.ThrowsAnyAsync<Exception>(() =>
            Run(evaluator, "INSERT INTO csv_out SELECT * FROM #src;"));

        Assert.True(denied.ToString().Contains("approved filesystem roots", StringComparison.OrdinalIgnoreCase),
            "Actual denial: " + denied);
        Assert.False(File.Exists(Path.Combine(_outside, "out.csv")),
            "The write must be denied before the file is produced.");
    }

    private static async Task Run(Evaluator evaluator, string sql)
    {
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        await evaluator.Evaluate(script);
    }

    private static EffectiveEnterprisePolicy EnrolledWithRoot(string root)
    {
        var document = new OrganizationPolicyDocument
        {
            Filesystem = new FilesystemPolicySection { ApprovedRoots = [root] },
            // Isolate the filesystem write boundary from the mutation guardrails, which would
            // otherwise deny the un-transactioned INSERT first.
            MutationGuardrails = new MutationGuardrailPolicySection
            {
                RequireWhatIfForDestructiveStatements = false,
                RequireTransactionForMutations = false
            }
        };
        return new EffectiveEnterprisePolicy(true, true, "Live", "v1", "test",
            DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1),
            DateTimeOffset.UtcNow, document,
            EnterprisePolicyConfiguration.Flatten(document.ToPolicyValues()));
    }
}
