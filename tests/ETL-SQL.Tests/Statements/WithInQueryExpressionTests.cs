using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Analysis.Lineage;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.Analysis.Linting.Rules;
using ETL_SQL.Analysis.Services;
using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Services;
using ETL_SQL.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Statements;

public class WithInQueryExpressionTests
{
    private static Script Parse(string sql)
    {
        var tokens = new Lexer(sql).Tokenize();
        return new Parser(tokens, sql).Parse();
    }

    private static Evaluator CreateEvaluator() =>
        DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

    [Theory]
    [InlineData("WITH a AS (WITH b(v) AS (SELECT 7) SELECT v FROM b) SELECT v FROM a;")]
    [InlineData("SELECT v FROM (WITH a(v) AS (SELECT 11) SELECT v FROM a) sub;")]
    [InlineData("SELECT l.id FROM (SELECT 1 AS id) l JOIN (WITH r(id) AS (SELECT 1) SELECT id FROM r) r ON l.id = r.id;")]
    [InlineData("SELECT (WITH a(v) AS (SELECT 42) SELECT v FROM a) AS ans;")]
    [InlineData("SELECT 1 WHERE 1 IN (WITH a(v) AS (SELECT 1 UNION ALL SELECT 2) SELECT v FROM a);")]
    [InlineData("SELECT 1 WHERE EXISTS (WITH a(v) AS (SELECT 1) SELECT v FROM a);")]
    [InlineData("(WITH a AS (SELECT 1 AS v) SELECT v FROM a) UNION (WITH b AS (SELECT 2 AS v) SELECT v FROM b);")]
    [InlineData("(WITH a AS (SELECT 1 AS v) SELECT v FROM a) UNION SELECT 2 AS v;")]
    [InlineData("SELECT 1 AS v UNION (WITH b AS (SELECT 2 AS v) SELECT v FROM b);")]
    [InlineData("WITH RECURSIVE r(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM r WHERE n < 3) SELECT n FROM r UNION ALL SELECT 99;")]
    [InlineData("INSERT INTO #out (WITH a AS (SELECT 1 AS v) SELECT v FROM a);")]
    [InlineData("CREATE VIEW v_nested AS (WITH a AS (SELECT 1 AS v) SELECT v FROM a) UNION (WITH b AS (SELECT 2 AS v) SELECT v FROM b);")]
    [InlineData("SELECT ((SELECT 1) + 2) AS v;")]
    [InlineData("SELECT 1 WHERE 2 IN ((SELECT 1), (SELECT 2));")]
    public void ParseAndSerialize_RoundTripsAllWithQueryPositions(string sql)
    {
        var script1 = Parse(sql);
        var stmt1 = Assert.Single(script1.Statements);

        var formatted = stmt1.ToSql();
        var script2 = Parse(formatted);
        var stmt2 = Assert.Single(script2.Statements);

        Assert.Equal(formatted, stmt2.ToSql());
    }

    [Fact]
    public async Task Execute_WithInsideAllQueryPositions_ReturnsExpectedResults()
    {
        var ev = CreateEvaluator();

        // 1. CTE body
        var cteBody = await ev.ExecuteQuery(Parse(
            "WITH outer_cte AS (WITH inner_cte(x) AS (SELECT 5) SELECT x * 2 AS y FROM inner_cte) SELECT y FROM outer_cte;"
        ).Statements[0]).FirstAsync();
        Assert.Equal(10m, Convert.ToDecimal(cteBody.Rows[0]["y"]));

        // 2. Derived table and JOIN subquery
        var derived = await ev.ExecuteQuery(Parse(
            """
            SELECT a.x + b.y AS total
            FROM (WITH c1 AS (SELECT 10 AS x) SELECT x FROM c1) a
            JOIN (WITH c2 AS (SELECT 20 AS y) SELECT y FROM c2) b ON 1 = 1;
            """
        ).Statements[0]).FirstAsync();
        Assert.Equal(30m, Convert.ToDecimal(derived.Rows[0]["total"]));

        // 3. Scalar subquery
        var scalar = await ev.ExecuteQuery(Parse(
            "SELECT (WITH c AS (SELECT 42 AS v) SELECT v FROM c) AS ans;"
        ).Statements[0]).FirstAsync();
        Assert.Equal(42m, Convert.ToDecimal(scalar.Rows[0]["ans"]));

        // 4. IN and NOT IN subqueries (including set operations)
        var inRes = await ev.ExecuteQuery(Parse(
            """
            SELECT n
            FROM (VALUES (1), (2), (3), (4)) t(n)
            WHERE n IN (WITH a AS (SELECT 1 AS v UNION ALL SELECT 3 AS v) SELECT v FROM a)
              AND n NOT IN ((WITH b AS (SELECT 3 AS v) SELECT v FROM b) UNION (SELECT 4 AS v))
            ORDER BY n;
            """
        ).Statements[0]).FirstAsync();
        Assert.Single(inRes.Rows);
        Assert.Equal(1m, Convert.ToDecimal(inRes.Rows[0]["n"]));

        // 5. EXISTS and NOT EXISTS (including set operations)
        var existsRes = await ev.ExecuteQuery(Parse(
            """
            SELECT
                CASE WHEN EXISTS ((WITH a AS (SELECT 1 AS x WHERE 1 = 0) SELECT x FROM a) UNION (WITH b AS (SELECT 9 AS x) SELECT x FROM b)) THEN 1 ELSE 0 END AS hit,
                CASE WHEN NOT EXISTS (WITH c AS (SELECT 1 AS x WHERE 1 = 0) SELECT x FROM c) THEN 1 ELSE 0 END AS miss;
            """
        ).Statements[0]).FirstAsync();
        Assert.Equal(1m, Convert.ToDecimal(existsRes.Rows[0]["hit"]));
        Assert.Equal(1m, Convert.ToDecimal(existsRes.Rows[0]["miss"]));

        // 6. Parenthesized set-operation branches
        var unionRes = await ev.ExecuteQuery(Parse(
            "(WITH a AS (SELECT 1 AS v) SELECT v FROM a) UNION (WITH b AS (SELECT 2 AS v) SELECT v FROM b) ORDER BY v;"
        ).Statements[0]).FirstAsync();
        Assert.Equal(2, unionRes.Rows.Count);
        Assert.Equal(1m, Convert.ToDecimal(unionRes.Rows[0]["v"]));
        Assert.Equal(2m, Convert.ToDecimal(unionRes.Rows[1]["v"]));
    }

    [Fact]
    public async Task Execute_NestedWith_ShadowsOuterCteAndTempTableOnlyInsideSubquery()
    {
        var ev = CreateEvaluator();
        await ev.Evaluate(Parse("CREATE TABLE #t (val INT); INSERT INTO #t VALUES (100);"));

        var res = await ev.ExecuteQuery(Parse(
            """
            WITH shared AS (SELECT 10 AS v)
            SELECT
                (WITH shared AS (SELECT 99 AS v) SELECT v FROM shared) AS inner_shared,
                (SELECT v FROM shared) AS outer_shared,
                (WITH #t AS (SELECT 777 AS val) SELECT val FROM #t) AS inner_temp,
                (SELECT val FROM #t) AS outer_temp;
            """
        ).Statements[0]).FirstAsync();

        Assert.Equal(99m, Convert.ToDecimal(res.Rows[0]["inner_shared"]));
        Assert.Equal(10m, Convert.ToDecimal(res.Rows[0]["outer_shared"]));
        Assert.Equal(777m, Convert.ToDecimal(res.Rows[0]["inner_temp"]));
        Assert.Equal(100m, Convert.ToDecimal(res.Rows[0]["outer_temp"]));

        var after = await ev.ExecuteQuery(Parse("SELECT val FROM #t;").Statements[0]).FirstAsync();
        Assert.Equal(100m, Convert.ToDecimal(after.Rows[0]["val"]));
    }

    [Fact]
    public async Task Execute_NestedWithRecursive_WorksInDerivedAndSubqueryPositions()
    {
        var ev = CreateEvaluator();

        var res = await ev.ExecuteQuery(Parse(
            """
            SELECT
                (WITH RECURSIVE r(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM r WHERE n < 4) SELECT SUM(n) FROM r) AS scalar_sum,
                (SELECT COUNT(*) FROM (WITH RECURSIVE r(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM r WHERE n < 3) SELECT n FROM r) d) AS derived_cnt,
                (WITH RECURSIVE r(n) AS (WITH seed AS (SELECT 1 AS s) SELECT s FROM seed UNION ALL SELECT n + 1 FROM r WHERE n < 4) SELECT SUM(n) FROM r) AS seeded_sum;
            """
        ).Statements[0]).FirstAsync();

        Assert.Equal(10m, Convert.ToDecimal(res.Rows[0]["scalar_sum"]));
        Assert.Equal(3m, Convert.ToDecimal(res.Rows[0]["derived_cnt"]));
        Assert.Equal(10m, Convert.ToDecimal(res.Rows[0]["seeded_sum"]));
    }

    [Fact]
    public async Task Execute_CorrelatedSubqueryWithNestedWith_DoesNotReuseStaleCache()
    {
        var ev = CreateEvaluator();
        await ev.Evaluate(Parse("CREATE TABLE #items (id INT, mult INT); INSERT INTO #items VALUES (1, 10), (2, 20), (3, 30);"));

        var res = await ev.ExecuteQuery(Parse(
            """
            SELECT
                t.id,
                (WITH c AS (SELECT t.mult * 2 AS scaled) SELECT scaled FROM c) AS scaled,
                CASE WHEN EXISTS (WITH k AS (SELECT t.id AS id WHERE t.mult >= 20) SELECT id FROM k) THEN 1 ELSE 0 END AS big
            FROM #items t
            ORDER BY t.id;
            """
        ).Statements[0]).FirstAsync();

        Assert.Equal(3, res.Rows.Count);
        Assert.Equal(20m, Convert.ToDecimal(res.Rows[0]["scaled"]));
        Assert.Equal(0m, Convert.ToDecimal(res.Rows[0]["big"]));
        Assert.Equal(40m, Convert.ToDecimal(res.Rows[1]["scaled"]));
        Assert.Equal(1m, Convert.ToDecimal(res.Rows[1]["big"]));
        Assert.Equal(60m, Convert.ToDecimal(res.Rows[2]["scaled"]));
        Assert.Equal(1m, Convert.ToDecimal(res.Rows[2]["big"]));
    }

    [Fact]
    public async Task Execute_NestedScalarArithmeticAndParenthesizedListIn_DoNotRegress()
    {
        var ev = CreateEvaluator();

        var res = await ev.ExecuteQuery(Parse(
            "SELECT ((SELECT 1) + 2) AS sum_val, CASE WHEN 2 IN ((SELECT 1), (SELECT 2)) THEN 1 ELSE 0 END AS in_hit;"
        ).Statements[0]).FirstAsync();

        Assert.Equal(3m, res.Rows[0]["sum_val"]);
        Assert.Equal(1m, res.Rows[0]["in_hit"]);
    }

    [Fact]
    public async Task Tooling_LinterLineageDataModelAndCompletion_HandleNestedCtes()
    {
        const string sql = """
            SELECT sub.id
            FROM (
                WITH inner_cte(id, score) AS (SELECT 1 AS id, 10 AS score)
                SELECT id, score FROM inner_cte
            ) sub
            WHERE EXISTS (
                WITH exists_cte(flag) AS (SELECT 1 AS flag)
                SELECT flag FROM exists_cte
            );
            """;

        var script = Parse(sql);

        // 1. Linter discovery exposes nested CTEs and their explicit columns in ScriptMetadataOverlay
        var lintContext = new DefaultLintContext();
        var linter = new Linter();
        await linter.AnalyzeAsync(script, lintContext);
        var tables = (await lintContext.Metadata.GetTablesAsync("DEFAULT")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("inner_cte", tables);
        Assert.Contains("exists_cte", tables);
        var innerCols = (await lintContext.Metadata.GetColumnsAsync("DEFAULT", "inner_cte")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("id", innerCols);
        Assert.Contains("score", innerCols);

        // 2. ScriptDataModelService includes nested CTE entities without conjuring phantom table entities
        var model = new ScriptDataModelService().Project(sql);
        Assert.True(model.Parsed);
        Assert.Contains(model.Entities, e => e.Kind == "cte" && e.Name.Equals("inner_cte", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(model.Entities, e => e.Kind == "cte" && e.Name.Equals("exists_cte", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(model.Entities, e => e.Kind == "table" && e.Name.Equals("inner_cte", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(model.Entities, e => e.Kind == "table" && e.Name.Equals("exists_cte", StringComparison.OrdinalIgnoreCase));

        var nestedCteModel = new ScriptDataModelService().Project(
            """
            WITH outer_cte AS (
                WITH inner_cte AS (SELECT id FROM #raw)
                SELECT id FROM inner_cte
            )
            SELECT id INTO #out FROM outer_cte;
            """
        );
        Assert.Contains(nestedCteModel.Entities, e => e.Kind == "cte" && e.Name.Equals("inner_cte", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(nestedCteModel.Entities, e => e.Kind == "cte" && e.Name.Equals("outer_cte", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(nestedCteModel.Entities, e => e.Kind == "table" && e.Name.Equals("inner_cte", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(nestedCteModel.Relationships, d => d.From == "cte:inner_cte" && d.To == "cte:outer_cte");

        // 3. LineageAnalyzer records CTE lineage inside nested scopes and propagates @pii through derived-table CTEs
        var tracker = new LineageTracker(NullLogger.Instance);
        var lineage = new LineageAnalyzer(tracker);
        lineage.Analyze(script);
        var lineageEntries = tracker.GetFullLineage().ToList();
        Assert.Contains(lineageEntries, r => r.Operation == "CTE" && r.TargetTable.Equals("inner_cte", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lineageEntries, r => r.Operation == "CTE" && r.TargetTable.Equals("exists_cte", StringComparison.OrdinalIgnoreCase));

        var piiTracker = new LineageTracker(NullLogger.Instance);
        new LineageAnalyzer(piiTracker).Analyze(Parse(
            """
            SELECT ssn INTO #out
            FROM (
                WITH staged AS (SELECT ssn /* @pii: true */ FROM #raw)
                SELECT ssn FROM staged
            ) sub;
            """
        ));
        var outMeta = piiTracker.GetColumnMetadata("#out", "ssn");
        Assert.NotNull(outMeta);
        Assert.Equal("true", outMeta!["pii"]);

        // 4. QuarantineBoundaryRule flags QUARANTINE hidden inside EXISTS (WITH ...)
        var quarantineLinter = new Linter();
        quarantineLinter.AddRule(new QuarantineBoundaryRule());
        var qResults = await quarantineLinter.AnalyzeAsync(Parse(
            """
            SELECT 1
            WHERE EXISTS (
                WITH c AS (SELECT x EXPECT NOT NULL ON FAILURE QUARANTINE FROM #src)
                SELECT x FROM c
            );
            """
        ), new DefaultLintContext());
        Assert.Contains(qResults, r => r.RuleName == "QuarantineBoundary");

        // 5. UnusedConnectionRule detects connections referenced only inside nested WITH expressions
        var unusedLinter = new Linter();
        unusedLinter.AddRule(new UnusedConnectionRule());
        var uResults = await unusedLinter.AnalyzeAsync(Parse(
            """
            CREATE CONNECTION remote_conn AS SQLITE('memory');
            SELECT 1
            FROM #local
            WHERE EXISTS (
                WITH c AS (SELECT id FROM remote_conn.items)
                SELECT id FROM c
            );
            """
        ), new DefaultLintContext());
        Assert.DoesNotContain(uResults, r => r.RuleName == "UnusedConnection");

        // 6. GrammarLanguageService transitions cleanly inside FROM (WITH ...)
        var connectors = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<ETL_SQL.Data.IConnectorRegistry>();
        var langService = new GrammarLanguageService(new MetadataManager(NullLogger.Instance, connectors));
        var suggestions = await langService.GetSuggestionsAsync(new SuggestionContext
        {
            Prefix = "",
            ScriptBefore = "SELECT * FROM (",
            FullScript = "SELECT * FROM (",
            DocumentUri = "test://nested-with"
        });
        Assert.Contains(suggestions, s => s.Text.Equals("WITH", StringComparison.OrdinalIgnoreCase));

        var recursiveSuggestions = await langService.GetSuggestionsAsync(new SuggestionContext
        {
            Prefix = "",
            ScriptBefore = "SELECT * FROM (WITH ",
            FullScript = "SELECT * FROM (WITH ",
            DocumentUri = "test://nested-with"
        });
        Assert.Contains(recursiveSuggestions, s => s.Text.Equals("RECURSIVE", StringComparison.OrdinalIgnoreCase));

        // 7. GrammarLanguageService offers WITH/SELECT inside WHERE EXISTS (, WHERE IN (, and scalar SELECT (
        foreach (var prefix in new[] { "SELECT * FROM #t WHERE EXISTS (", "SELECT * FROM #t WHERE id IN (", "SELECT (" })
        {
            var exprSuggestions = await langService.GetSuggestionsAsync(new SuggestionContext
            {
                Prefix = "",
                ScriptBefore = prefix,
                FullScript = prefix,
                DocumentUri = "test://nested-with-expr"
            });
            Assert.Contains(exprSuggestions, s => s.Text.Equals("WITH", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(exprSuggestions, s => s.Text.Equals("SELECT", StringComparison.OrdinalIgnoreCase));
        }

        // 8. AvoidSelectStarRule and AggregateWithoutGroupByRule inspect nested CTEs and subquery expressions
        var starLinter = new Linter();
        starLinter.AddRule(new AvoidSelectStarRule());
        var starResults = await starLinter.AnalyzeAsync(Parse(
            """
            SELECT 1
            FROM #local
            WHERE EXISTS (
                WITH inner_star AS (SELECT * FROM #src)
                SELECT id FROM inner_star
            );
            """
        ), new DefaultLintContext());
        Assert.Contains(starResults, r => r.RuleName == "AvoidSelectStar");

        var aggLinter = new Linter();
        aggLinter.AddRule(new AggregateWithoutGroupByRule());
        var aggResults = await aggLinter.AnalyzeAsync(Parse(
            """
            WITH bad_agg AS (SELECT dept, COUNT(*) FROM #src)
            SELECT dept FROM bad_agg;
            """
        ), new DefaultLintContext());
        Assert.Contains(aggResults, r => r.RuleName == "AggregateWithoutGroupBy");

        // 9. GrammarLanguageService offers scoped CTE names and columns inside nested queries, and evicts them once out of scope
        var inScopeTableSuggestions = await langService.GetSuggestionsAsync(new SuggestionContext
        {
            Prefix = "",
            ScriptBefore = "SELECT * FROM (WITH inner_cte(id, score) AS (SELECT 1, 10) SELECT * FROM ",
            FullScript = "SELECT * FROM (WITH inner_cte(id, score) AS (SELECT 1, 10) SELECT * FROM ",
            DocumentUri = "test://nested-with"
        });
        Assert.Contains(inScopeTableSuggestions, s => s.Type == SuggestionType.Table && s.Text.Equals("inner_cte", StringComparison.OrdinalIgnoreCase));

        var outOfScopeTableSuggestions = await langService.GetSuggestionsAsync(new SuggestionContext
        {
            Prefix = "",
            ScriptBefore = "SELECT * FROM (WITH inner_cte(id, score) AS (SELECT 1, 10) SELECT * FROM inner_cte) sub JOIN ",
            FullScript = "SELECT * FROM (WITH inner_cte(id, score) AS (SELECT 1, 10) SELECT * FROM inner_cte) sub JOIN ",
            DocumentUri = "test://nested-with"
        });
        Assert.DoesNotContain(outOfScopeTableSuggestions, s => s.Text.Equals("inner_cte", StringComparison.OrdinalIgnoreCase));

        var inScopeColumnSuggestions = await langService.GetSuggestionsAsync(new SuggestionContext
        {
            Prefix = "inner_cte.",
            ScriptBefore = "SELECT * FROM (WITH inner_cte(id, score) AS (SELECT 1, 10) SELECT inner_cte.",
            FullScript = "SELECT * FROM (WITH inner_cte(id, score) AS (SELECT 1, 10) SELECT inner_cte. FROM inner_cte)",
            DocumentUri = "test://nested-with"
        });
        Assert.Contains(inScopeColumnSuggestions, s => s.Type == SuggestionType.Column && s.Text.Equals("inner_cte.id", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(inScopeColumnSuggestions, s => s.Type == SuggestionType.Column && s.Text.Equals("inner_cte.score", StringComparison.OrdinalIgnoreCase));
    }
}
