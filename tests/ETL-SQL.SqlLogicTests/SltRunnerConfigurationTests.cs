using System;
using Xunit;

namespace ETL_SQL.SqlLogicTests
{
    /// <summary>
    /// The corpus runner must honour engine configuration.
    ///
    /// <para><see cref="SltRunner"/> builds its own service collection rather than using the
    /// production composition root, and for a long time it registered no <c>IConfiguration</c>.
    /// <c>DefaultThresholds</c> takes a nullable config and falls back to built-in constants, so
    /// every <c>Engine__*</c> override the lane set was accepted, ignored, and reported nothing —
    /// a run asked to spill at 25 rows executed at 1,000,000 and touched no spill path at all.</para>
    ///
    /// <para>That is invisible from the outside: the corpus still passes, just against a
    /// configuration nobody chose. This asserts the wiring rather than the behaviour, because the
    /// behaviour of an ignored setting is indistinguishable from a setting that had no effect.</para>
    /// </summary>
    public class SltRunnerConfigurationTests
    {
        [Fact]
        public void EngineEnvironmentOverrides_ReachTheEvaluator()
        {
            const string variable = "Engine__TempTableSpillThresholdRows";
            var original = Environment.GetEnvironmentVariable(variable);

            try
            {
                Environment.SetEnvironmentVariable(variable, "25");

                using var runner = new SltRunner();

                Assert.True(runner.TempTableSpillThresholdRows == 25,
                    $"{variable}=25 was set, but the runner resolved "
                    + $"{runner.TempTableSpillThresholdRows}. The evaluator is not seeing "
                    + "IConfiguration, so every engine threshold the lane configures is silently "
                    + "the built-in default and the corpus exercises paths nobody selected.");
            }
            finally
            {
                Environment.SetEnvironmentVariable(variable, original);
            }
        }

        [Fact]
        public void WithNoOverride_TheRunnerUsesTheShippedDefault()
        {
            const string variable = "Engine__TempTableSpillThresholdRows";
            var original = Environment.GetEnvironmentVariable(variable);

            try
            {
                Environment.SetEnvironmentVariable(variable, null);

                using var runner = new SltRunner();

                // Guards the other direction: reading configuration must not itself change the
                // baseline the corpus has always run at.
                Assert.True(runner.TempTableSpillThresholdRows == 1_000_000,
                    "Unconfigured, the corpus should still run at the shipped default of 1,000,000 "
                    + $"rows; it resolved {runner.TempTableSpillThresholdRows}.");
            }
            finally
            {
                Environment.SetEnvironmentVariable(variable, original);
            }
        }
    }
}
