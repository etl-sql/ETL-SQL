using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles SET WEEK_START_DAY = 'Monday' — configures the start-of-week day used by RELDATE W/WS/WE anchors
    /// for the duration of the current script session.
    /// </summary>
    public class SetWeekStartDayHandler(ILogger logger) : IStatementHandler
    {
        private static readonly Dictionary<string, DayOfWeek> DayNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Monday"]    = DayOfWeek.Monday,
            ["Tuesday"]   = DayOfWeek.Tuesday,
            ["Wednesday"] = DayOfWeek.Wednesday,
            ["Thursday"]  = DayOfWeek.Thursday,
            ["Friday"]    = DayOfWeek.Friday,
            ["Saturday"]  = DayOfWeek.Saturday,
            ["Sunday"]    = DayOfWeek.Sunday
        };

        public Type SupportedStatementType => typeof(SetWeekStartDayStatement);

        public Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (SetWeekStartDayStatement)statement;
            if (!DayNames.TryGetValue(stmt.DayName, out var day))
                throw new ExecutionException(
                    $"Invalid WEEK_START_DAY value: '{stmt.DayName}'. Valid values: Monday, Tuesday, Wednesday, Thursday, Friday, Saturday, Sunday.");

            context.WeekStartDay = day;
            logger.WriteLine($"Week start day set to {day}.", ConsoleColor.Cyan);
            return Task.CompletedTask;
        }
    }
}
