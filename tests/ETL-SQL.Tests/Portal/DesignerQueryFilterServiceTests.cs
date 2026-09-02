using System;
using System.Collections.Generic;
using ETL_SQL.Core.Parser;
using ETL_SQL.Reporting.Authoring;
using Xunit;

namespace ETL_SQL.Tests.Portal;

public sealed class DesignerQueryFilterServiceTests
{
    private readonly DesignerQueryFilterService _service = new();

    [Fact]
    public void AppliesCategoricalFilterWithoutReplacingHandAuthoredWhere()
    {
        const string source = "SELECT Region, Amount FROM #sales WHERE IsActive = 1 ORDER BY Region";

        var filtered = _service.Apply(source,
        [
            new DesignerQueryFilter("region", "Region", "categorical", ["North", "O'Brien"])
        ]);

        Assert.Contains("WHERE IsActive = 1", filtered, StringComparison.Ordinal);
        Assert.Contains("Region IN ('North', 'O''Brien')", filtered, StringComparison.Ordinal);
        Assert.Contains("ORDER BY Region", filtered, StringComparison.Ordinal);
        AssertParses(filtered);
    }

    [Fact]
    public void ReplacesStudioOwnedPredicatesWhenControlChanges()
    {
        var first = _service.Apply("SELECT * FROM &sales",
        [
            new DesignerQueryFilter("amount", "Amount", "number", Maximum: "100")
        ]);

        var second = _service.Apply(first,
        [
            new DesignerQueryFilter("amount", "Amount", "number", Minimum: "25", Maximum: "75")
        ]);

        Assert.DoesNotContain("Amount <= 100", second, StringComparison.Ordinal);
        Assert.Contains("Amount BETWEEN 25 AND 75", second, StringComparison.Ordinal);
        Assert.Equal(1, Count(second, "ETL-SQL-STUDIO-FILTER"));
        AssertParses(second);
    }

    [Fact]
    public void BuildsDateRangeAndParameterPredicatesForInlineVisualSources()
    {
        var dated = _service.Apply("#sales",
        [
            new DesignerQueryFilter("order-date", "Order Date", "date", Minimum: "2026-08-01", Maximum: "2026-08-28")
        ]);
        var parameterized = _service.Apply(dated,
        [
            new DesignerQueryFilter(
                "order-date", "Order Date", "parameter",
                ParameterName: "@selected_order_date", ParameterOperator: "minimum")
        ]);

        Assert.StartsWith("(SELECT * FROM #sales", parameterized, StringComparison.Ordinal);
        Assert.DoesNotContain("2026-08-01", parameterized, StringComparison.Ordinal);
        Assert.Contains("[Order Date] >= @selected_order_date", parameterized, StringComparison.Ordinal);
        AssertParses(parameterized[1..^1]);
    }

    [Fact]
    public void BuildsDistinctSlicerOptionSourceFromFilteredQuery()
    {
        var filtered = _service.Apply("SELECT Region, Amount FROM #sales",
        [
            new DesignerQueryFilter("amount", "Amount", "number", Maximum: "100")
        ]);

        var options = _service.BuildCategoricalOptionSource(filtered, "Region");

        Assert.Contains("SELECT DISTINCT Region", options, StringComparison.Ordinal);
        Assert.DoesNotContain("ETL-SQL-STUDIO-FILTER", options, StringComparison.Ordinal);
        Assert.Contains("AS studio_options ORDER BY Region", options, StringComparison.Ordinal);
        AssertParses(options[1..^1]);
    }

    [Fact]
    public void DesignerParserPreservesManagedFilterMarkersForTheNextEdit()
    {
        var datasetQuery = _service.Apply("SELECT Region, Amount FROM #sales",
            [new DesignerQueryFilter("amount", "Amount", "number", Maximum: "100")],
            asVisualSource: false);
        var visualSource = _service.Apply("#sales",
            [new DesignerQueryFilter("region", "Region", "categorical", ["North"])],
            asVisualSource: true);
        var script = $"""
            CREATE DATASET &sales AS (
              {datasetQuery}
            );

            CREATE VISUAL SalesBar AS BAR (
              SOURCE = {visualSource},
              MAPPINGS (X = Region, Y = Amount)
            );

            CREATE PAGE [Main] AS DASHBOARD (
              LAYOUT (STRUCTURE = 'A', MAP ('A' = SalesBar))
            );
            """;

        var state = new DesignerScriptParsingService().Parse(script);

        Assert.Contains("ETL-SQL-STUDIO-FILTER", state.Datasets[0].Query, StringComparison.Ordinal);
        var visual = Assert.Single(state.Pages[0].Visuals);
        Assert.Contains("ETL-SQL-STUDIO-FILTER", visual.Options["inline_source"], StringComparison.Ordinal);
        Assert.StartsWith("(", visual.Options["inline_source"], StringComparison.Ordinal);
        Assert.EndsWith(")", visual.Options["inline_source"], StringComparison.Ordinal);
    }

    [Fact]
    public void KeepsExistingParameterBindingsWhenAnotherStaticFilterChanges()
    {
        var parameterized = _service.Apply("#sales",
        [
            new DesignerQueryFilter("region", "Region", "parameter",
                ParameterName: "@selected_region", ParameterOperator: "equals", AllValue: "All")
        ]);

        var withAmount = _service.Apply(parameterized,
        [
            new DesignerQueryFilter("amount", "Amount", "number", Maximum: "100")
        ]);

        Assert.Contains("@selected_region = 'All' OR Region = @selected_region", withAmount, StringComparison.Ordinal);
        Assert.Contains("Amount <= 100", withAmount, StringComparison.Ordinal);
        Assert.Equal(2, Count(withAmount, "ETL-SQL-STUDIO-FILTER"));
    }

    /// <summary>
    /// The operators a filter pane needs beyond a range, each with the SQL it must produce.
    ///
    /// <para>"Between" was the only question the pane could ask, so "after the 3rd", "not zero", and
    /// "never filled in" had no expression at all. Each operator is pinned to its predicate here
    /// because the pane sends a word and the meaning is decided in this service.</para>
    /// </summary>
    [Theory]
    [InlineData("minimum", "Amount >= 25")]
    [InlineData("maximum", "Amount <= 25")]
    [InlineData("greater", "Amount > 25")]
    [InlineData("less", "Amount < 25")]
    [InlineData("equals", "Amount = 25")]
    [InlineData("notequals", "Amount <> 25")]
    public void NumericComparisonOperators_ProduceTheirOwnPredicate(string op, string expected)
    {
        var filtered = _service.Apply("SELECT Amount FROM #sales",
        [
            new DesignerQueryFilter("amount", "Amount", "number", Minimum: "25", Operator: op)
        ]);

        Assert.Contains(expected, filtered, StringComparison.Ordinal);
        AssertParses(filtered);
    }

    [Theory]
    [InlineData("isnull", "Amount IS NULL")]
    [InlineData("notnull", "Amount IS NOT NULL")]
    public void NullChecks_NeedNoValue(string op, string expected)
    {
        var filtered = _service.Apply("SELECT Amount FROM #sales",
        [
            new DesignerQueryFilter("amount", "Amount", "number", Operator: op)
        ]);

        Assert.Contains(expected, filtered, StringComparison.Ordinal);
        AssertParses(filtered);
    }

    [Fact]
    public void DateComparison_KeepsTheIsoBoundContract()
    {
        var filtered = _service.Apply("#sales",
        [
            new DesignerQueryFilter("order-date", "Order Date", "date", Minimum: "2026-08-01", Operator: "greater")
        ]);

        Assert.Contains("[Order Date] > '2026-08-01'", filtered, StringComparison.Ordinal);
        // A bare reference comes back as a parenthesised visual source, which is a source and not a
        // statement, so the inner query is what has to parse.
        AssertParses(filtered[1..^1]);

        // A bound that is not an ISO date is still refused, whichever operator asks for it.
        Assert.Throws<ArgumentException>(() => _service.Apply("#sales",
        [
            new DesignerQueryFilter("order-date", "Order Date", "date", Minimum: "last tuesday", Operator: "greater")
        ]));
    }

    [Fact]
    public void AnOmittedOperator_StillMeansBetween()
    {
        // Every filter written before operators existed carries no operator at all.
        var filtered = _service.Apply("SELECT Amount FROM #sales",
        [
            new DesignerQueryFilter("amount", "Amount", "number", Minimum: "10", Maximum: "20")
        ]);

        Assert.Contains("Amount BETWEEN 10 AND 20", filtered, StringComparison.Ordinal);
    }

    [Fact]
    public void AComparisonWithNoValueYet_FiltersNothingRatherThanFailing()
    {
        // The author picks the operator before typing the number; that half-built state is not an
        // error, it is a filter that has nothing to say yet.
        var filtered = _service.Apply("SELECT Amount FROM #sales",
        [
            new DesignerQueryFilter("amount", "Amount", "number", Operator: "greater")
        ]);

        Assert.DoesNotContain("ETL-SQL-STUDIO-FILTER", filtered, StringComparison.Ordinal);
        AssertParses(filtered);
    }

    [Fact]
    public void AnUnknownOperator_IsRefusedRatherThanTreatedAsARange()
    {
        Assert.Throws<ArgumentException>(() => _service.Apply("SELECT Amount FROM #sales",
        [
            new DesignerQueryFilter("amount", "Amount", "number", Minimum: "25", Operator: "roughly")
        ]));
    }

    [Fact]
    public void AnOperatorChange_ReplacesTheStudioPredicateItOwns()
    {
        var range = _service.Apply("SELECT Amount FROM #sales",
        [
            new DesignerQueryFilter("amount", "Amount", "number", Minimum: "10", Maximum: "20")
        ]);
        var switched = _service.Apply(range,
        [
            new DesignerQueryFilter("amount", "Amount", "number", Operator: "isnull")
        ]);

        Assert.DoesNotContain("BETWEEN", switched, StringComparison.Ordinal);
        Assert.Contains("Amount IS NULL", switched, StringComparison.Ordinal);
        Assert.Equal(1, Count(switched, "ETL-SQL-STUDIO-FILTER"));
        AssertParses(switched);
    }

    private static void AssertParses(string query)
    {
        var script = new Parser(new Lexer(query).Tokenize(), query).Parse();
        Assert.DoesNotContain(script.Diagnostics, diagnostic =>
            diagnostic.Severity == ETL_SQL.Core.Common.DiagnosticSeverity.Error);
    }

    private static int Count(string value, string needle)
    {
        var count = 0;
        for (var index = 0; (index = value.IndexOf(needle, index, StringComparison.Ordinal)) >= 0; index += needle.Length)
            count++;
        return count;
    }
}
