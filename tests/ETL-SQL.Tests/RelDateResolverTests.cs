using System;
using ETL_SQL.Engine;
using ETL_SQL.Core.Common.Exceptions;
using Xunit;

namespace ETL_SQL.Tests
{
    public class RelDateResolverTests
    {
        private static readonly DayOfWeek Monday = DayOfWeek.Monday;

        // Reference point: Wednesday April 16, 2025 09:30:00 local
        private static readonly DateTime Ref = new(2025, 4, 16, 9, 30, 0);

        // ── Fixed date passthrough ──────────────────────────────────────────────

        [Fact]
        public void FixedDate_IsoString_ReturnsParsed()
        {
            var result = RelDateResolver.Resolve("2026-12-31", Monday, Ref);
            Assert.Equal(new DateTime(2026, 12, 31), result);
        }

        [Fact]
        public void FixedDate_WithTime_ReturnsParsed()
        {
            var result = RelDateResolver.Resolve("2026-01-15 08:00:00", Monday, Ref);
            Assert.Equal(new DateTime(2026, 1, 15, 8, 0, 0), result);
        }

        [Fact]
        public void FixedDate_Invalid_Throws()
        {
            Assert.Throws<ExecutionException>(() => RelDateResolver.Resolve("2026-99-99", Monday, Ref));
        }

        // ── D anchor ───────────────────────────────────────────────────────────

        [Fact]
        public void D_NoShift_ReturnsTodayMidnight()
        {
            var result = RelDateResolver.Resolve("D", Monday, Ref);
            Assert.Equal(Ref.Date, result);
        }

        [Fact]
        public void D_MinusOne_ReturnsYesterdayMidnight()
        {
            var result = RelDateResolver.Resolve("D-1", Monday, Ref);
            Assert.Equal(Ref.Date.AddDays(-1), result);
        }

        [Fact]
        public void D_PlusFive_ReturnsFiveDaysAhead()
        {
            var result = RelDateResolver.Resolve("D+5", Monday, Ref);
            Assert.Equal(Ref.Date.AddDays(5), result);
        }

        // ── N anchor (local) ───────────────────────────────────────────────────

        [Fact]
        public void N_NoShift_ReturnsNowExact()
        {
            var result = RelDateResolver.Resolve("N", Monday, Ref);
            Assert.Equal(Ref, result);
        }

        [Fact]
        public void N_MinusTwoH_SubtractsTwoHours()
        {
            var result = RelDateResolver.Resolve("N-2H", Monday, Ref);
            Assert.Equal(Ref.AddHours(-2), result);
        }

        [Fact]
        public void N_MinusThirtyI_SubtractThirtyMinutes()
        {
            var result = RelDateResolver.Resolve("N-30I", Monday, Ref);
            Assert.Equal(Ref.AddMinutes(-30), result);
        }

        [Fact]
        public void N_MinusFortyFiveS_SubtractsFortyFiveSeconds()
        {
            var result = RelDateResolver.Resolve("N-45S", Monday, Ref);
            Assert.Equal(Ref.AddSeconds(-45), result);
        }

        [Fact]
        public void N_PlusTwoH_AddsTwoHours()
        {
            var result = RelDateResolver.Resolve("N+2H", Monday, Ref);
            Assert.Equal(Ref.AddHours(2), result);
        }

        [Fact]
        public void N_ArithmeticNoUnit_Throws()
        {
            Assert.Throws<ExecutionException>(() => RelDateResolver.Resolve("N-2", Monday, Ref));
        }

        [Fact]
        public void N_InvalidUnit_Throws()
        {
            Assert.Throws<ExecutionException>(() => RelDateResolver.Resolve("N-2D", Monday, Ref));
        }

        // ── NU anchor (UTC) ────────────────────────────────────────────────────

        [Fact]
        public void NU_NoShift_ReturnsNowAsProvided()
        {
            var result = RelDateResolver.Resolve("NU", Monday, Ref);
            Assert.Equal(Ref, result);
        }

        [Fact]
        public void NU_MinusOneH_SubtractsOneHour()
        {
            var result = RelDateResolver.Resolve("NU-1H", Monday, Ref);
            Assert.Equal(Ref.AddHours(-1), result);
        }

        // ── W anchor (week) ────────────────────────────────────────────────────
        // Ref = Wednesday April 16, 2025. With Monday as weekStart:
        // W = Monday April 14.

        [Fact]
        public void W_MondayStart_ReturnsThisWeekMonday()
        {
            var result = RelDateResolver.Resolve("W", Monday, Ref);
            Assert.Equal(new DateTime(2025, 4, 14), result);
        }

        [Fact]
        public void WS_AliasSameAsW()
        {
            Assert.Equal(
                RelDateResolver.Resolve("W", Monday, Ref),
                RelDateResolver.Resolve("WS", Monday, Ref));
        }

        [Fact]
        public void W_MinusOne_ReturnsPreviousWeekMonday()
        {
            var result = RelDateResolver.Resolve("W-1", Monday, Ref);
            Assert.Equal(new DateTime(2025, 4, 7), result);
        }

        [Fact]
        public void WE_MondayStart_ReturnsSunday()
        {
            // WE = W + 6 = Monday April 14 + 6 = Sunday April 20
            var result = RelDateResolver.Resolve("WE", Monday, Ref);
            Assert.Equal(new DateTime(2025, 4, 20), result);
        }

        [Fact]
        public void WE_MinusOne_ReturnsPreviousWeekSunday()
        {
            // WE-1 = previous week WE = April 20 - 7 = April 13
            var result = RelDateResolver.Resolve("WE-1", Monday, Ref);
            Assert.Equal(new DateTime(2025, 4, 13), result);
        }

        // ── M anchor (month) ───────────────────────────────────────────────────
        // Ref = April 16, 2025.

        [Fact]
        public void M_NoShift_ReturnsFirstOfMonth()
        {
            var result = RelDateResolver.Resolve("M", Monday, Ref);
            Assert.Equal(new DateTime(2025, 4, 1), result);
        }

        [Fact]
        public void MS_AliasSameAsM()
        {
            Assert.Equal(
                RelDateResolver.Resolve("M", Monday, Ref),
                RelDateResolver.Resolve("MS", Monday, Ref));
        }

        [Fact]
        public void M_MinusOne_ReturnsFirstOfPreviousMonth()
        {
            var result = RelDateResolver.Resolve("M-1", Monday, Ref);
            Assert.Equal(new DateTime(2025, 3, 1), result);
        }

        [Fact]
        public void ME_NoShift_ReturnsLastDayOfCurrentMonth()
        {
            var result = RelDateResolver.Resolve("ME", Monday, Ref);
            Assert.Equal(new DateTime(2025, 4, 30), result);
        }

        [Fact]
        public void ME_MinusOne_ReturnsLastDayOfPreviousMonth()
        {
            // Period shift: March. Last day of March = March 31.
            var result = RelDateResolver.Resolve("ME-1", Monday, Ref);
            Assert.Equal(new DateTime(2025, 3, 31), result);
        }

        [Fact]
        public void ME_February_HandlesLeapYear()
        {
            // Feb 2024 is a leap year.
            var feb2024 = new DateTime(2024, 2, 15);
            var result = RelDateResolver.Resolve("ME", Monday, feb2024);
            Assert.Equal(new DateTime(2024, 2, 29), result);
        }

        // ── Q anchor (quarter) ─────────────────────────────────────────────────
        // Ref = April 16, 2025 = Q2.

        [Fact]
        public void Q_NoShift_ReturnsQ2Start()
        {
            var result = RelDateResolver.Resolve("Q", Monday, Ref);
            Assert.Equal(new DateTime(2025, 4, 1), result);
        }

        [Fact]
        public void QS_AliasSameAsQ()
        {
            Assert.Equal(
                RelDateResolver.Resolve("Q", Monday, Ref),
                RelDateResolver.Resolve("QS", Monday, Ref));
        }

        [Fact]
        public void Q_MinusOne_ReturnsQ1Start()
        {
            var result = RelDateResolver.Resolve("Q-1", Monday, Ref);
            Assert.Equal(new DateTime(2025, 1, 1), result);
        }

        [Fact]
        public void QE_NoShift_ReturnsLastDayOfQ2()
        {
            // Q2 ends June 30.
            var result = RelDateResolver.Resolve("QE", Monday, Ref);
            Assert.Equal(new DateTime(2025, 6, 30), result);
        }

        [Fact]
        public void QE_MinusOne_ReturnsLastDayOfQ1()
        {
            // Q1 ends March 31.
            var result = RelDateResolver.Resolve("QE-1", Monday, Ref);
            Assert.Equal(new DateTime(2025, 3, 31), result);
        }

        // ── Y anchor (year) ────────────────────────────────────────────────────

        [Fact]
        public void Y_NoShift_ReturnsJan1()
        {
            var result = RelDateResolver.Resolve("Y", Monday, Ref);
            Assert.Equal(new DateTime(2025, 1, 1), result);
        }

        [Fact]
        public void YS_AliasSameAsY()
        {
            Assert.Equal(
                RelDateResolver.Resolve("Y", Monday, Ref),
                RelDateResolver.Resolve("YS", Monday, Ref));
        }

        [Fact]
        public void Y_MinusOne_ReturnsPreviousYearJan1()
        {
            var result = RelDateResolver.Resolve("Y-1", Monday, Ref);
            Assert.Equal(new DateTime(2024, 1, 1), result);
        }

        [Fact]
        public void YE_NoShift_ReturnsDec31()
        {
            var result = RelDateResolver.Resolve("YE", Monday, Ref);
            Assert.Equal(new DateTime(2025, 12, 31), result);
        }

        [Fact]
        public void YE_MinusTwo_ReturnsDec31TwoYearsAgo()
        {
            var result = RelDateResolver.Resolve("YE-2", Monday, Ref);
            Assert.Equal(new DateTime(2023, 12, 31), result);
        }

        // ── Period-shift rule ──────────────────────────────────────────────────

        [Fact]
        public void PeriodShiftRule_ME_MinusOne_IsLastDayOfPreviousMonth_NotThisMonthMinusOneDay()
        {
            // ME-1 must be last day of (this month - 1), NOT (last day of this month - 1 day).
            // April last day = April 30. April 30 - 1 = April 29 ← WRONG.
            // Correct: last day of March = March 31.
            var result = RelDateResolver.Resolve("ME-1", Monday, Ref);
            Assert.NotEqual(new DateTime(2025, 4, 29), result);
            Assert.Equal(new DateTime(2025, 3, 31), result);
        }

        // ── Error paths ────────────────────────────────────────────────────────

        [Fact]
        public void UnknownAnchor_Throws()
        {
            Assert.Throws<ExecutionException>(() => RelDateResolver.Resolve("X-1", Monday, Ref));
        }

        [Fact]
        public void Empty_Throws()
        {
            Assert.Throws<ExecutionException>(() => RelDateResolver.Resolve("", Monday, Ref));
        }

        [Fact]
        public void Whitespace_Throws()
        {
            Assert.Throws<ExecutionException>(() => RelDateResolver.Resolve("   ", Monday, Ref));
        }

        [Fact]
        public void SignWithoutMagnitude_Throws()
        {
            Assert.Throws<ExecutionException>(() => RelDateResolver.Resolve("D-", Monday, Ref));
        }

        [Fact]
        public void TrailingGarbage_Throws()
        {
            Assert.Throws<ExecutionException>(() => RelDateResolver.Resolve("D-1X", Monday, Ref));
        }

        [Fact]
        public void NonNAnchorWithUnit_Throws()
        {
            Assert.Throws<ExecutionException>(() => RelDateResolver.Resolve("D-1H", Monday, Ref));
        }

        [Fact]
        public void InvalidNUnit_Throws()
        {
            Assert.Throws<ExecutionException>(() => RelDateResolver.Resolve("N-2X", Monday, Ref));
        }
    }
}
