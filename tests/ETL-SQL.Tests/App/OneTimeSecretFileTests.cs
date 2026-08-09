using ETL_SQL.App.Admin;

namespace ETL_SQL.Tests.App;

public sealed class OneTimeSecretFileTests
{
    [Fact]
    public async Task CommitWritesTheSecretWithoutEchoOrTrailingText()
    {
        var path = Path.Combine(Path.GetTempPath(), $"etl-sql-secret-{Guid.NewGuid():N}");
        try
        {
            await using (var output = OneTimeSecretFile.Reserve(path))
                await output.CommitAsync("sas_one_time_value", CancellationToken.None);

            Assert.Equal("sas_one_time_value", await File.ReadAllTextAsync(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task DisposeWithoutCommitRemovesOnlyItsEmptyReservation()
    {
        var path = Path.Combine(Path.GetTempPath(), $"etl-sql-secret-{Guid.NewGuid():N}");
        await using (OneTimeSecretFile.Reserve(path)) { }
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void ReserveNeverOverwritesAnExistingFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"etl-sql-secret-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllText(path, "keep-me");
            var error = Assert.Throws<AdminCliException>(() => OneTimeSecretFile.Reserve(path));
            Assert.Equal(AdminExitCode.ValidationError, error.Code);
            Assert.Equal("keep-me", File.ReadAllText(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
