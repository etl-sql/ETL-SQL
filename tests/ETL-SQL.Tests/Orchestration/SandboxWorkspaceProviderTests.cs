using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Orchestrator.Execution;
using Xunit;

namespace ETL_SQL.Tests.Orchestration;

/// <summary>A fact about Unix file modes, which have no meaning on Windows.</summary>
public sealed class UnixFactAttribute : FactAttribute
{
    public UnixFactAttribute()
    {
        if (OperatingSystem.IsWindows())
            Skip = "Unix file modes do not apply on Windows; ACL inheritance scopes the workspace root.";
    }
}

public sealed class SandboxWorkspaceProviderTests
{
    [Fact]
    public async Task SuccessiveAssignmentsNeverReuseWritableState()
    {
        using var temp = new TempDirectory();
        var provider = CreateProvider(temp.Root);
        var identity = Identity("tenant-a", "run-1", "attempt-1");

        string firstRoot;
        await using (var first = await provider.AssignAsync(identity))
        {
            firstRoot = first.RootPath;
            await File.WriteAllTextAsync(Path.Combine(first.ScratchPath, "tenant-residue.txt"), "secret");
            Assert.True(File.Exists(Path.Combine(first.ScratchPath, "tenant-residue.txt")));
        }

        Assert.False(Directory.Exists(firstRoot));

        await using var second = await provider.AssignAsync(identity);
        Assert.NotEqual(firstRoot, second.RootPath);
        Assert.False(File.Exists(Path.Combine(second.ScratchPath, "tenant-residue.txt")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(second.ScratchPath));
    }

    [Fact]
    public async Task EqualRunAndAttemptIdsRemainTenantPartitioned()
    {
        using var temp = new TempDirectory();
        var provider = CreateProvider(temp.Root);

        await using var first = await provider.AssignAsync(Identity("tenant-a", "run-1", "attempt-1"));
        await using var second = await provider.AssignAsync(Identity("tenant-b", "run-1", "attempt-1"));

        Assert.NotEqual(first.RootPath, second.RootPath);
        Assert.Contains($"{Path.DirectorySeparatorChar}tenant-a{Path.DirectorySeparatorChar}", first.RootPath);
        Assert.Contains($"{Path.DirectorySeparatorChar}tenant-b{Path.DirectorySeparatorChar}", second.RootPath);
    }

    [UnixFact]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public async Task WritableLeavesAcceptAnUnprivilegedSandboxUidWhileEnclosingRootsStayPrivate()
    {
        using var temp = new TempDirectory();
        var provider = CreateProvider(temp.Root);

        await using var assignment = await provider.AssignAsync(Identity("tenant-a", "run-1", "attempt-1"));

        // The sandbox runs as a uid unrelated to this process, so it must be able to write its own
        // output and scratch, or the workload cannot produce anything.
        foreach (var writable in new[] { assignment.OutputPath, assignment.ScratchPath })
        {
            var mode = File.GetUnixFileMode(writable);
            Assert.True(mode.HasFlag(UnixFileMode.OtherWrite), $"{writable} must accept sandbox writes.");
            Assert.True(mode.HasFlag(UnixFileMode.OtherExecute), $"{writable} must be traversable.");
        }

        // That is only safe because nothing else on the host can reach them.
        var rootMode = File.GetUnixFileMode(assignment.RootPath);
        Assert.False(rootMode.HasFlag(UnixFileMode.OtherRead));
        Assert.False(rootMode.HasFlag(UnixFileMode.OtherWrite));
        Assert.False(rootMode.HasFlag(UnixFileMode.OtherExecute));
        Assert.False(rootMode.HasFlag(UnixFileMode.GroupWrite));
    }

    [Fact]
    public async Task ConcurrentProvidersOnOneHostRootNeverCollideOrTearDownEachOther()
    {
        using var temp = new TempDirectory();
        // Two worker processes sharing one host workspace root, given identical logical identifiers.
        var first = CreateProvider(temp.Root);
        var second = CreateProvider(temp.Root);
        var identity = Identity("tenant-a", "run-1", "attempt-1");

        var firstAssignment = await first.AssignAsync(identity);
        await using var secondAssignment = await second.AssignAsync(identity);
        await File.WriteAllTextAsync(
            Path.Combine(secondAssignment.ScratchPath, "in-flight.txt"), "still running");

        Assert.NotEqual(firstAssignment.AssignmentId, secondAssignment.AssignmentId);
        Assert.NotEqual(firstAssignment.RootPath, secondAssignment.RootPath);

        await firstAssignment.DestroyAsync();

        // One process finishing must not delete writable state a concurrent process still owns.
        Assert.False(Directory.Exists(firstAssignment.RootPath));
        Assert.True(File.Exists(Path.Combine(secondAssignment.ScratchPath, "in-flight.txt")));
    }

    [Fact]
    public async Task TeardownRemovesNestedAndReadOnlyResidue()
    {
        using var temp = new TempDirectory();
        var provider = CreateProvider(temp.Root);
        var assignment = await provider.AssignAsync(Identity("tenant-a", "run-1", "attempt-1"));
        var nested = Directory.CreateDirectory(Path.Combine(assignment.OutputPath, "nested"));
        var residue = Path.Combine(nested.FullName, "result.bin");
        await File.WriteAllBytesAsync(residue, [1, 2, 3]);
        File.SetAttributes(residue, FileAttributes.ReadOnly);

        await assignment.DestroyAsync();

        Assert.False(Directory.Exists(assignment.RootPath));
        await assignment.DestroyAsync();
    }

    [Fact]
    public async Task TamperedOwnershipMarkerFailsClosedWithoutDeletingWorkspace()
    {
        using var temp = new TempDirectory();
        var provider = CreateProvider(temp.Root);
        var assignment = await provider.AssignAsync(Identity("tenant-a", "run-1", "attempt-1"));
        var marker = Path.Combine(assignment.RootPath, ".etlsql-assignment.json");
        var originalMarker = await File.ReadAllTextAsync(marker);
        await File.WriteAllTextAsync(marker, "{}");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await assignment.DestroyAsync());
        Assert.True(Directory.Exists(assignment.RootPath));

        await File.WriteAllTextAsync(marker, originalMarker);
        await assignment.DestroyAsync();
        Assert.False(Directory.Exists(assignment.RootPath));
    }

    [Theory]
    [InlineData("../foreign")]
    [InlineData("nested/run")]
    [InlineData("..")]
    public void AssignmentIdentityRejectsPathShapingIdentifiers(string runId)
    {
        var tenant = TenantContext.FromVerifiedCredential("tenant-a");
        Assert.Throws<ArgumentException>(() => new SandboxAssignmentIdentity(tenant, runId, "attempt-1"));
    }

    [Fact]
    public void ProviderRequiresAbsoluteHostOwnedRoot()
    {
        Assert.Throws<ArgumentException>(() => new FileSystemSandboxWorkspaceProvider(
            new FileSystemSandboxWorkspaceOptions { RootPath = "relative-root" }));
    }

    private static FileSystemSandboxWorkspaceProvider CreateProvider(string root) =>
        new(new FileSystemSandboxWorkspaceOptions { RootPath = Path.Combine(root, "assignments") });

    private static SandboxAssignmentIdentity Identity(string tenantId, string runId, string attemptId) =>
        new(TenantContext.FromVerifiedCredential(tenantId), runId, attemptId);

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Root = Path.Combine(Path.GetTempPath(), $"etlsql-sandbox-workspace-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}
