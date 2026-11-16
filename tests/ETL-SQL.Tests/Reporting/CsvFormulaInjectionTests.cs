using System.Collections.Generic;
using ETL_SQL.Reporting;
using Xunit;

namespace ETL_SQL.Tests.Reporting;

public class CsvFormulaInjectionTests
{
    private static string Render(params List<string?>[] rows) => new CsvRenderer().Render(new ReportManifest
    {
        Visuals =
        [
            new VisualManifest
            {
                Name = "T",
                VisualType = "TABLE",
                Columns = ["=Header", "B"],
                Rows = [.. rows]
            }
        ]
    });

    [Fact]
    public void FormulaTriggersInTextAreNeutralized()
    {
        var csv = Render(["=HYPERLINK(\"https://evil.example\")", "@SUM(A1)"], ["+cmd|' /C calc'!A0", "-2+3"]);

        Assert.Contains("'=Header,B", csv);
        Assert.Contains("\"'=HYPERLINK(\"\"https://evil.example\"\")\",'@SUM(A1)", csv);
        Assert.Contains("'+cmd|' /C calc'!A0,'-2+3", csv);
    }

    [Fact]
    public void NumbersKeepTheirSign()
    {
        var csv = Render(["-5", "+1.5"], ["-0.25", "-1E-05"]);

        Assert.Contains("-5,+1.5", csv);
        Assert.Contains("-0.25,-1E-05", csv);
        Assert.DoesNotContain("'-", csv);
    }
}
