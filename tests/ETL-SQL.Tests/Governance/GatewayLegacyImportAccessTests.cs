using ETL_SQL.Core.Governance;
using ETL_SQL.Infrastructure.Sqlite;

namespace ETL_SQL.Tests.Governance;

public sealed class GatewayLegacyImportAccessTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gateway-import-access-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InaccessibleLegacyLedgerRefusesStartup(bool lockedFile)
    {
        using var store = new SqliteGatewayOutcomeStore(Path.Combine(_root, "outcomes.db"));
        var legacy = Path.Combine(_root, "outcomes.json");
        using var fileLock = lockedFile ? new FileStream(legacy, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None) : null;
        if (!lockedFile) Directory.CreateDirectory(legacy);
        await Assert.ThrowsAsync<GatewayProtocolException>(() => store.ImportLegacyAsync(legacy));
    }

    [Fact]
    public async Task MissingLegacyLedgerIsValidForANewGateway()
    {
        using var store = new SqliteGatewayOutcomeStore(Path.Combine(_root, "outcomes.db"));
        await store.ImportLegacyAsync(Path.Combine(_root, "outcomes.json"));
        Assert.Null(store.Find("tenant", "operation"));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
