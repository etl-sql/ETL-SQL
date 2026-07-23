using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Connectors.MockDb;
using ETL_SQL.Data;
using Xunit;

namespace ETL_SQL.Tests.Core;

public class MockDbDimensionTests
{
    [Fact]
    public async Task SeedDataAsync_IncludesCommonDimensionTables()
    {
        var seeder = new MockDataSeeder();
        var tables = new Dictionary<string, DataTable>(StringComparer.OrdinalIgnoreCase);
        var rng = new Random(42);

        await seeder.SeedDataAsync(tables, rng);

        // Verify Numbers
        Assert.True(tables.ContainsKey("Numbers"));
        var numbers = tables["Numbers"];
        Assert.Equal(1000000, numbers.Rows.Count);
        Assert.Equal(1, Convert.ToInt32(numbers.Rows[0]["Number"]));
        Assert.Equal(1000000, Convert.ToInt32(numbers.Rows[999999]["Number"]));

        // Verify Dates
        Assert.True(tables.ContainsKey("Dates"));
        var dates = tables["Dates"];
        Assert.True(dates.Rows.Count >= 73000); // 200 years of dates (1900 to 2100)
        Assert.True(tables.ContainsKey("DimDate"));

        // Verify Times
        Assert.True(tables.ContainsKey("Times"));
        var times = tables["Times"];
        Assert.Equal(1440, times.Rows.Count); // 24 * 60 minutes
        Assert.True(tables.ContainsKey("DimTime"));

        // Verify Geography
        Assert.True(tables.ContainsKey("Geography"));
        var geo = tables["Geography"];
        Assert.True(geo.Rows.Count >= 5);

        // Verify Currencies
        Assert.True(tables.ContainsKey("Currencies"));
        var cur = tables["Currencies"];
        Assert.True(cur.Rows.Count >= 5);

        // Verify Flags
        Assert.True(tables.ContainsKey("Flags"));
        var flags = tables["Flags"];
        Assert.Equal(2, flags.Rows.Count);
    }
}
