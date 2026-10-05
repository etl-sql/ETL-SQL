namespace ETL_SQL.Tests.Integration.Connectors;

public sealed class PlatformFixtureRootTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImageBuildsUseTheNearestCheckoutIncludingNestedWorktrees(bool worktree)
    {
        var tempRoot = Path.GetFullPath(Path.GetTempPath());
        var fixture = Path.Combine(tempRoot, $"etlsql-platform-root-{Guid.NewGuid():N}");
        var checkout = Path.Combine(fixture, "candidate");
        var output = Path.Combine(checkout, "tests", "bin");
        Directory.CreateDirectory(Path.Combine(fixture, ".git"));
        Directory.CreateDirectory(output);
        try
        {
            var marker = Path.Combine(checkout, ".git");
            if (worktree) await File.WriteAllTextAsync(marker, "gitdir: ../.git/worktrees/candidate");
            else Directory.CreateDirectory(marker);

            Assert.Equal(checkout, PlatformFixtureHelpers.FindRepoRoot(output));
        }
        finally
        {
            var cleanup = Path.GetFullPath(fixture);
            if (!cleanup.StartsWith(tempRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.Ordinal) || File.GetAttributes(cleanup).HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidOperationException("Platform fixture cleanup escaped its owned temporary root.");
            Directory.Delete(cleanup, recursive: true);
        }
    }
}
