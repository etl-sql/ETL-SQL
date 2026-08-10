using System;
using ETL_SQL.Engine;
using Xunit;

namespace ETL_SQL.Tests
{
    /// <summary>
    /// Verifies RELDATE W/WS/WE resolution for all seven possible week-start days
    /// using representative calendar dates from the spec.
    /// </summary>
    public class WeekStartArithmeticTests
    {
        // Spec example: WEEK_START_DAY = Wednesday, today = Thursday April 17
        // (Using 2025-04-17 Thursday)
        private static readonly DateTime Thursday = new(2025, 4, 17);

        [Fact]
        public void W_Wednesday_Start_TodayIsThursday_ReturnsMostRecentWednesday()
        {
            // Most recent Wednesday = April 16
            var result = RelDateResolver.Resolve("W", DayOfWeek.Wednesday, Thursday);
            Assert.Equal(new DateTime(2025, 4, 16), result);
        }

        [Fact]
        public void W_Wednesday_MinusOne_ReturnsPreviousWednesday()
        {
            // April 16 - 7 = April 9
            var result = RelDateResolver.Resolve("W-1", DayOfWeek.Wednesday, Thursday);
            Assert.Equal(new DateTime(2025, 4, 9), result);
        }

        [Fact]
        public void WE_Wednesday_Start_ReturnsTuesdayOfThisWeek()
        {
            // WE = W + 6 = April 16 + 6 = April 22 (Tuesday)
            var result = RelDateResolver.Resolve("WE", DayOfWeek.Wednesday, Thursday);
            Assert.Equal(new DateTime(2025, 4, 22), result);
        }

        [Fact]
        public void WE_Wednesday_MinusOne_ReturnsPreviousWeekEnd()
        {
            // WE-1 = April 22 - 7 = April 15 (Tuesday)
            var result = RelDateResolver.Resolve("WE-1", DayOfWeek.Wednesday, Thursday);
            Assert.Equal(new DateTime(2025, 4, 15), result);
        }

        // ── When today IS the week start day ──────────────────────────────────

        [Fact]
        public void W_TodayIsStartDay_ReturnsTodayMidnight()
        {
            // Monday start, today = Monday April 14.
            var monday = new DateTime(2025, 4, 14);
            var result = RelDateResolver.Resolve("W", DayOfWeek.Monday, monday);
            Assert.Equal(monday, result);
        }

        // ── All seven start days for a fixed reference date ───────────────────
        // Ref = Monday April 14, 2025.

        private static readonly DateTime RefMonday = new(2025, 4, 14);

        [Theory]
        [InlineData(DayOfWeek.Monday, 2025, 4, 14)]   // April 14 IS Monday
        [InlineData(DayOfWeek.Tuesday, 2025, 4, 8)]   // Most recent Tuesday before/on April 14 = April 8
        [InlineData(DayOfWeek.Wednesday, 2025, 4, 9)]   // April 9
        [InlineData(DayOfWeek.Thursday, 2025, 4, 10)]   // April 10
        [InlineData(DayOfWeek.Friday, 2025, 4, 11)]   // April 11
        [InlineData(DayOfWeek.Saturday, 2025, 4, 12)]   // April 12
        [InlineData(DayOfWeek.Sunday, 2025, 4, 13)]   // April 13
        public void W_AllStartDays_ReferenceMonday(DayOfWeek startDay, int y, int m, int d)
        {
            var result = RelDateResolver.Resolve("W", startDay, RefMonday);
            Assert.Equal(new DateTime(y, m, d), result);
        }

        [Theory]
        [InlineData(DayOfWeek.Monday, 2025, 4, 20)]   // Monday April 14 + 6 = April 20 (Sunday)
        [InlineData(DayOfWeek.Tuesday, 2025, 4, 14)]   // April 8 + 6 = April 14 (Monday)
        [InlineData(DayOfWeek.Wednesday, 2025, 4, 15)]   // April 9 + 6 = April 15 (Tuesday)
        [InlineData(DayOfWeek.Thursday, 2025, 4, 16)]   // April 10 + 6 = April 16 (Wednesday)
        [InlineData(DayOfWeek.Friday, 2025, 4, 17)]   // April 11 + 6 = April 17 (Thursday)
        [InlineData(DayOfWeek.Saturday, 2025, 4, 18)]   // April 12 + 6 = April 18 (Friday)
        [InlineData(DayOfWeek.Sunday, 2025, 4, 19)]   // April 13 + 6 = April 19 (Saturday)
        public void WE_AllStartDays_ReferenceMonday(DayOfWeek startDay, int y, int m, int d)
        {
            var result = RelDateResolver.Resolve("WE", startDay, RefMonday);
            Assert.Equal(new DateTime(y, m, d), result);
        }

        // ── W-1 for all start days ─────────────────────────────────────────────

        [Theory]
        [InlineData(DayOfWeek.Monday, 2025, 4, 7)]
        [InlineData(DayOfWeek.Tuesday, 2025, 4, 1)]
        [InlineData(DayOfWeek.Wednesday, 2025, 4, 2)]
        [InlineData(DayOfWeek.Thursday, 2025, 4, 3)]
        [InlineData(DayOfWeek.Friday, 2025, 4, 4)]
        [InlineData(DayOfWeek.Saturday, 2025, 4, 5)]
        [InlineData(DayOfWeek.Sunday, 2025, 4, 6)]
        public void W_MinusOne_AllStartDays_ReferenceMonday(DayOfWeek startDay, int y, int m, int d)
        {
            var result = RelDateResolver.Resolve("W-1", startDay, RefMonday);
            Assert.Equal(new DateTime(y, m, d), result);
        }

        // ── WE-1 for all start days ────────────────────────────────────────────

        [Theory]
        [InlineData(DayOfWeek.Monday, 2025, 4, 13)]
        [InlineData(DayOfWeek.Tuesday, 2025, 4, 7)]
        [InlineData(DayOfWeek.Wednesday, 2025, 4, 8)]
        [InlineData(DayOfWeek.Thursday, 2025, 4, 9)]
        [InlineData(DayOfWeek.Friday, 2025, 4, 10)]
        [InlineData(DayOfWeek.Saturday, 2025, 4, 11)]
        [InlineData(DayOfWeek.Sunday, 2025, 4, 12)]
        public void WE_MinusOne_AllStartDays_ReferenceMonday(DayOfWeek startDay, int y, int m, int d)
        {
            var result = RelDateResolver.Resolve("WE-1", startDay, RefMonday);
            Assert.Equal(new DateTime(y, m, d), result);
        }
    }
}
