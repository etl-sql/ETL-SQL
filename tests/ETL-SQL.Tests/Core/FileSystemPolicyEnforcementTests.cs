using ETL_SQL.Core;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Tests.Core;

/// <summary>
/// End-to-end proof that script statements and functions routed through
/// <see cref="FileSystemPolicyAuthorizer"/> enforce enterprise approved filesystem roots,
/// and that unenrolled (standalone) execution is unrestricted by organization policy.
/// </summary>
public sealed class FileSystemPolicyEnforcementTests : IDisposable
{
    private readonly string _root;
    private readonly string _outside;

    public FileSystemPolicyEnforcementTests()
    {
        // CWD-relative so local safe-zone guardrails treat both alike; only the enterprise
        // policy distinguishes them (ApprovedRoots = [_root]).
        _root = Path.GetFullPath($"policy_enf_root_{Guid.NewGuid():N}");
        _outside = Path.GetFullPath($"policy_enf_outside_{Guid.NewGuid():N}");
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
    public async Task MergeFiles_EnterpriseRootsDenyOutsideDestination_AllowInside()
    {
        var src1 = Path.Combine(_root, "merge_a.csv");
        var src2 = Path.Combine(_root, "merge_b.csv");
        await File.WriteAllTextAsync(src1, "h\n1\n");
        await File.WriteAllTextAsync(src2, "h\n2\n");
        EnterprisePolicyRuntime.SetCurrent(EnrolledPolicy(_root));

        var deniedDest = Path.Combine(_outside, "merged.csv");
        var denied = await Assert.ThrowsAnyAsync<Exception>(() => ExecuteAsync(
            $"MERGE FILES '{Path.Combine(_root, "merge_*.csv")}' TO '{deniedDest}' WITH (HEADER = ON, OVERWRITE = ON);"));
        Assert.Contains("approved filesystem roots", denied.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(deniedDest));

        var allowedDest = Path.Combine(_root, "merged.csv");
        await ExecuteAsync(
            $"MERGE FILES '{Path.Combine(_root, "merge_*.csv")}' TO '{allowedDest}' WITH (HEADER = ON, OVERWRITE = ON);");
        Assert.True(File.Exists(allowedDest));
    }

    [Fact]
    public async Task SplitFile_EnterpriseRootsDenyOutsideDestinationDirectory()
    {
        var src = Path.Combine(_root, "split_src.txt");
        await File.WriteAllLinesAsync(src, ["line1", "line2"]);
        EnterprisePolicyRuntime.SetCurrent(EnrolledPolicy(_root));

        var deniedDir = Path.Combine(_outside, "chunks");
        var denied = await Assert.ThrowsAnyAsync<Exception>(() => ExecuteAsync(
            $"SPLIT FILE '{src}' TO '{deniedDir}' WITH (LIMIT_TYPE = 'ROWS', LIMIT_VALUE = 1, PREFIX = 'chunk_', OVERWRITE = ON);"));
        Assert.Contains("approved filesystem roots", denied.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(deniedDir));
    }

    [Fact]
    public async Task VerifyFileIntegrity_EnterpriseRootsDenyOutsideSource()
    {
        EnterprisePolicyRuntime.SetCurrent(EnrolledPolicy(_root));

        var denied = await Assert.ThrowsAnyAsync<Exception>(() => ExecuteAsync(
            $"VERIFY FILE INTEGRITY '{Path.Combine(_outside, "probe.txt")}' WITH (EXPECTED_HASH = 'abc', ALGORITHM = 'SHA256');"));
        Assert.Contains("approved filesystem roots", denied.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FileExistsFunction_EnterpriseRootsDenyOutsideProbe_AllowInside()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "present.txt"), "x");
        EnterprisePolicyRuntime.SetCurrent(EnrolledPolicy(_root));

        var denied = await Assert.ThrowsAnyAsync<Exception>(() => ExecuteAsync(
            $"DECLARE @probe BIT; SET @probe = FILE_EXISTS('{Path.Combine(_outside, "probe.txt")}');"));
        Assert.Contains("approved filesystem roots", denied.ToString(), StringComparison.OrdinalIgnoreCase);

        // Inside the approved root the probe executes normally.
        await ExecuteAsync(
            $"DECLARE @ok BIT; SET @ok = FILE_EXISTS('{Path.Combine(_root, "present.txt")}');");
    }

    [Fact]
    public async Task CreateConnection_EnterpriseRootsDenyFileRootOutside_AllowInside()
    {
        EnterprisePolicyRuntime.SetCurrent(EnrolledPolicy(_root));

        var denied = await Assert.ThrowsAnyAsync<Exception>(() => ExecuteAsync(
            $"CREATE CONNECTION policy_enf_bad AS DIRECTORY('{_outside}');"));
        Assert.Contains("approved filesystem roots", denied.ToString(), StringComparison.OrdinalIgnoreCase);

        await ExecuteAsync($"CREATE CONNECTION policy_enf_ok AS DIRECTORY('{_root}');");
    }

    [Fact]
    public async Task Standalone_Unenrolled_RemainsUnrestrictedByOrganizationPolicy()
    {
        var src = Path.Combine(_root, "merge_standalone.csv");
        await File.WriteAllTextAsync(src, "h\n1\n");
        EnterprisePolicyRuntime.SetCurrent(EffectiveEnterprisePolicy.Standalone);

        var dest = Path.Combine(_outside, "merged_standalone.csv");
        await ExecuteAsync(
            $"MERGE FILES '{src}' TO '{dest}' WITH (HEADER = ON, OVERWRITE = ON);");
        Assert.True(File.Exists(dest));
    }

    private static async Task ExecuteAsync(string sql)
    {
        var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        await evaluator.Evaluate(script);
    }

    private static EffectiveEnterprisePolicy EnrolledPolicy(string root)
    {
        var document = new OrganizationPolicyDocument
        {
            Filesystem = new FilesystemPolicySection { ApprovedRoots = [root] }
        };
        return new EffectiveEnterprisePolicy(true, true, "Live", "v1", "test",
            DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1),
            DateTimeOffset.UtcNow, document,
            EnterprisePolicyConfiguration.Flatten(document.ToPolicyValues()));
    }
}
