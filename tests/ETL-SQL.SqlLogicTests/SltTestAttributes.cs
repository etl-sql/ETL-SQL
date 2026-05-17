using System;
using Xunit;

namespace ETL_SQL.SqlLogicTests
{
    internal static class SltRunGate
    {
        private const string EnableVariable = "ETL_SQL_RUN_SLT";
        private const string SkipReason = "SLT tests are deployment-only. Set ETL_SQL_RUN_SLT=1 to run them explicitly.";

        public static bool IsEnabled
        {
            get
            {
                var value = Environment.GetEnvironmentVariable(EnableVariable);
                return value != null &&
                    (value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                     value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                     value.Equals("yes", StringComparison.OrdinalIgnoreCase));
            }
        }

        public static string? SkipIfDisabled => IsEnabled ? null : SkipReason;
    }

    public sealed class SltFactAttribute : FactAttribute
    {
        public SltFactAttribute()
        {
            Skip = SltRunGate.SkipIfDisabled;
        }
    }

    public sealed class SltTheoryAttribute : TheoryAttribute
    {
        public SltTheoryAttribute()
        {
            Skip = SltRunGate.SkipIfDisabled;
        }
    }
}
