using System;
using System.Linq;
using ETL_SQL.Analysis.Services;
using Xunit;

namespace ETL_SQL.Tests.Analysis;

/// <summary>
/// Dataset lifecycle authoring: access, TTL, and the refresh/export/publish statements a script
/// declares.
///
/// <para>The claim under test throughout is that an edit here is a span and not a regeneration. A
/// <c>CREATE DATASET</c> carries clauses no authoring model represents, and the only way to
/// guarantee they survive is to never write the bytes that hold them — so most of these assertions
/// are about what is still in the script after an edit that had nothing to do with it.</para>
/// </summary>
public class ScriptDatasetLifecycleTests
{
    private readonly ScriptDatasetLifecycleService _service = new();

    private const string Script = """
        CREATE CONNECTION corp AS MOCKDB();

        SELECT region, total INTO #sales FROM corp.orders;

        CREATE DATASET &sales COMPRESS = ON ENCRYPT = MACHINE TTL = '1h' AS (
          SELECT region, total FROM #sales
        );
        """;

    private static ScriptDataset Dataset(ScriptDatasetLifecycle lifecycle, string name) =>
        lifecycle.Datasets.Single(dataset => dataset.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    // ── Reading ──────────────────────────────────────────────────────────────

    [Fact]
    public void Reports_what_the_script_declares_about_a_dataset()
    {
        var dataset = Dataset(_service.Read(Script), "&sales");

        Assert.Equal("PRIVATE", dataset.Access);
        Assert.Equal("1h", dataset.Ttl);
        Assert.True(dataset.Compress);
        Assert.Equal("machine", dataset.Encryption);
    }

    [Fact]
    public void Reports_the_lifecycle_statements_a_script_already_declares()
    {
        var lifecycle = _service.Read(Script + "\nREFRESH DATASET &sales;\nEXPORT DATASET &sales TO 'out.parquet' ENCRYPT = PASSWORD PASSWORD = 'x';");

        var steps = Dataset(lifecycle, "&sales").Lifecycle;
        Assert.Equal(["refresh", "export"], steps.Select(step => step.Kind).ToArray());
        Assert.Equal("out.parquet", steps[1].Detail);
    }

    [Fact]
    public void A_script_that_does_not_parse_is_refused_rather_than_projected_empty()
    {
        var lifecycle = _service.Read("SELECT * FROM corp.orders WHERE ) = 1;");

        Assert.False(lifecycle.Parsed);
        Assert.Empty(lifecycle.Datasets);
    }

    // ── Access and TTL ───────────────────────────────────────────────────────

    [Fact]
    public void Making_a_dataset_public_leaves_every_other_clause_where_it_was()
    {
        var result = _service.SetAccess(Script, "&sales", "PUBLIC");

        Assert.True(result.Applied, result.Error);
        Assert.Equal("PUBLIC", Dataset(_service.Read(result.Script), "&sales").Access);
        Assert.Contains("COMPRESS = ON", result.Script, StringComparison.Ordinal);
        Assert.Contains("ENCRYPT = MACHINE", result.Script, StringComparison.Ordinal);
        Assert.Contains("TTL = '1h'", result.Script, StringComparison.Ordinal);
    }

    [Fact]
    public void Making_a_public_dataset_private_again_removes_the_clause_rather_than_writing_the_default()
    {
        var made = _service.SetAccess(Script, "&sales", "PUBLIC");
        var result = _service.SetAccess(made.Script, "&sales", "PRIVATE");

        Assert.True(result.Applied, result.Error);
        Assert.DoesNotContain("ACCESS", result.Script, StringComparison.Ordinal);
        Assert.Equal("PRIVATE", Dataset(_service.Read(result.Script), "&sales").Access);
        Assert.Contains("ENCRYPT = MACHINE", result.Script, StringComparison.Ordinal);
    }

    [Fact]
    public void Changing_the_ttl_edits_only_the_ttl()
    {
        var result = _service.SetTtl(Script, "&sales", "24h");

        Assert.True(result.Applied, result.Error);
        Assert.Contains("TTL = '24h'", result.Script, StringComparison.Ordinal);
        Assert.Contains("COMPRESS = ON", result.Script, StringComparison.Ordinal);
    }

    [Fact]
    public void Clearing_the_ttl_removes_the_clause()
    {
        var result = _service.SetTtl(Script, "&sales", null);

        Assert.True(result.Applied, result.Error);
        Assert.DoesNotContain("TTL", result.Script, StringComparison.Ordinal);
        Assert.Contains("ENCRYPT = MACHINE", result.Script, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_an_access_level_that_is_not_one()
    {
        var result = _service.SetAccess(Script, "&sales", "SEMI_PUBLIC");

        Assert.False(result.Applied);
        Assert.Contains("PUBLIC or PRIVATE", result.Error);
        Assert.Equal(Script, result.Script);
    }

    [Fact]
    public void Refuses_a_dataset_the_script_does_not_create()
    {
        var result = _service.SetAccess(Script, "&nothing", "PUBLIC");

        Assert.False(result.Applied);
        Assert.Contains("nothing", result.Error);
    }

    // ── Lifecycle statements ─────────────────────────────────────────────────

    [Fact]
    public void Writes_a_refresh_after_the_statement_that_creates_the_dataset()
    {
        var result = _service.AddLifecycleStatement(Script, "&sales", "refresh", null, null, null, null, null);

        Assert.True(result.Applied, result.Error);
        Assert.Contains("REFRESH DATASET &sales;", result.Script, StringComparison.Ordinal);
        Assert.True(
            result.Script.IndexOf("REFRESH DATASET", StringComparison.Ordinal)
                > result.Script.IndexOf("CREATE DATASET", StringComparison.Ordinal),
            "refreshing a dataset the script has not created yet is a statement about nothing");
    }

    [Fact]
    public void Writes_an_export_with_its_transport_credential()
    {
        var result = _service.AddLifecycleStatement(
            Script, "&sales", "export", "/tmp/sales.parquet", "PASSWORD", "hunter2", null, null);

        Assert.True(result.Applied, result.Error);
        Assert.Contains(
            "EXPORT DATASET &sales TO '/tmp/sales.parquet' ENCRYPT = PASSWORD PASSWORD = 'hunter2';",
            result.Script,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_an_export_with_no_transport_credential()
    {
        // The file leaves the machine that wrote it, so it cannot carry the at-rest key only that
        // machine holds. Without one the export produces a file nothing can publish, and finding
        // that out is a run away.
        var result = _service.AddLifecycleStatement(
            Script, "&sales", "export", "/tmp/sales.parquet", null, null, null, null);

        Assert.False(result.Applied);
        Assert.Contains("transport credential", result.Error);
    }

    [Fact]
    public void Writes_a_publish_with_its_folder_and_access_level()
    {
        var result = _service.AddLifecycleStatement(
            Script, "&sales", "publish", "/tmp/sales.parquet", "KEYFILE", "/keys/transport.key", "/Sales", "PUBLIC");

        Assert.True(result.Applied, result.Error);
        Assert.Contains(
            "PUBLISH DATASET &sales FROM '/tmp/sales.parquet' INTO '/Sales' ACCESS PUBLIC ENCRYPT = KEYFILE KEYFILE = '/keys/transport.key';",
            result.Script,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_lifecycle_step_that_is_not_one()
    {
        var result = _service.AddLifecycleStatement(Script, "&sales", "archive", null, null, null, null, null);

        Assert.False(result.Applied);
        Assert.Contains("archive", result.Error);
    }

    [Fact]
    public void An_added_statement_that_would_not_parse_is_refused_rather_than_written()
    {
        var result = _service.AddLifecycleStatement(
            Script, "&sales", "export", "already'quoted", "PASSWORD", "x", null, null);

        // The path is escaped rather than refused; the point is that the result parses.
        Assert.True(result.Applied, result.Error);
        Assert.Contains("'already''quoted'", result.Script, StringComparison.Ordinal);
    }
}
