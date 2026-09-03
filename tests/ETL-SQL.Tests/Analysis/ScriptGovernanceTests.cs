using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Analysis.Services;
using Xunit;

namespace ETL_SQL.Tests.Analysis;

/// <summary>
/// The governance projection and tag authoring behind Studio's governance panel.
///
/// <para>Two things are worth testing hardest here, and they pull in opposite directions. A tag the
/// panel <em>shows</em> must be one the engine would really apply — an inherited classification that
/// the engine would not actually inherit is a compliance claim the file does not support. And a tag
/// the panel <em>writes</em> must land in the one place its consumer reads: an inline comment for a
/// projected column, a tag statement for everything else. A write that goes to the wrong form is
/// inert, and inert governance metadata looks exactly like enforced governance metadata.</para>
/// </summary>
public class ScriptGovernanceTests
{
    private readonly ScriptGovernanceService _service = new();

    private const string TaggedScript = """
        -- @owner: analytics; @classification: internal

        CREATE CONNECTION corp AS MOCKDB();

        INSERT TAG FOR TABLE corp.customers COLUMN email (pii = 'true', classification = 'restricted');

        SELECT email, name AS customer_name, UPPER(name) AS shouty
        INTO #people
        FROM corp.customers;
        """;

    private static GovernanceScope Scope(ScriptGovernance governance, string id) =>
        governance.Scopes.Single(scope => scope.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    private static GovernanceTag? TagOn(GovernanceScope scope, string name) =>
        scope.Tags.FirstOrDefault(tag => tag.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static Dictionary<string, string?> Set(string name, string? value) => new() { [name] = value };

    // ── Reading ──────────────────────────────────────────────────────────────

    [Fact]
    public void Reports_a_tag_statement_against_the_column_it_names()
    {
        var governance = _service.Read(TaggedScript);

        var column = Scope(governance, "column:corp.customers.email");
        Assert.Equal("true", TagOn(column, "pii")?.Value);
        Assert.Equal(GovernanceTagOrigin.Statement, TagOn(column, "pii")?.Origin);
    }

    [Fact]
    public void Inherits_a_source_column_tag_onto_the_column_that_reads_it()
    {
        var governance = _service.Read(TaggedScript);

        var inherited = TagOn(Scope(governance, "column:#people.email"), "pii");
        Assert.Equal("true", inherited?.Value);
        Assert.Equal(GovernanceTagOrigin.Derived, inherited?.Origin);
        Assert.Equal("corp.customers.email", inherited?.DerivedFrom);
        Assert.False(inherited?.Editable);
    }

    [Fact]
    public void Does_not_inherit_onto_a_column_that_is_an_expression()
    {
        // UPPER(name) is not the source column any more, so the source column's tags are no longer a
        // statement about the value. The engine does not carry them across, and neither does this.
        var governance = _service.Read("""
            CREATE CONNECTION corp AS MOCKDB();
            INSERT TAG FOR TABLE corp.customers COLUMN name (classification = 'confidential');
            SELECT UPPER(name) AS shouty INTO #people FROM corp.customers;
            """);

        Assert.Null(TagOn(Scope(governance, "column:#people.shouty"), "classification"));
    }

    [Fact]
    public void Does_not_inherit_when_two_joined_sources_could_both_supply_the_column()
    {
        var governance = _service.Read("""
            CREATE CONNECTION corp AS MOCKDB();
            INSERT TAG FOR TABLE corp.customers COLUMN region (classification = 'confidential');
            SELECT region INTO #x FROM corp.customers JOIN corp.stores ON corp.customers.id = corp.stores.id;
            """);

        Assert.Null(TagOn(Scope(governance, "column:#x.region"), "classification"));
    }

    [Fact]
    public void Reports_a_script_header_tag_as_derived_on_every_scope_it_reaches()
    {
        var governance = _service.Read(TaggedScript);

        var fromHeader = TagOn(Scope(governance, "column:#people.customer_name"), "owner");
        Assert.Equal("analytics", fromHeader?.Value);
        Assert.Equal(GovernanceTagOrigin.Derived, fromHeader?.Origin);
        Assert.Contains("header", fromHeader?.DerivedFrom ?? string.Empty);
    }

    [Fact]
    public void A_column_tag_wins_over_the_header_tag_of_the_same_name()
    {
        var governance = _service.Read("""
            -- @classification: internal
            CREATE CONNECTION corp AS MOCKDB();
            SELECT name /* @classification: restricted */ INTO #people FROM corp.customers;
            """);

        var tag = TagOn(Scope(governance, "column:#people.name"), "classification");
        Assert.Equal("restricted", tag?.Value);
        Assert.Equal(GovernanceTagOrigin.Inline, tag?.Origin);
    }

    [Fact]
    public void Names_the_required_tags_a_scope_is_missing()
    {
        var governance = _service.Read(TaggedScript);

        var missing = Scope(governance, "table:#people").MissingRequired;
        Assert.Contains("steward", missing);
        Assert.DoesNotContain("owner", missing);   // the header supplies it
    }

    [Fact]
    public void Carries_the_governance_lint_findings_alongside_the_scopes()
    {
        var governance = _service.Read("""
            CREATE CONNECTION corp AS MOCKDB();
            SELECT name INTO #people FROM corp.customers;
            CREATE DATASET &people ACCESS PUBLIC AS (SELECT name FROM #people);
            """);

        Assert.Contains(governance.Findings, finding => finding.Code == "ETLSQL-GOV-TAG-MISSING-METADATA");
    }

    [Fact]
    public void A_script_that_does_not_parse_is_refused_rather_than_projected_empty()
    {
        var governance = _service.Read("SELECT * FROM corp.orders WHERE ) = 1;");

        Assert.False(governance.Parsed);
        Assert.NotNull(governance.Error);
        Assert.Empty(governance.Scopes);
    }

    [Fact]
    public void Groups_a_scope_under_the_labelled_task_that_writes_it()
    {
        var governance = _service.Read("""
            CREATE CONNECTION corp AS MOCKDB();
            stage_people:
            SELECT name INTO #people FROM corp.customers;
            """);

        Assert.Equal("stage_people", Scope(governance, "table:#people").Producer);
    }

    // ── Writing ──────────────────────────────────────────────────────────────

    [Fact]
    public void Writes_a_projected_column_tag_as_an_inline_comment()
    {
        var result = _service.Write(TaggedScript, "column:#people.customer_name", Set("steward", "dana"));

        Assert.True(result.Applied, result.Error);
        Assert.Contains("@steward: dana", result.Script);
        Assert.DoesNotContain("INSERT TAG FOR TABLE #people", result.Script);

        var reread = TagOn(Scope(_service.Read(result.Script), "column:#people.customer_name"), "steward");
        Assert.Equal("dana", reread?.Value);
        Assert.Equal(GovernanceTagOrigin.Inline, reread?.Origin);
    }

    [Fact]
    public void Merges_a_new_tag_into_the_comment_a_column_already_carries()
    {
        var script = """
            CREATE CONNECTION corp AS MOCKDB();
            SELECT name /* @d: the customer name */ AS customer_name INTO #people FROM corp.customers;
            """;

        var result = _service.Write(script, "column:#people.customer_name", Set("owner", "analytics"));

        Assert.True(result.Applied, result.Error);
        Assert.Equal(1, result.Script.Split("/*").Length - 1);
        Assert.Contains("@d:", result.Script);
        Assert.Contains("@owner: analytics", result.Script);
    }

    [Fact]
    public void Writes_a_temp_table_tag_as_a_tag_statement_after_the_statement_that_builds_it()
    {
        var result = _service.Write(TaggedScript, "table:#people", Set("quality", "gold"));

        Assert.True(result.Applied, result.Error);
        Assert.Contains("INSERT TAG FOR TABLE #people (quality = 'gold');", result.Script);

        var statementIndex = result.Script.IndexOf("INSERT TAG FOR TABLE #people", StringComparison.Ordinal);
        var buildIndex = result.Script.IndexOf("INTO #people", StringComparison.Ordinal);
        Assert.True(statementIndex > buildIndex, "the tag must be applied after the table exists");
    }

    [Fact]
    public void Writes_a_source_table_tag_before_the_statement_that_reads_it()
    {
        // A tag on a table the script only reads has to be set before the read, or the columns that
        // read it inherit nothing — which is silently no governance at all rather than an error.
        var result = _service.Write(TaggedScript, "table:corp.customers", Set("classification", "confidential"));

        Assert.True(result.Applied, result.Error);
        var tagIndex = result.Script.IndexOf("INSERT TAG FOR TABLE corp.customers (", StringComparison.Ordinal);
        var readIndex = result.Script.IndexOf("FROM corp.customers", StringComparison.Ordinal);
        Assert.True(tagIndex >= 0 && tagIndex < readIndex, "the tag must be set before the table is read");
    }

    [Fact]
    public void Removing_an_inline_tag_takes_the_comment_with_it_when_nothing_is_left()
    {
        var script = """
            CREATE CONNECTION corp AS MOCKDB();
            SELECT name /* @owner: analytics */ AS customer_name INTO #people FROM corp.customers;
            """;

        var result = _service.Write(script, "column:#people.customer_name", Set("owner", null));

        Assert.True(result.Applied, result.Error);
        Assert.DoesNotContain("@owner", result.Script);
        Assert.DoesNotContain("/*", result.Script);
        Assert.Contains("SELECT name AS customer_name", result.Script);
    }

    [Fact]
    public void Turning_off_an_inherited_tag_writes_the_delete_the_engine_reads()
    {
        var result = _service.Write(TaggedScript, "table:#people", Set("pii", null));

        Assert.True(result.Applied, result.Error);
        Assert.Contains("DELETE TAG FOR TABLE #people (pii);", result.Script);
    }

    [Fact]
    public void Writes_the_script_header_tag_at_the_top()
    {
        var result = _service.Write(TaggedScript, "script", Set("steward", "dana"));

        Assert.True(result.Applied, result.Error);
        Assert.Equal("dana", _service.Read(result.Script).Scopes[0].Tags
            .Single(tag => tag.Name == "steward").Value);
        Assert.Equal("analytics", _service.Read(result.Script).Scopes[0].Tags
            .Single(tag => tag.Name == "owner").Value);
    }

    [Fact]
    public void Refuses_a_value_the_tag_catalog_does_not_accept()
    {
        var result = _service.Write(TaggedScript, "table:#people", Set("classification", "sort-of-secret"));

        Assert.False(result.Applied);
        Assert.Contains("public, internal, confidential, restricted", result.Error);
        Assert.Equal(TaggedScript, result.Script);
    }

    [Fact]
    public void Refuses_a_tag_that_is_not_in_the_catalog_and_not_marked_as_the_organisation_s_own()
    {
        var result = _service.Write(TaggedScript, "table:#people", Set("cost_centre", "42"));

        Assert.False(result.Applied);
        Assert.Contains("org_", result.Error);

        var custom = _service.Write(TaggedScript, "table:#people", Set("org_cost_centre", "42"));
        Assert.True(custom.Applied, custom.Error);
    }

    [Fact]
    public void Refuses_to_hand_author_a_rule_tag_the_engine_projects()
    {
        // @expect/@fail are published by the engine from a column's EXPECT clauses. A hand-written
        // one is inert and would look enforced, which is the worst of both.
        var result = _service.Write(TaggedScript, "column:#people.customer_name", Set("expect", "NOT NULL"));

        Assert.False(result.Applied);
        Assert.Contains("EXPECT", result.Error);
    }

    [Fact]
    public void Refuses_a_scope_this_script_does_not_have()
    {
        var result = _service.Write(TaggedScript, "table:#nowhere", Set("owner", "analytics"));

        Assert.False(result.Applied);
        Assert.Contains("#nowhere", result.Error);
    }

    [Fact]
    public void Leaves_every_other_byte_of_the_script_alone()
    {
        var result = _service.Write(TaggedScript, "table:#people", Set("quality", "gold"));

        Assert.True(result.Applied, result.Error);
        foreach (var line in TaggedScript.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Trim().Length > 0))
            Assert.Contains(line, result.Script);
    }
}
