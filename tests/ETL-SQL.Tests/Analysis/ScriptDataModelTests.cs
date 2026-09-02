using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Analysis.Services;
using Xunit;

namespace ETL_SQL.Tests.Analysis;

/// <summary>
/// The data-model projection behind Studio's ER view.
///
/// <para>Most of what is worth testing here is what the projection <em>refuses</em> to draw. An ER
/// view is unusually easy to make plausible and wrong: two tables carrying a column called
/// <c>region_id</c> look related, a join looks like a foreign key, and a foreign key looks like a
/// cardinality. Each of those is an inference the parser did not make, and a diagram that shows one
/// is worse than a diagram that shows nothing, because the author will believe it.</para>
/// </summary>
public class ScriptDataModelTests
{
    private readonly ScriptDataModelService _service = new();

    private const string JoinedScript = """
        CREATE CONNECTION corp AS MOCKDB();

        SELECT o.order_id, c.name, o.total
        INTO #enriched_orders
        FROM corp.orders o
        JOIN corp.customers c ON o.customer_id = c.customer_id;

        SELECT region, SUM(total) AS revenue INTO #by_region FROM #enriched_orders GROUP BY region;
        """;

    private static DataModelEntity Entity(ScriptDataModel model, string name) =>
        model.Entities.Single(entity => entity.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static DataModelRelationship? Between(ScriptDataModel model, string from, string to, string kind) =>
        model.Relationships.FirstOrDefault(relationship =>
            relationship.Kind == kind
            && relationship.From == Entity(model, from).Id
            && relationship.To == Entity(model, to).Id);

    [Fact]
    public void ReportsConnectionsTempTablesAndTheJoinsTheScriptWrites()
    {
        var model = _service.Project(JoinedScript);

        Assert.True(model.Parsed);
        Assert.Equal("connection", Entity(model, "corp").Kind);
        Assert.Equal("temp", Entity(model, "#enriched_orders").Kind);
        Assert.Equal("temp", Entity(model, "#by_region").Kind);
        Assert.Equal("corp", Entity(model, "orders").Connection);

        var join = Between(model, "orders", "customers", "join");
        Assert.NotNull(join);
        Assert.Equal("customer_id", join!.FromColumn);
        Assert.Equal("customer_id", join.ToColumn);

        // The chain of temps is most of an ETL-SQL model, so it is drawn as first-class edges.
        Assert.NotNull(Between(model, "orders", "#enriched_orders", "derivation"));
        Assert.NotNull(Between(model, "customers", "#enriched_orders", "derivation"));
        Assert.NotNull(Between(model, "#enriched_orders", "#by_region", "derivation"));
    }

    [Fact]
    public void WithoutSchemaEvidence_EveryCardinalityIsUnknownAndSaysSo()
    {
        var model = _service.Project(JoinedScript);

        Assert.False(model.HasSchemaEvidence);
        Assert.Equal("unknown", Between(model, "orders", "customers", "join")!.Cardinality);
        Assert.DoesNotContain(model.Relationships, relationship => relationship.Kind == "foreign-key");
    }

    [Fact]
    public void ADeclaredKeyOnOneSide_MakesTheJoinManyToOne()
    {
        var evidence = new DataModelSchemaEvidence(
            new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["corp.customers"] = new[] { "customer_id" },
            },
            []);

        var model = _service.Project(JoinedScript, evidence);

        Assert.True(model.HasSchemaEvidence);
        Assert.Equal("many-to-one", Between(model, "orders", "customers", "join")!.Cardinality);
        Assert.Contains(Entity(model, "customers").Columns, column => column is { Name: "customer_id", IsKey: true });
    }

    [Fact]
    public void MatchingColumnNamesAloneProduceNoRelationship()
    {
        // The inference every ER tool is tempted to make. Both tables carry `region_id`, nothing
        // joins them, and the projection must say nothing about them.
        const string script = """
            CREATE CONNECTION corp AS MOCKDB();
            SELECT region_id, total INTO #sales FROM corp.orders;
            SELECT region_id, name INTO #regions FROM corp.regions;
            """;

        var model = _service.Project(script);

        Assert.DoesNotContain(model.Relationships, relationship => relationship.Kind is "join" or "foreign-key");
    }

    [Fact]
    public void AnUnqualifiedJoinColumn_IsLeftUndrawnRatherThanGuessedAt()
    {
        const string script = """
            CREATE CONNECTION corp AS MOCKDB();
            SELECT o.total FROM corp.orders o JOIN corp.customers c ON customer_id = c.customer_id;
            """;

        var model = _service.Project(script);

        Assert.DoesNotContain(model.Relationships, relationship => relationship.Kind == "join");
    }

    [Fact]
    public void ADeclaredForeignKey_IsDrawnOnlyBetweenTablesTheScriptReads()
    {
        var evidence = new DataModelSchemaEvidence(
            new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase),
            [
                new DataModelForeignKey("corp.orders", "customer_id", "corp.customers", "customer_id"),
                // A relationship in the same database that this script never touches.
                new DataModelForeignKey("corp.shipments", "carrier_id", "corp.carriers", "carrier_id"),
            ]);

        var model = _service.Project(JoinedScript, evidence);

        var declared = model.Relationships.Where(relationship => relationship.Kind == "foreign-key").ToList();
        var single = Assert.Single(declared);
        Assert.Equal("schema", single.Evidence);
        Assert.Equal("many-to-one", single.Cardinality);
        Assert.DoesNotContain(model.Entities, entity => entity.Name.Equals("shipments", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ReportsCtesAndWhatTheyAreBuiltFrom()
    {
        const string script = """
            CREATE CONNECTION corp AS MOCKDB();

            WITH recent AS (SELECT order_id, total FROM corp.orders)
            SELECT order_id INTO #recent_ids FROM recent;
            """;

        var model = _service.Project(script);

        Assert.Equal("cte", Entity(model, "recent").Kind);
        Assert.NotNull(Between(model, "orders", "recent", "derivation"));
    }

    [Fact]
    public void ReadsTablesInsideControlFlowBlocks()
    {
        // A pipeline script keeps most of the tables it reads inside a block; a top-level-only walk
        // would report an empty model for exactly the scripts this view exists to explain.
        const string script = """
            CREATE CONNECTION corp AS MOCKDB();

            IF 1 = 1
            BEGIN
                SELECT order_id, total INTO #staged FROM corp.orders;
            END;
            """;

        var model = _service.Project(script);

        Assert.Equal("temp", Entity(model, "#staged").Kind);
        Assert.NotNull(Between(model, "orders", "#staged", "derivation"));
    }

    [Fact]
    public void APushdownBlockCreditsTheConnection_BecauseItsSqlIsNeverParsedHere()
    {
        // `EXECUTE conn INTO #t BEGIN … END` hands its body to the database verbatim. Reading table
        // names out of that text would mean this projection parsing a dialect it does not own — so
        // the only source it claims for the result is the connection the work was pushed to.
        const string script = """
            CREATE CONNECTION corp AS MOCKDB();

            EXECUTE corp INTO #staged BEGIN
                SELECT order_id, total FROM orders;
            END;
            """;

        var model = _service.Project(script);

        Assert.Equal("temp", Entity(model, "#staged").Kind);
        Assert.NotNull(Between(model, "corp", "#staged", "derivation"));
        Assert.Contains("pushed down to corp", Entity(model, "#staged").Detail);
        Assert.DoesNotContain(model.Entities, entity => entity.Name.Equals("orders", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ABrokenScriptIsRefusedRatherThanProjectedFromADamagedTree()
    {
        // The parser recovers from most errors instead of throwing, so the gate is the diagnostic,
        // not the exception — the same condition the designer parse route and the patcher use. A
        // model built from a damaged tree is the failure mode worth avoiding: it renders, and it is
        // wrong about what the script reads.
        var model = _service.Project(">>> INVALID <<<");

        Assert.False(model.Parsed);
        Assert.NotNull(model.Error);
        Assert.Empty(model.Entities);
    }
}
