using System;
using System.Linq;
using ETL_SQL.Analysis.Services;
using Xunit;

namespace ETL_SQL.Tests.Analysis;

/// <summary>
/// The data-quality rule projection and authoring behind Studio's governance rail.
///
/// <para>An <c>EXPECT</c> clause decides which rows leave a statement, so the failure mode that
/// matters here is a rule that reads as enforced and is not: written into the wrong place, written
/// with an action the column cannot elect, or electing QUARANTINE with no route for the rows to take.
/// The tests below are mostly about those three.</para>
/// </summary>
public class ScriptQualityRuleTests
{
    private readonly ScriptQualityRuleService _service = new();

    private const string Script = """
        CREATE CONNECTION corp AS MOCKDB();

        SELECT
            order_id EXPECT NOT NULL ON FAILURE THROW,
            email AS customer_email EXPECT NOT BLANK,
            total
        INTO #orders
        FROM corp.orders
        ON FAILURE WARN;
        """;

    private static QualityStatement Statement(ScriptQuality quality, string id) =>
        quality.Statements.Single(statement => statement.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    private static QualityColumnRules Column(ScriptQuality quality, string scopeId) =>
        quality.Statements.SelectMany(statement => statement.Columns)
            .Single(column => column.ScopeId.Equals(scopeId, StringComparison.OrdinalIgnoreCase));

    // ── Reading ──────────────────────────────────────────────────────────────

    [Fact]
    public void Reports_each_clause_with_the_action_it_elects()
    {
        var quality = _service.Read(Script);

        var clause = Column(quality, "column:#orders.order_id").Clauses.Single();
        Assert.Equal("NOT NULL", clause.Rule);
        Assert.Equal("THROW", clause.Action);
        Assert.True(clause.ActionExplicit);
    }

    [Fact]
    public void An_omitted_action_reports_as_warn_but_says_it_was_not_written()
    {
        // WARN is the fail-safe default, not silence — and "defaults to warn" is a different fact
        // about a pipeline from "somebody chose warn".
        var clause = Column(_service.Read(Script), "column:#orders.customer_email").Clauses.Single();

        Assert.Equal("WARN", clause.Action);
        Assert.False(clause.ActionExplicit);
    }

    [Fact]
    public void Reports_the_statements_own_routing()
    {
        var routing = Statement(_service.Read(Script), "#orders").Routing.Single();

        Assert.Equal("WARN", routing.Action);
        Assert.Null(routing.Target);
    }

    [Fact]
    public void A_column_with_no_rules_is_not_reported_as_a_rule_holder()
    {
        Assert.DoesNotContain(Statement(_service.Read(Script), "#orders").Columns,
            column => column.Column == "total");
    }

    [Fact]
    public void Flags_a_column_electing_quarantine_when_the_statement_routes_nowhere()
    {
        // The parser refuses a routing clause with no target; nothing refuses a column rule whose
        // action has no route, so the rows have nowhere to go and the run only says so when it runs.
        var quality = _service.Read("""
            CREATE CONNECTION corp AS MOCKDB();
            SELECT order_id EXPECT NOT NULL ON FAILURE QUARANTINE INTO #orders FROM corp.orders;
            """);

        Assert.True(Statement(quality, "#orders").MissingQuarantineTarget);
    }

    [Fact]
    public void A_routed_quarantine_is_not_flagged()
    {
        var quality = _service.Read("""
            CREATE CONNECTION corp AS MOCKDB();
            SELECT order_id EXPECT NOT NULL ON FAILURE QUARANTINE
            INTO #orders FROM corp.orders
            ON FAILURE QUARANTINE TO #bad_orders WITH (RETENTION = '30 DAYS', HANDLING = STEWARD);
            """);

        var statement = Statement(quality, "#orders");
        Assert.False(statement.MissingQuarantineTarget);
        var routing = statement.Routing.Single();
        Assert.Equal("#bad_orders", routing.Target);
        Assert.Equal("30 DAYS", routing.Retention);
        Assert.Equal("STEWARD", routing.Handling);
    }

    [Fact]
    public void A_query_that_names_no_output_is_left_alone()
    {
        // A rule there would have no stable identity to edit against, and quarantined rows from a
        // query whose output goes straight to a reader have nowhere meaningful to be routed.
        var quality = _service.Read("""
            CREATE CONNECTION corp AS MOCKDB();
            SELECT order_id FROM corp.orders;
            """);

        Assert.Empty(quality.Statements);
    }

    [Fact]
    public void A_script_that_does_not_parse_is_refused_rather_than_projected_empty()
    {
        var quality = _service.Read("SELECT * FROM corp.orders WHERE ) = 1;");

        Assert.False(quality.Parsed);
        Assert.NotNull(quality.Error);
    }

    // ── Writing ──────────────────────────────────────────────────────────────

    [Fact]
    public void Adds_a_rule_to_a_column_that_had_none()
    {
        var result = _service.SetRule(Script, "column:#orders.total", -1, ">= 0", "WARN");

        Assert.True(result.Applied, result.Error);
        Assert.Contains("total EXPECT >= 0 ON FAILURE WARN", result.Script);

        var clause = Column(_service.Read(result.Script), "column:#orders.total").Clauses.Single();
        Assert.Equal(">= 0", clause.Rule);
        Assert.True(clause.ActionExplicit);
    }

    [Fact]
    public void Adds_a_second_rule_after_the_one_a_column_already_carries()
    {
        var result = _service.SetRule(Script, "column:#orders.customer_email", -1, "MATCHES '@'", "QUARANTINE");

        Assert.True(result.Applied, result.Error);
        var clauses = Column(_service.Read(result.Script), "column:#orders.customer_email").Clauses;
        Assert.Equal(2, clauses.Count);
        Assert.Equal("NOT BLANK", clauses[0].Rule);
        Assert.Equal("QUARANTINE", clauses[1].Action);
    }

    [Fact]
    public void Replaces_the_clause_at_an_index_without_touching_its_neighbour()
    {
        var result = _service.SetRule(Script, "column:#orders.order_id", 0, "NOT NULL AND > 0", "THROW");

        Assert.True(result.Applied, result.Error);
        var clauses = Column(_service.Read(result.Script), "column:#orders.order_id").Clauses;
        Assert.Single(clauses);
        Assert.Equal("NOT NULL AND > 0", clauses[0].Rule);
        Assert.Contains("EXPECT NOT BLANK", result.Script);
    }

    [Fact]
    public void Removing_the_only_rule_leaves_the_column_as_it_was_written()
    {
        var result = _service.RemoveRule(Script, "column:#orders.customer_email", 0);

        Assert.True(result.Applied, result.Error);
        Assert.Contains("email AS customer_email,", result.Script);
        Assert.DoesNotContain("NOT BLANK", result.Script);
    }

    [Fact]
    public void Refuses_a_rule_the_parser_does_not_accept()
    {
        // Nothing here re-implements the rule grammar: the verdict is the parser's, reached by
        // reparsing what was written, so this surface cannot drift from what actually runs.
        var result = _service.SetRule(Script, "column:#orders.total", -1, "BETWEEN", "WARN");

        Assert.False(result.Applied);
        Assert.Contains("would not parse", result.Error);
        Assert.Equal(Script, result.Script);
    }

    [Fact]
    public void Refuses_a_rule_for_a_column_the_script_does_not_project()
    {
        var result = _service.SetRule(Script, "column:#orders.nothing", -1, "NOT NULL", null);

        Assert.False(result.Applied);
        Assert.Contains("nothing", result.Error);
    }

    [Fact]
    public void Adds_statement_routing_inside_the_statement_it_routes()
    {
        var script = """
            CREATE CONNECTION corp AS MOCKDB();
            SELECT order_id EXPECT NOT NULL ON FAILURE QUARANTINE INTO #orders FROM corp.orders;
            """;

        var result = _service.SetRouting(script, "#orders", "QUARANTINE", "#bad_orders", "30 DAYS", "STEWARD");

        Assert.True(result.Applied, result.Error);
        Assert.Contains("ON FAILURE QUARANTINE TO #bad_orders WITH (RETENTION = '30 DAYS', HANDLING = STEWARD);", result.Script);
        Assert.False(Statement(_service.Read(result.Script), "#orders").MissingQuarantineTarget);
    }

    [Fact]
    public void Replaces_a_routing_clause_the_statement_already_has()
    {
        var result = _service.SetRouting(Script, "#orders", "WARN", null, null, null);

        Assert.True(result.Applied, result.Error);
        Assert.Single(Statement(_service.Read(result.Script), "#orders").Routing);
    }

    [Fact]
    public void Removes_a_routing_clause()
    {
        var result = _service.SetRouting(Script, "#orders", "WARN", null, null, null, remove: true);

        Assert.True(result.Applied, result.Error);
        Assert.Empty(Statement(_service.Read(result.Script), "#orders").Routing);
        Assert.Contains("FROM corp.orders", result.Script);
    }

    [Fact]
    public void Refuses_quarantine_routing_with_no_target()
    {
        var result = _service.SetRouting(Script, "#orders", "QUARANTINE", null, null, null);

        Assert.False(result.Applied);
        Assert.Contains("nowhere else to go", result.Error);
    }

    [Fact]
    public void Refuses_a_target_on_throw()
    {
        var result = _service.SetRouting(Script, "#orders", "THROW", "#bad", null, null);

        Assert.False(result.Applied);
        Assert.Contains("does not take a target", result.Error);
    }

    [Fact]
    public void Refuses_handling_on_anything_but_quarantine()
    {
        var result = _service.SetRouting(Script, "#orders", "WARN", "#kept", null, "STEWARD");

        Assert.False(result.Applied);
        Assert.Contains("QUARANTINE", result.Error);
    }

    [Fact]
    public void Refuses_a_retention_interval_that_is_not_one()
    {
        var result = _service.SetRouting(Script, "#orders", "QUARANTINE", "#bad", "a while", "STEWARD");

        Assert.False(result.Applied);
        Assert.Contains("MINUTES", result.Error);
    }

    [Fact]
    public void Leaves_every_other_line_of_the_script_alone()
    {
        var result = _service.SetRule(Script, "column:#orders.total", -1, "NOT NULL", "WARN");

        Assert.True(result.Applied, result.Error);
        foreach (var line in Script.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Trim().Length > 0))
        {
            if (line.Trim().StartsWith("total", StringComparison.Ordinal)) continue;
            Assert.Contains(line, result.Script);
        }
    }
}
