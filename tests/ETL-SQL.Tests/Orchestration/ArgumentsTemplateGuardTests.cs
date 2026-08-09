using System.Collections.Generic;
using System.Linq;
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

    [Theory]
    [InlineData(null)]
    [InlineData("run {ScriptFile} --json --session {SessionId}")]
    public void VariableOverridesAreAppendedToDefaultAndCustomArgumentPaths(string? template)
    {
        var args = ProcessJobExecutor.BuildArguments(
            "job.etlsql",
            "session-1",
            template,
            new Dictionary<string, string>
            {
                ["@start_date"] = "2026-08-01",
                ["region"] = "North America"
            });

        var first = args.IndexOf("--var");
        Assert.True(first >= 0);
        Assert.Equal("@start_date=2026-08-01", args[first + 1]);
        var second = args.IndexOf("--var", first + 1);
        Assert.True(second > first);
        Assert.Equal("@region=North America", args[second + 1]);
        Assert.Equal(2, args.Count(arg => arg == "--var"));
    }

    [Fact]
    public void DefaultResumePassesSessionAndResumeFlag()
    {
        var args = ProcessJobExecutor.BuildArguments(
            "job.etlsql", "session-42", argumentsTemplate: null, resume: true);

        Assert.Equal(
            ["run", "job.etlsql", "--json", "--session", "session-42", "--resume"],
            args);
    }

    [Fact]
    public void CustomTemplateResumeAppendsMissingSessionContract()
    {
        var args = ProcessJobExecutor.BuildArguments(
            "job.etlsql", "session-42", "run {ScriptFile} --json", resume: true);

        Assert.Equal(
            ["run", "job.etlsql", "--json", "--session", "session-42", "--resume"],
            args);
    }

    [Fact]
    public void CustomTemplateResumeDoesNotDuplicateSessionOption()
    {
        var args = ProcessJobExecutor.BuildArguments(
            "job.etlsql", "session-42", "run {ScriptFile} --json --session {SessionId}", resume: true);

        Assert.Equal(1, args.Count(argument => argument == "--session"));
        Assert.Equal("--resume", args[^1]);
    }
}
