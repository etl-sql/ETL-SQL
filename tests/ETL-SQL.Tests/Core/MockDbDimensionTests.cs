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
        Assert.True(dates.Rows.Count >= 73000); // 200 years of dates (1900 to 2100) + sentinels
        Assert.True(tables.ContainsKey("DimDate"));

        // Verify Sentinels
        Assert.Equal(-1, Convert.ToInt32(dates.Rows[0]["DateKey"]));
        Assert.Equal("Unknown", dates.Rows[0]["MonthName"]);
        Assert.Equal(-2, Convert.ToInt32(dates.Rows[1]["DateKey"]));
        Assert.Equal("Not Applicable", dates.Rows[1]["MonthName"]);

        // Verify Ultimate Date Columns
        var sampleRow = dates.Rows[2]; // First real date: 1900-01-01
        Assert.Equal("1900-01-01", sampleRow["FullDateISO"]);
        Assert.Equal("1900-Q1", sampleRow["YearQuarter"]);
        Assert.Equal("Jan", sampleRow["MonthShortName"]);
        Assert.Equal(190001, Convert.ToInt32(sampleRow["YearMonth"]));
        Assert.Equal("Mon", sampleRow["DayShortName"]);
        Assert.Equal(1, Convert.ToInt32(sampleRow["DayOfYear"]));
        Assert.Equal(1, Convert.ToInt32(sampleRow["IsMonthStart"]));
        Assert.Equal(1, Convert.ToInt32(sampleRow["IsYearStart"]));
        Assert.Equal("FQ1", sampleRow["FiscalQuarter"]);

        // Verify Times
        Assert.True(tables.ContainsKey("Times"));
        var times = tables["Times"];
        Assert.Equal(1441, times.Rows.Count); // 1440 minutes + 1 sentinel (-1)
        Assert.True(tables.ContainsKey("DimTime"));

        // Verify Time Sentinel
        Assert.Equal(-1, Convert.ToInt32(times.Rows[0]["TimeKey"]));
        Assert.Equal("Unknown", times.Rows[0]["TimeOfDay"]);

        // Verify Ultimate Time Columns (Sample at 13:30:00 -> row index 1 + 13*60 + 30 = 811)
        var sampleTime = times.Rows[811]; // 13:30:00
        Assert.Equal(133000, Convert.ToInt32(sampleTime["TimeKey"]));
        Assert.Equal("13:30:00", sampleTime["FullTime24"]);
        Assert.Equal("01:30:00 PM", sampleTime["FullTime12"]);
        Assert.Equal("13:30", sampleTime["HourMinute24"]);
        Assert.Equal("01:30 PM", sampleTime["HourMinute12"]);
        Assert.Equal(810, Convert.ToInt32(sampleTime["MinuteOfDay"]));
        Assert.Equal(48600, Convert.ToInt32(sampleTime["SecondOfDay"]));
        Assert.Equal(27, Convert.ToInt32(sampleTime["HalfHour"]));
        Assert.Equal(54, Convert.ToInt32(sampleTime["QuarterHour"]));
        Assert.Equal("13:00 - 14:00", sampleTime["HourBand"]);
        Assert.Equal("13:30 - 14:00", sampleTime["HalfHourBand"]);
        Assert.Equal("13:30 - 13:45", sampleTime["QuarterHourBand"]);
        Assert.Equal(1, Convert.ToInt32(sampleTime["IsBusinessHours"]));
        Assert.Equal("Shift 1 (Day)", sampleTime["WorkShift"]);

        // Verify Geography
        Assert.True(tables.ContainsKey("Geography"));
        var geo = tables["Geography"];
        Assert.Equal(10, geo.Rows.Count); // 9 regions + 1 sentinel (-1)
        Assert.True(tables.ContainsKey("DimGeography"));
        Assert.True(tables.ContainsKey("Dim_Geography"));
        Assert.Equal(-1, Convert.ToInt32(geo.Rows[0]["GeoKey"]));
        Assert.Equal("USA", geo.Rows[1]["CountryCode3"]);
        Assert.Equal("Pacific", geo.Rows[1]["SubRegion"]);
        Assert.Equal(1, Convert.ToInt32(geo.Rows[1]["IsDomestic"]));

        // Verify Currencies
        Assert.True(tables.ContainsKey("Currencies"));
        var cur = tables["Currencies"];
        Assert.Equal(9, cur.Rows.Count); // 8 currencies + 1 sentinel (-1)
        Assert.True(tables.ContainsKey("DimCurrencies"));
        Assert.True(tables.ContainsKey("Dim_Currencies"));
        Assert.Equal(-1, Convert.ToInt32(cur.Rows[0]["CurrencyKey"]));
        Assert.Equal(840, Convert.ToInt32(cur.Rows[1]["NumericCode"]));
        Assert.Equal("$#,##0.00", cur.Rows[1]["StandardFormatPattern"]);
        Assert.Equal(1, Convert.ToInt32(cur.Rows[1]["IsBaseCurrency"]));

        // Verify Flags
        Assert.True(tables.ContainsKey("Flags"));
        var flags = tables["Flags"];
        Assert.Equal(3, flags.Rows.Count); // 0, 1, and sentinel (-1)
        Assert.True(tables.ContainsKey("DimFlags"));
        Assert.True(tables.ContainsKey("Dim_Flags"));
        Assert.Equal(-1, Convert.ToInt32(flags.Rows[0]["FlagKey"]));
        Assert.Equal("Yes", flags.Rows[2]["YesNo"]);
        Assert.Equal("Y", flags.Rows[2]["YesNoChar"]);
        Assert.Equal("Pass", flags.Rows[2]["PassFail"]);
        Assert.Equal("Enabled", flags.Rows[2]["EnabledDisabled"]);
    }
}
