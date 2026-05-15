using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Functions;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Engine.Functions
{
    public static partial class StandardFunctions
    {
        private static void RegisterDateFunctions(IFunctionRegistry registry)
        {
            registry.RegisterWithHelp("DATENAME", DateName, "DATENAME(datepart, date): Returns a string representing the specified date part (e.g. 'January').");
            registry.RegisterWithHelp("DATEPART", DatePart, "DATEPART(datepart, date): Returns an integer representing the specified date part.");
            registry.RegisterWithHelp("DATEDIFF", DateDiff, "DATEDIFF(datepart, start, end): Returns the count of specified datepart boundaries crossed between two dates.");
            registry.RegisterWithHelp("ISDATE", (args, ctx) => EvaluationUtils.SafeTryParseDate(args[0]?.ToString() ?? "", out _) ? 1 : 0, "ISDATE(expr): Returns 1 if the expression is a valid date, 0 otherwise.");
            registry.RegisterWithHelp("EOMONTH", EoMonth, "EOMONTH(date[, months_to_add]): Returns the last day of the month containing the date.");
            
            registry.RegisterWithHelp("YEAR", (args, ctx) => args[0] == null ? null : (EvaluationUtils.TryToDateTime(args[0], out var dt) ? (decimal)dt.Year : null), "YEAR(date): Returns the year part of a date.");
            registry.RegisterWithHelp("MONTH", (args, ctx) => args[0] == null ? null : (EvaluationUtils.TryToDateTime(args[0], out var dt) ? (decimal)dt.Month : null), "MONTH(date): Returns the month part of a date.");
            registry.RegisterWithHelp("DAY", (args, ctx) => args[0] == null ? null : (EvaluationUtils.TryToDateTime(args[0], out var dt) ? (decimal)dt.Day : null), "DAY(date): Returns the day part of a date.");
            
            registry.RegisterWithHelp("GETDATE", (args, ctx) => DateTime.Now, "GETDATE(): Returns the current system date and time.");
            registry.RegisterWithHelp("SYSDATE", (args, ctx) => DateTime.Now, "SYSDATE(): Returns the current system date and time (Oracle style).");
            registry.RegisterWithHelp("NOW", (args, ctx) => DateTime.Now, "NOW(): Alias for GETDATE.");
            registry.RegisterWithHelp("CURRENT_TIMESTAMP", (args, ctx) => DateTime.Now, "CURRENT_TIMESTAMP: Returns the current system date and time.");
            registry.RegisterWithHelp("CURRENT_DATE", (args, ctx) => DateTime.Today, "CURRENT_DATE: Returns the current system date.");
            registry.RegisterWithHelp("CURRENT_TIME", (args, ctx) => DateTime.Now.TimeOfDay, "CURRENT_TIME: Returns the current system time.");
            
            registry.RegisterWithHelp("DATETIMEFROMPARTS", DateTimeFromParts, "DATETIMEFROMPARTS(y, m, d, h, mi, s, ms): Constructs a DATETIME from parts.");
            registry.RegisterWithHelp("TIMEFROMPARTS", TimeFromParts, "TIMEFROMPARTS(h, mi, s, frac, prec): Constructs a TIME from parts.");
            registry.RegisterWithHelp("DATETIMEOFFSETSFROMPARTS", DateTimeOffsetsFromParts, "DATETIMEOFFSETSFROMPARTS(...): Constructs a DATETIMEOFFSET from parts.");
            
            registry.RegisterWithHelp("TRUNC", Trunc, "TRUNC(val[, part]): Truncates a date to the specified part (default 'DAY') or a number to decimals.");
            registry.RegisterWithHelp("TO_DATE", (args, ctx) => args.Count >= 1 ? EvaluationUtils.CastToType(args[0], "DATETIME") : null, "TO_DATE(str[, fmt]): Converts a string to a date.");
            registry.RegisterWithHelp("RELDATE", (args, ctx) => args.Count == 0 ? null : RelDateResolver.Resolve(args[0]?.ToString() ?? "", ctx.WeekStartDay), "RELDATE(expr): Resolves a relative date expression (e.g. 'D-7', 'M-1', 'W-1').");
            registry.RegisterWithHelp("DATEADD", DateAdd, "DATEADD(datepart, number, date): Adds a value to a date.");
        }

        private static object? DateName(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 2 || args[1] == null) return null;
            string part = args[0]?.ToString()?.ToUpperInvariant() ?? "";
            if (!DateTime.TryParse(args[1]?.ToString(), out var dt)) throw new ExecutionException($"Invalid date format for DATENAME: {args[1]}");
            return part switch {
                "MONTH" or "MM" or "M" => dt.ToString("MMMM"),
                "WEEKDAY" or "DW" or "W" => dt.ToString("dddd"),
                "YEAR" or "YY" or "YYYY" => dt.Year.ToString(),
                "QUARTER" or "QQ" or "Q" => "Q" + ((dt.Month - 1) / 3 + 1),
                _ => dt.ToString()
            };
        }

        private static object? DatePart(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 2 || args[1] == null) return null;
            string part = args[0]?.ToString()?.ToUpperInvariant() ?? "";
            if (!DateTime.TryParse(args[1]?.ToString(), out var dt)) throw new ExecutionException($"Invalid date format for DATEPART: {args[1]}");
            return part switch {
                "YEAR" or "YY" or "YYYY" => (decimal)dt.Year,
                "QUARTER" or "QQ" or "Q" => (decimal)((dt.Month - 1) / 3 + 1),
                "MONTH" or "MM" or "M" => (decimal)dt.Month,
                "DAY" or "DD" or "D" => (decimal)dt.Day,
                "HOUR" or "HH" => (decimal)dt.Hour,
                "MINUTE" or "MI" or "N" => (decimal)dt.Minute,
                "SECOND" or "SS" or "S" => (decimal)dt.Second,
                _ => (decimal)0
            };
        }

        private static object? DateDiff(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 3 || args[1] == null || args[2] == null) return null;
            string part = args[0]?.ToString()?.ToUpperInvariant() ?? "";
            if (!EvaluationUtils.TryToDateTime(args[1], out var dt1)) return null;
            if (!EvaluationUtils.TryToDateTime(args[2], out var dt2)) return null;
            
            var diff = dt2 - dt1;
            return part switch {
                "YEAR" or "YY" or "YYYY" => (decimal)(dt2.Year - dt1.Year),
                "QUARTER" or "QQ" or "Q" => (decimal)((dt2.Year - dt1.Year) * 4 + ((dt2.Month - 1) / 3) - ((dt1.Month - 1) / 3)),
                "MONTH" or "MM" or "M" => (decimal)((dt2.Year - dt1.Year) * 12 + dt2.Month - dt1.Month),
                "WEEK" or "WK" or "WW" => (decimal)Math.Truncate((GetStartOfWeek(dt2) - GetStartOfWeek(dt1)).TotalDays / 7),
                "DAY" or "DD" or "D" => (decimal)(dt2.Date - dt1.Date).TotalDays,
                "HOUR" or "HH" => (decimal)(dt2.Date.AddHours(dt2.Hour) - dt1.Date.AddHours(dt1.Hour)).TotalHours,
                "MINUTE" or "MI" or "N" => (decimal)(dt2.Date.AddHours(dt2.Hour).AddMinutes(dt2.Minute) - dt1.Date.AddHours(dt1.Hour).AddMinutes(dt1.Minute)).TotalMinutes,
                "SECOND" or "SS" or "S" => (decimal)(new DateTime(dt2.Year, dt2.Month, dt2.Day, dt2.Hour, dt2.Minute, dt2.Second) - new DateTime(dt1.Year, dt1.Month, dt1.Day, dt1.Hour, dt1.Minute, dt1.Second)).TotalSeconds,
                "MILLISECOND" or "MS" => (decimal)(new DateTime(dt2.Year, dt2.Month, dt2.Day, dt2.Hour, dt2.Minute, dt2.Second, dt2.Millisecond) - new DateTime(dt1.Year, dt1.Month, dt1.Day, dt1.Hour, dt1.Minute, dt1.Second, dt1.Millisecond)).TotalMilliseconds,
                _ => (decimal)0
            };
        }

        private static DateTime GetStartOfWeek(DateTime dt)
        {
            int diff = (7 + (dt.DayOfWeek - DayOfWeek.Sunday)) % 7;
            return dt.Date.AddDays(-1 * diff);
        }

        private static object? EoMonth(List<object?> args, IExecutionContext ctx)
        {
            if (args[0] == null) return null;
            if (!EvaluationUtils.TryToDateTime(args[0], out var dt)) return null;
            var monthsToAdd = args.Count >= 2 && int.TryParse(args[1]?.ToString(), out var m) ? m : 0;
            var target = dt.AddMonths(monthsToAdd);
            var firstOfNextMonth = new DateTime(target.Year, target.Month, 1).AddMonths(1);
            return firstOfNextMonth.AddDays(-1);
        }

        private static object? DateTimeFromParts(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 7) throw new ExecutionException("DATETIMEFROMPARTS requires 7 arguments");
            return new DateTime(Convert.ToInt32(args[0]), Convert.ToInt32(args[1]), Convert.ToInt32(args[2]), Convert.ToInt32(args[3]), Convert.ToInt32(args[4]), Convert.ToInt32(args[5]), Convert.ToInt32(args[6]));
        }

        private static object? TimeFromParts(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 5) throw new ExecutionException("TIMEFROMPARTS requires 5 arguments");
            return new TimeSpan(0, Convert.ToInt32(args[0]), Convert.ToInt32(args[1]), Convert.ToInt32(args[2]), Convert.ToInt32(args[3]));
        }

        private static object? DateTimeOffsetsFromParts(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 10) throw new ExecutionException("DATETIMEOFFSETSFROMPARTS requires 10 arguments");
            return new DateTimeOffset(Convert.ToInt32(args[0]), Convert.ToInt32(args[1]), Convert.ToInt32(args[2]), Convert.ToInt32(args[3]), Convert.ToInt32(args[4]), Convert.ToInt32(args[5]), Convert.ToInt32(args[6]), new TimeSpan(Convert.ToInt32(args[7]), Convert.ToInt32(args[8]), 0));
        }

        private static object? Trunc(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 1 || args[0] == null) return null;

            if (decimal.TryParse(args[0]?.ToString(), out var n))
            {
                int decimals = args.Count >= 2 && int.TryParse(args[1]?.ToString(), out var d) ? d : 0;
                var factor = (decimal)Math.Pow(10, decimals);
                return Math.Truncate(n * factor) / factor;
            }

            if (EvaluationUtils.TryToDateTime(args[0], out var dt))
            {
                string part = args.Count >= 2 ? args[1]?.ToString()?.ToUpperInvariant() ?? "DAY" : "DAY";
                return part switch
                {
                    "YEAR" or "YYYY" or "YY" => new DateTime(dt.Year, 1, 1),
                    "QUARTER" or "QQ" or "Q" => new DateTime(dt.Year, ((dt.Month - 1) / 3) * 3 + 1, 1),
                    "MONTH" or "MM" or "M" => new DateTime(dt.Year, dt.Month, 1),
                    "WEEK" or "WW" or "WK" => GetStartOfWeek(dt),
                    "DAY" or "DD" or "D" => dt.Date,
                    "HOUR" or "HH" => new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, 0, 0),
                    "MINUTE" or "MI" or "N" => new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, 0),
                    "SECOND" or "SS" or "S" => new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second),
                    _ => dt.Date
                };
            }

            return null;
        }

        private static object? DateAdd(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 3 || args[2] == null) return null;
            string part = args[0]?.ToString()?.ToUpperInvariant() ?? "";
            if (!double.TryParse(args[1]?.ToString(), out var val)) return null;
            if (!EvaluationUtils.TryToDateTime(args[2], out var dt)) return null;
            
            return part switch {
                "YEAR" or "YY" or "YYYY" => dt.AddYears((int)val),
                "QUARTER" or "QQ" or "Q" => dt.AddMonths((int)val * 3),
                "MONTH" or "MM" or "M" => dt.AddMonths((int)val),
                "WEEK" or "WK" or "WW" => dt.AddDays(val * 7),
                "DAY" or "DD" or "D" => dt.AddDays(val),
                "HOUR" or "HH" => dt.AddHours(val),
                "MINUTE" or "MI" or "N" => dt.AddMinutes(val),
                "SECOND" or "SS" or "S" => dt.AddSeconds(val),
                "MILLISECOND" or "MS" => dt.AddMilliseconds(val),
                _ => dt
            };
        }
    }
}
