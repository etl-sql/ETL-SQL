using ETL_SQL.Orchestrator.Execution;
using Xunit;

namespace ETL_SQL.Tests.Orchestration;

/// <summary>
/// A custom <c>Jobs:ArgumentsTemplate</c> that omits <c>--json</c> leaves the scheduler with no
/// result envelope, and the fallback path returns success with zero rows. Runs keep reporting green
/// while row counts and data-quality metrics quietly vanish — on precisely the customised
/// deployments least likely to notice a metric that was never there.
/// </summary>
public class ArgumentsTemplateGuardTests
{
    /// <summary>No template means the built-in arguments, which always include the envelope flag.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TheDefaultArgumentsNeedNoWarning(string? template) =>
        Assert.Null(ProcessJobExecutor.DescribeArgumentsTemplateRisk(template));

    [Theory]
    [InlineData("run {ScriptFile} --json")]
    [InlineData("run {ScriptFile} --json --session {SessionId}")]
    [InlineData("run {ScriptFile} --JSON")]
    public void ATemplateCarryingTheFlagIsAccepted(string template) =>
        Assert.Null(ProcessJobExecutor.DescribeArgumentsTemplateRisk(template));

    [Theory]
    [InlineData("run {ScriptFile}")]
    [InlineData("run {ScriptFile} --session {SessionId}")]
    [InlineData("run {ScriptFile} --verbose")]
    public void ATemplateMissingTheFlagIsReported(string template)
    {
        var warning = ProcessJobExecutor.DescribeArgumentsTemplateRisk(template);

        Assert.NotNull(warning);
        Assert.Contains("--json", warning);
    }

    /// <summary>
    /// The message has to explain the consequence, not just the omission. "Add --json" alone reads
    /// as pedantry; "runs will report success with zero rows" is why an operator should care.
    /// </summary>
    [Fact]
    public void TheWarningExplainsWhatGoesWrongRatherThanJustWhatIsMissing()
    {
        var warning = ProcessJobExecutor.DescribeArgumentsTemplateRisk("run {ScriptFile}")!;

        Assert.Contains("zero rows", warning);
        Assert.Contains("data-quality", warning);
    }

    /// <summary>A flag embedded in another token is not the flag.</summary>
    [Theory]
    [InlineData("run {ScriptFile} --json-output")]
    [InlineData("run {ScriptFile} --no-json")]
    public void ASimilarlyNamedTokenDoesNotCountAsTheFlag(string template) =>
        Assert.NotNull(ProcessJobExecutor.DescribeArgumentsTemplateRisk(template));
}
