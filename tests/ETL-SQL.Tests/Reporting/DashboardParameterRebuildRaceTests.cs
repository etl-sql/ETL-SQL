using ETL_SQL.ReportHosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Reporting;

/// <summary>
/// A parameter posted while the session is rebuilding must take effect. The Portal saw this as a
/// click action that set <c>@Region</c> and got the report back with <c>@Region = 'All'</c>.
/// </summary>
public sealed class DashboardParameterRebuildRaceTests
{
    [Fact]
    public async Task AParameterSetDuringARebuildIsKept()
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"param_rebuild_race_{Guid.NewGuid():N}.rptsql");
        File.WriteAllText(scriptPath, """
            DECLARE @Region VARCHAR(20) = 'All';
            CREATE TABLE #sales (SaleID INT, Region VARCHAR(20));
            INSERT INTO #sales VALUES (1, 'North'), (2, 'South');
            CREATE VISUAL Orders AS TABLE (SOURCE = 'SELECT * FROM #sales WHERE @Region = ''All'' OR Region = @Region');
            CREATE PAGE Main AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = Orders));
            """);
        using var scopes = new GatedScopeFactory(DashboardTestHelper.CreateMockScopeFactory());
        try
        {
            var service = new DashboardService(scriptPath, scopes);
            var initial = await service.GetManifestAsync();
            Assert.Null(initial.Error);

            // Hold a rebuild at the point where it has released the old evaluator and not yet built
            // the new one, then post the parameter.
            scopes.Close();
            var rebuild = Task.Run(service.RebuildAsync);
            Assert.True(scopes.Entered.Wait(TimeSpan.FromSeconds(10)), "The rebuild never asked for a scope.");
            var update = service.SetParametersAsync([("@Region", "North")]);
            scopes.Open();
            await rebuild;
            var manifest = await update;

            Assert.Null(manifest.Error);
            Assert.Equal("North", manifest.Parameters["@Region"]);
            var orders = manifest.Visuals.Single(visual => visual.Name == "Orders");
            var region = orders.Columns.FindIndex(column => column.Equals("Region", StringComparison.OrdinalIgnoreCase));
            Assert.NotEmpty(orders.Rows);
            Assert.All(orders.Rows, row => Assert.Equal("North", row[region]?.ToString()));
        }
        finally
        {
            if (File.Exists(scriptPath)) File.Delete(scriptPath);
        }
    }

    /// <summary>Blocks <see cref="CreateScope"/> while closed, and says when a caller is waiting.</summary>
    private sealed class GatedScopeFactory(IServiceScopeFactory inner) : IServiceScopeFactory, IDisposable
    {
        private readonly ManualResetEventSlim _gate = new(initialState: true);

        public ManualResetEventSlim Entered { get; } = new();

        public void Close() => _gate.Reset();

        public void Open() => _gate.Set();

        public IServiceScope CreateScope()
        {
            if (!_gate.IsSet)
            {
                Entered.Set();
                _gate.Wait();
            }
            return inner.CreateScope();
        }

        public void Dispose()
        {
            _gate.Dispose();
            Entered.Dispose();
        }
    }
}
