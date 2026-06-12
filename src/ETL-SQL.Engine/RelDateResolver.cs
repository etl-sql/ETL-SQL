using System;
using System.Globalization;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Engine
{
    /// <summary>
    /// Stateless resolver for RELDATE expressions. Converts a relative-date string such as
    /// "D-1", "ME-1", "N-2H" into a concrete <see cref="DateTime"/> at execution time.
    /// </summary>
    public static class RelDateResolver
    {
        /// <summary>
        /// Resolves a RELDATE expression to a concrete DateTime.
        /// </summary>
        /// <param name="expression">The RELDATE expression string (e.g. "D-1", "ME-1", "N-30I", "2026-12-31").</param>
        /// <param name="weekStart">The configured start-of-week day (for W/WS/WE anchors).</param>
        /// <param name="now">Override for "now" — defaults to DateTime.Now. Pass a fixed value in tests.</param>
        public static DateTime Resolve(string expression, DayOfWeek weekStart, DateTime? now = null)
        {
            if (string.IsNullOrWhiteSpace(expression))
                throw new ExecutionException("RELDATE expression cannot be empty.");

            var localNow = now ?? DateTime.Now;
            var expr = expression.Trim();

            // Fixed date passthrough: first character is a digit → parse as ISO date and return.
            if (char.IsDigit(expr[0]))
            {
                if (DateTime.TryParse(expr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fixedDate))
                    return fixedDate;
                throw new ExecutionException($"Invalid RELDATE fixed date: '{expression}'.");
            }

            var upper = expr.ToUpperInvariant();
            int pos = 0;
            char baseAnchor;
            bool isEnd = false;
            bool isUtc = false;

            // Parse anchor — longest match first to avoid partial matches (e.g. WE before W).
            if (upper.StartsWith("NU", StringComparison.Ordinal)) { baseAnchor = 'N'; isUtc = true; pos = 2; }
            else if (upper.StartsWith("WE", StringComparison.Ordinal)) { baseAnchor = 'W'; isEnd = true; pos = 2; }
            else if (upper.StartsWith("WS", StringComparison.Ordinal)) { baseAnchor = 'W'; pos = 2; }
            else if (upper.StartsWith("ME", StringComparison.Ordinal)) { baseAnchor = 'M'; isEnd = true; pos = 2; }
            else if (upper.StartsWith("MS", StringComparison.Ordinal)) { baseAnchor = 'M'; pos = 2; }
            else if (upper.StartsWith("QE", StringComparison.Ordinal)) { baseAnchor = 'Q'; isEnd = true; pos = 2; }
            else if (upper.StartsWith("QS", StringComparison.Ordinal)) { baseAnchor = 'Q'; pos = 2; }
            else if (upper.StartsWith("YE", StringComparison.Ordinal)) { baseAnchor = 'Y'; isEnd = true; pos = 2; }
            else if (upper.StartsWith("YS", StringComparison.Ordinal)) { baseAnchor = 'Y'; pos = 2; }
            else if (upper[0] == 'N') { baseAnchor = 'N'; pos = 1; }
            else if (upper[0] == 'W') { baseAnchor = 'W'; pos = 1; }
            else if (upper[0] == 'M') { baseAnchor = 'M'; pos = 1; }
            else if (upper[0] == 'Q') { baseAnchor = 'Q'; pos = 1; }
            else if (upper[0] == 'Y') { baseAnchor = 'Y'; pos = 1; }
            else if (upper[0] == 'D') { baseAnchor = 'D'; pos = 1; }
            else throw new ExecutionException(
                $"Unknown RELDATE anchor in '{expression}'. Valid anchors: D, W/WS/WE, M/MS/ME, Q/QS/QE, Y/YS/YE, N, NU.");

            // Parse optional +/- magnitude [unit]
            int shift = 0;
            char? unit = null;

            if (pos < upper.Length && (upper[pos] == '+' || upper[pos] == '-'))
            {
                int sign = upper[pos] == '-' ? -1 : 1;
                pos++;

                int numStart = pos;
                while (pos < upper.Length && char.IsDigit(upper[pos])) pos++;
                if (pos == numStart)
                    throw new ExecutionException($"Expected numeric magnitude after sign in RELDATE expression '{expression}'.");

                shift = sign * int.Parse(upper.Substring(numStart, pos - numStart), CultureInfo.InvariantCulture);

                if (pos < upper.Length && char.IsLetter(upper[pos]))
                {
                    unit = upper[pos];
                    pos++;
                }
            }

            if (pos < upper.Length)
                throw new ExecutionException($"Unexpected characters '{upper[pos..]}' in RELDATE expression '{expression}'.");

            // Validate units
            if (baseAnchor == 'N')
            {
                if (unit != null && unit != 'H' && unit != 'I' && unit != 'S')
                    throw new ExecutionException(
                        $"Invalid unit '{unit}' for N/NU anchor. Use H (hours), I (minutes), or S (seconds) in '{expression}'.");
                if (shift != 0 && unit == null)
                    throw new ExecutionException(
                        $"N/NU arithmetic requires a time unit (H, I, or S) in '{expression}'.");
            }
            else if (unit != null)
            {
                throw new ExecutionException(
                    $"Unit suffix '{unit}' is only valid for N/NU anchors in '{expression}'.");
            }

            var today = localNow.Date;

            switch (baseAnchor)
            {
                case 'D':
                    return today.AddDays(shift);

                case 'N':
                    {
                        // For NU: use UTC. If now is explicitly provided (tests), use it directly.
                        var refTime = isUtc ? (now.HasValue ? now.Value : DateTime.UtcNow) : localNow;
                        if (shift == 0) return refTime;
                        return unit switch
                        {
                            'H' => refTime.AddHours(shift),
                            'I' => refTime.AddMinutes(shift),
                            'S' => refTime.AddSeconds(shift),
                            _ => refTime  // unreachable — validated above
                        };
                    }

                case 'W':
                    {
                        // Critical rule: shift the period first, then apply start/end.
                        var weekStartDate = GetWeekStart(today, weekStart).AddDays(shift * 7);
                        return isEnd ? weekStartDate.AddDays(6) : weekStartDate;
                    }

                case 'M':
                    {
                        var monthStart = new DateTime(today.Year, today.Month, 1).AddMonths(shift);
                        if (isEnd)
                            return new DateTime(monthStart.Year, monthStart.Month,
                                DateTime.DaysInMonth(monthStart.Year, monthStart.Month));
                        return monthStart;
                    }

                case 'Q':
                    {
                        int qStartMonth = ((today.Month - 1) / 3) * 3 + 1;
                        var qStart = new DateTime(today.Year, qStartMonth, 1).AddMonths(shift * 3);
                        if (isEnd)
                        {
                            int endMonth = qStart.Month + 2;
                            return new DateTime(qStart.Year, endMonth, DateTime.DaysInMonth(qStart.Year, endMonth));
                        }
                        return qStart;
                    }

                case 'Y':
                    {
                        var yearStart = new DateTime(today.Year, 1, 1).AddYears(shift);
                        return isEnd ? new DateTime(yearStart.Year, 12, 31) : yearStart;
                    }

                default:
                    throw new ExecutionException($"Unhandled RELDATE anchor '{baseAnchor}'.");
            }
        }

        private static DateTime GetWeekStart(DateTime date, DayOfWeek firstDay)
        {
            int diff = ((int)date.DayOfWeek - (int)firstDay + 7) % 7;
            return date.AddDays(-diff);
        }
    }
}
