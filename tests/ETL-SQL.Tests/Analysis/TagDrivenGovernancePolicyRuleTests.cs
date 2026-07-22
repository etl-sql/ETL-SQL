using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.Analysis.Linting.Rules;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine.Handlers;
using Xunit;

namespace ETL_SQL.Tests.Analysis;

public class TagDrivenGovernancePolicyRuleTests
{
    private static async Task<IReadOnlyList<LintResult>> LintAsync(string sql)
    {
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        var linter = new Linter();
        linter.AddRule(new TagDrivenGovernancePolicyRule());
        return await linter.AnalyzeAsync(script, new DefaultLintContext());
    }

    [Fact]
    public async Task PublicRestrictedDataset_ReportsMissingMetadataAndRestrictedPublicAccess()
    {
        var results = await LintAsync("""
            CREATE DATASET &claims ACCESS PUBLIC AS
            SELECT claim_id /* @classification: restricted; @quality: silver */
            FROM #claims;
            """);

        Assert.Contains(results, r => r.Code == TagDrivenGovernancePolicyRule.MissingMetadataCode && r.Severity == LintSeverity.Error);
        Assert.Contains(results, r => r.Code == TagDrivenGovernancePolicyRule.RestrictedPublicDatasetCode && r.Severity == LintSeverity.Error);
    }

    [Fact]
    public async Task SensitiveDatasetExportWithoutTransportEncryption_ReportsError()
    {
        var results = await LintAsync("""
            /* @owner: DataOps; @steward: steward@example.com; @contact: data@example.com; @classification: internal; @quality: silver */
            CREATE DATASET &customers AS
            SELECT email /* @pii: true */
            FROM #customers;

            EXPORT DATASET &customers TO 'customers.parquet';
            """);

        var result = Assert.Single(results, r => r.Code == TagDrivenGovernancePolicyRule.SensitiveExportCode);
        Assert.Equal(LintSeverity.Error, result.Severity);
    }

    [Fact]
    public async Task GoldQualityWithoutCompleteStewardshipMetadata_ReportsWarning()
    {
        var results = await LintAsync("""
            SELECT customer_id /* @quality: gold; @owner: CRM */
            FROM #customers;
            """);

        var result = Assert.Single(results, r => r.Code == TagDrivenGovernancePolicyRule.GoldQualityCode);
        Assert.Equal(LintSeverity.Warning, result.Severity);
        Assert.Contains("@steward", result.Message);
    }

    [Fact]
    public async Task CompletePrivateEncryptedDataset_NoFindings()
    {
        var results = await LintAsync("""
            /* @owner: DataOps; @steward: steward@example.com; @contact: data@example.com; @classification: internal; @quality: gold */
            CREATE DATASET &customers AS
            SELECT email /* @pii: true */
            FROM #customers;

            EXPORT DATASET &customers TO 'customers.parquet' ENCRYPT = PASSWORD PASSWORD = 'transport';
            """);

        Assert.Empty(results);
    }

    [Fact]
    public void RuntimePolicy_PublicDatasetMissingMetadata_Throws()
    {
        var ex = Assert.Throws<ExecutionException>(() =>
            TagGovernanceRuntimePolicy.EnforceDatasetPublish(
                "&customers",
                DatasetAccessLevel.Public,
                new Dictionary<string, string> { ["owner"] = "DataOps" },
                1,
                1));

        Assert.Contains("@steward", ex.Message);
        Assert.Contains("@classification", ex.Message);
    }

    [Fact]
    public void RuntimePolicy_PublicRestrictedDataset_Throws()
    {
        var ex = Assert.Throws<ExecutionException>(() =>
            TagGovernanceRuntimePolicy.EnforceDatasetPublish(
                "&claims",
                DatasetAccessLevel.Public,
                new Dictionary<string, string>
                {
                    ["owner"] = "ClaimsOps",
                    ["steward"] = "claims-steward@example.com",
                    ["contact"] = "claims-data@example.com",
                    ["classification"] = "restricted",
                    ["quality"] = "silver"
                },
                1,
                1));

        Assert.Contains("restricted", ex.Message);
        Assert.Contains("private", ex.Message);
    }

    [Fact]
    public void RuntimePolicy_PrivateGoldDatasetMissingMetadata_Throws()
    {
        var ex = Assert.Throws<ExecutionException>(() =>
            TagGovernanceRuntimePolicy.EnforceDatasetPublish(
                "&customers",
                DatasetAccessLevel.Private,
                new Dictionary<string, string> { ["quality"] = "gold" },
                1,
                1));

        Assert.Contains("@owner", ex.Message);
        Assert.Contains("@contact", ex.Message);
    }

    [Fact]
    public void RuntimePolicy_PublicInternalDatasetWithCompleteMetadata_Allows()
    {
        TagGovernanceRuntimePolicy.EnforceDatasetPublish(
            "&customers",
            DatasetAccessLevel.Public,
            new Dictionary<string, string>
            {
                ["owner"] = "DataOps",
                ["steward"] = "steward@example.com",
                ["contact"] = "data@example.com",
                ["classification"] = "internal",
                ["quality"] = "gold"
            },
            1,
            1);
    }
}
