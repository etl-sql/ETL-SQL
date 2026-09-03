using System;
using System.Linq;
using ETL_SQL.Portal.Services;
using Xunit;

namespace ETL_SQL.Tests.Portal;

/// <summary>
/// What a designer edit is allowed to do to a <c>CREATE DATASET</c> statement.
///
/// <para>Designer state models a dataset's query and its TTL. It does not model <c>ACCESS</c>,
/// <c>COMPRESS</c>, or <c>ENCRYPT</c>/<c>PASSWORD</c>/<c>KEYFILE</c> — and the patcher used to
/// regenerate the whole statement from that state, so any edit touching the query rewrote
/// <c>ACCESS PUBLIC</c> back to the private default and dropped the encryption mode. Applying a
/// dataset-scoped filter is such an edit, and it is one click.</para>
///
/// <para>These tests are the guarantee that a clause nothing in the pipeline models survives the
/// pipeline untouched. They assert on the bytes, because that is the only place the guarantee
/// lives.</para>
/// </summary>
public class DatasetClausePreservationTests
{
    private readonly DesignerAnalysisService _analysis = new();
    private readonly DesignerScriptPatcher _patcher = new();

    private const string GovernedDataset = """
        CREATE CONNECTION corp AS MOCKDB();

        CREATE DATASET &sales_public ACCESS PUBLIC COMPRESS = ON ENCRYPT = MACHINE TTL = '1h' AS (
          SELECT region, total FROM corp.orders
        );
        """;

    private string Rewrite(string script, Func<ETL_SQL.Portal.Models.DesignerStateDto, ETL_SQL.Portal.Models.DesignerStateDto> edit)
    {
        var parsed = _analysis.Parse(script, 500);
        Assert.NotNull(parsed.DesignState);
        return _patcher.Patch(script, edit(parsed.DesignState!));
    }

    private static ETL_SQL.Portal.Models.DesignerStateDto WithDatasetQuery(
        ETL_SQL.Portal.Models.DesignerStateDto state,
        string query) =>
        state with
        {
            Datasets = state.Datasets
                .Select(dataset => dataset with { Query = query })
                .ToList()
        };

    [Fact]
    public void Changing_the_query_keeps_every_clause_the_designer_does_not_model()
    {
        var patched = Rewrite(GovernedDataset, state =>
            WithDatasetQuery(state, "SELECT region, total FROM corp.orders WHERE total > 100"));

        Assert.Contains("WHERE total > 100", patched, StringComparison.Ordinal);
        Assert.Contains("ACCESS PUBLIC", patched, StringComparison.Ordinal);
        Assert.Contains("COMPRESS = ON", patched, StringComparison.Ordinal);
        Assert.Contains("ENCRYPT = MACHINE", patched, StringComparison.Ordinal);
        Assert.Contains("TTL = '1h'", patched, StringComparison.Ordinal);
    }

    [Fact]
    public void A_password_is_never_rewritten_through_the_designer_round_trip()
    {
        // The value is already in the script, so preserving it is safe; reading it out and writing it
        // back is a journey a secret has no reason to make, and the way to guarantee it does not
        // happen is to never write the bytes that hold it.
        const string script = """
            CREATE CONNECTION corp AS MOCKDB();

            CREATE DATASET &secured ENCRYPT = PASSWORD PASSWORD = 'correct horse' AS (
              SELECT region FROM corp.orders
            );
            """;

        var patched = Rewrite(script, state => WithDatasetQuery(state, "SELECT region FROM corp.orders WHERE region = 'North'"));

        Assert.Contains("WHERE region = 'North'", patched, StringComparison.Ordinal);
        Assert.Contains("ENCRYPT = PASSWORD PASSWORD = 'correct horse'", patched, StringComparison.Ordinal);
    }

    [Fact]
    public void Changing_the_ttl_edits_only_the_ttl_clause()
    {
        var patched = Rewrite(GovernedDataset, state => state with
        {
            Datasets = state.Datasets.Select(dataset => dataset with { Ttl = "24h" }).ToList()
        });

        Assert.Contains("TTL = '24h'", patched, StringComparison.Ordinal);
        Assert.DoesNotContain("TTL = '1h'", patched, StringComparison.Ordinal);
        Assert.Contains("ACCESS PUBLIC", patched, StringComparison.Ordinal);
        Assert.Contains("ENCRYPT = MACHINE", patched, StringComparison.Ordinal);
    }

    [Fact]
    public void Adding_a_ttl_to_a_dataset_that_had_none_leaves_its_other_clauses_alone()
    {
        const string script = """
            CREATE CONNECTION corp AS MOCKDB();

            CREATE DATASET &plain ACCESS PUBLIC AS (
              SELECT region FROM corp.orders
            );
            """;

        var patched = Rewrite(script, state => state with
        {
            Datasets = state.Datasets.Select(dataset => dataset with { Ttl = "30m" }).ToList()
        });

        Assert.Contains("TTL = '30m'", patched, StringComparison.Ordinal);
        Assert.Contains("ACCESS PUBLIC", patched, StringComparison.Ordinal);
    }

    [Fact]
    public void An_edit_that_changes_nothing_leaves_the_dataset_statement_byte_for_byte()
    {
        // Scoped to the statement rather than the file: patching an empty design state also
        // scaffolds a CREATE PAGE, which is the patcher's own long-standing behaviour and not what
        // this test is about.
        var parsed = _analysis.Parse(GovernedDataset, 500);
        var patched = _patcher.Patch(GovernedDataset, parsed.DesignState!);

        Assert.Contains(
            "CREATE DATASET &sales_public ACCESS PUBLIC COMPRESS = ON ENCRYPT = MACHINE TTL = '1h' AS (",
            patched,
            StringComparison.Ordinal);
        Assert.Contains("  SELECT region, total FROM corp.orders", patched, StringComparison.Ordinal);
    }
}
