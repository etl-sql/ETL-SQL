using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Engine.Storage;
using Xunit;

namespace ETL_SQL.Tests.Analysis
{
    /// <summary>
    /// A hop is observed twice — once by static analysis at parse time, once by the engine as it
    /// executes — and the two observations know different things. Collapsing them for display must
    /// keep both halves, or the chain silently loses whichever half the loser carried.
    /// </summary>
    public class LineageChainFidelityTests
    {
        private static LineageEntry Analyzed(string column, TransformationKind kind, string? expression = null) =>
            new("#stage", "SELECT INTO")
            {
                TargetColumn = column,
                SourceTables = ["pats.FILE"],
                SourceTablesPhysical = [null],
                SourceColumns = ["raw"],
                TransformationKind = kind,
                TransformationExpression = expression,
                Line = 12
            };

        private static LineageEntry Executed(string column, string physical) =>
            new("#stage", "SELECT INTO")
            {
                TargetColumn = column,
                SourceTables = ["pats.FILE"],
                SourceTablesPhysical = [physical],
                SourceColumns = ["raw"],
                TransformationKind = TransformationKind.Unknown,
                Line = 40
            };

        [Fact]
        public void MergingKeepsTheTransformationTheAnalyzerSawAndThePathTheEngineResolved()
        {
            var merged = LineageDataSource.MergeObservations(
            [
                Analyzed("full_name", TransformationKind.StringOperation, "a + ' ' + b"),
                Executed("full_name", @"FLATFILE C:\tmp\patients.csv")
            ]);

            Assert.Equal(TransformationKind.StringOperation, merged.TransformationKind);
            Assert.Equal("a + ' ' + b", merged.TransformationExpression);
            Assert.Equal(@"FLATFILE C:\tmp\patients.csv", merged.SourceTablesPhysical.Single());
        }

        /// <summary>Order of observation must not change the result.</summary>
        [Fact]
        public void MergingIsIndifferentToObservationOrder()
        {
            var forward = LineageDataSource.MergeObservations(
                [Analyzed("c", TransformationKind.Cast), Executed("c", "FLATFILE f.csv")]);
            var reverse = LineageDataSource.MergeObservations(
                [Executed("c", "FLATFILE f.csv"), Analyzed("c", TransformationKind.Cast)]);

            Assert.Equal(forward.TransformationKind, reverse.TransformationKind);
            Assert.Equal(forward.SourceTablesPhysical, reverse.SourceTablesPhysical);
        }

        /// <summary>
        /// The entries belong to the tracker and are what hover and export read. Folding two of them
        /// together to render a chain must not rewrite the session's recorded lineage.
        /// </summary>
        [Fact]
        public void MergingDoesNotMutateTheRecordedEntries()
        {
            var analyzed = Analyzed("full_name", TransformationKind.StringOperation);
            var executed = Executed("full_name", "FLATFILE f.csv");

            LineageDataSource.MergeObservations([analyzed, executed]);

            Assert.Null(analyzed.SourceTablesPhysical.Single());
            Assert.Equal(TransformationKind.Unknown, executed.TransformationKind);
        }

        [Fact]
        public void MergingKeepsTheEarlierSourcePositionSoTheChainPointsAtTheWrite()
        {
            var merged = LineageDataSource.MergeObservations(
                [Executed("c", "FLATFILE f.csv"), Analyzed("c", TransformationKind.Cast)]);

            Assert.Equal(12, merged.Line);
        }

        [Fact]
        public void MergingUnionsTheTagsFromBothObservations()
        {
            var analyzed = Analyzed("c", TransformationKind.Cast);
            analyzed.Metadata["d"] = "described";
            var executed = Executed("c", "FLATFILE f.csv");
            executed.Metadata["owner"] = "Finance";

            var merged = LineageDataSource.MergeObservations([analyzed, executed]);

            Assert.Equal("described", merged.Metadata["d"]);
            Assert.Equal("Finance", merged.Metadata["owner"]);
        }

        [Fact]
        public void ASingleObservationIsCarriedThrough()
        {
            var only = Analyzed("c", TransformationKind.Aggregation);

            Assert.Same(only, LineageDataSource.MergeObservations([only]));
        }

        /// <summary>
        /// Static analysis records a hop before any connection is open, so its entry has no physical
        /// identifier. When the engine re-records the same hop it must supply one, or a column whose
        /// two observations collapse into a single tracker entry ends up as the only column in the
        /// chain with an unresolved source.
        /// </summary>
        [Fact]
        public void ReRecordingAHopResolvesThePhysicalIdentifierTheFirstObservationLacked()
        {
            var tracker = new LineageTracker(NullLogger.Instance);
            tracker.Record("#stage", ["pats"], "SELECT INTO", targetColumn: "id",
                sourceColumns: ["id"], line: 3, column: 5);

            tracker.ConnectionResolver = _ => new LineageSourceDescriptor(
                ConnectorType: "FLATFILE", FilePath: @"C:\tmp\patients.csv");
            tracker.Record("#stage", ["pats"], "SELECT INTO", targetColumn: "id",
                sourceColumns: ["id"], line: 3, column: 5,
                transformationKind: TransformationKind.Cast);

            var entry = tracker.GetFullLineage().Single(e => e.TargetColumn == "id");
            Assert.Equal(TransformationKind.Cast, entry.TransformationKind);
            Assert.Contains("patients.csv", entry.SourceTablesPhysical.Single());
        }
    }
}
