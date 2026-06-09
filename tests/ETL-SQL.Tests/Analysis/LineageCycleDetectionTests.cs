using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using ETL_SQL.Core;
using ETL_SQL.Common;

namespace ETL_SQL.Tests.Analysis
{
    /// <summary>
    /// Verifies that <see cref="LineageTracker"/> surfaces a warning when ancestor traversal
    /// encounters a genuine cycle, and does NOT false-positive on re-convergent (diamond) lineage.
    /// </summary>
    public class LineageCycleDetectionTests
    {
        private sealed class CapturingLogger : ILogger
        {
            public List<string> Warnings { get; } = new();
            public string? SessionId { get; set; }
            public bool IsDebugEnabled => false;
            public bool IsVerboseEnabled => false;
            public bool IsVerbose { get; set; }
            public bool SuppressConsole { get; set; }
            public bool IsJsonMode { get; set; }
            public event Action<string, string?, ConsoleColor>? OnMessage;

            public void Log(LogLevel level, string message, Exception? ex = null)
            {
                if (level == LogLevel.Warning) Warnings.Add(message);
                OnMessage?.Invoke(message, SessionId, ConsoleColor.White);
            }
        }

        [Fact]
        public void GenuineCycle_IsDetectedAndWarned()
        {
            var logger = new CapturingLogger();
            var tracker = new LineageTracker(logger);

            // A <- B and B <- A  →  cycle
            tracker.Record("A", new[] { "B" }, "SELECT");
            tracker.Record("B", new[] { "A" }, "SELECT");

            var ancestors = tracker.GetAncestors("A").ToList();

            Assert.NotEmpty(tracker.DetectedCycles);
            Assert.Contains(logger.Warnings, w => w.Contains("Lineage cycle detected"));
            // Traversal still terminates and returns the entries it could collect.
            Assert.NotEmpty(ancestors);
        }

        [Fact]
        public void DiamondLineage_IsNotFlaggedAsCycle()
        {
            var logger = new CapturingLogger();
            var tracker = new LineageTracker(logger);

            // D <- B, D <- C, B <- A, C <- A  →  re-convergent, NOT a cycle
            tracker.Record("D", new[] { "B", "C" }, "SELECT");
            tracker.Record("B", new[] { "A" }, "SELECT");
            tracker.Record("C", new[] { "A" }, "SELECT");

            _ = tracker.GetAncestors("D").ToList();

            Assert.Empty(tracker.DetectedCycles);
            Assert.DoesNotContain(logger.Warnings, w => w.Contains("Lineage cycle detected"));
        }

        [Fact]
        public void Cycle_IsWarnedOnlyOnce_AcrossRepeatedQueries()
        {
            var logger = new CapturingLogger();
            var tracker = new LineageTracker(logger);
            tracker.Record("A", new[] { "B" }, "SELECT");
            tracker.Record("B", new[] { "A" }, "SELECT");

            _ = tracker.GetAncestors("A").ToList();
            _ = tracker.GetAncestors("A").ToList();
            _ = tracker.GetAncestors("B").ToList();

            Assert.Single(logger.Warnings, w => w.Contains("Lineage cycle detected involving 'A'"));
        }
    }
}
