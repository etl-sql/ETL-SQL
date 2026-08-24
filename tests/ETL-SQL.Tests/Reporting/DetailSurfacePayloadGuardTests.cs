using System;
using System.Collections.Generic;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Reporting;
using ETL_SQL.Reporting;
using Xunit;

namespace ETL_SQL.Tests.Reporting
{
    /// <summary>
    /// Payload budget for detail surfaces. Unlike the structural budgets this one depends on
    /// the rows a detail visual actually returned, so it is enforced once the manifest is
    /// complete. Asserted at limit-1, limit, and limit+1 like every other numeric boundary.
    /// </summary>
    public class DetailSurfacePayloadGuardTests
    {
        /// <summary>
        /// Builds a manifest whose single detail surface serializes to exactly
        /// <paramref name="targetBytes"/>, by padding one cell in the detail visual.
        /// </summary>
        private static ReportManifest ManifestOfSize(int targetBytes)
        {
            // Each padding character is one ASCII byte inside a JSON string, so a single
            // correction step lands exactly on the target.
            int padding = 0;
            ReportManifest manifest = Build(padding);
            int measured = DetailSurfacePayloadGuard.Measure(manifest.Visuals[1].Tooltip!, manifest);

            padding = targetBytes - measured;
            Assert.True(padding >= 0, $"base payload {measured} already exceeds target {targetBytes}");

            manifest = Build(padding);
            Assert.Equal(targetBytes, DetailSurfacePayloadGuard.Measure(manifest.Visuals[1].Tooltip!, manifest));
            return manifest;
        }

        private static ReportManifest Build(int padding) => new()
        {
            Title = "Sales",
            Visuals =
            [
                new VisualManifest
                {
                    Name = "MonthDetail",
                    VisualType = "BAR",
                    Columns = ["Region"],
                    Rows = [[new string('x', padding)]]
                },
                new VisualManifest
                {
                    Name = "BarWithTooltip",
                    VisualType = "BAR",
                    Columns = ["Month"],
                    Rows = [["January"]],
                    Tooltip = new TooltipManifest
                    {
                        Type = "container",
                        Mode = TooltipManifest.PopoverMode,
                        ContainerRef = "TooltipBox",
                        ResolvedVisuals = ["MonthDetail"]
                    }
                }
            ],
            Containers =
            [
                new ContainerManifest { Name = "TooltipBox", ContainerType = "BOX" }
            ]
        };

        [Theory]
        [Trait("Category", "Smoke.Reporting")]
        [InlineData(-1, false)]
        [InlineData(0, false)]
        [InlineData(1, true)]
        public void PayloadBytes_BoundaryIsEnforced(int delta, bool shouldFail)
        {
            var manifest = ManifestOfSize(DetailSurfaceLimits.MaxManifestBytes + delta);

            if (!shouldFail)
            {
                DetailSurfacePayloadGuard.Enforce(manifest); // must not throw
                return;
            }

            var error = Assert.Throws<ExecutionException>(() => DetailSurfacePayloadGuard.Enforce(manifest));
            Assert.Contains(DetailSurfaceDiagnostics.ManifestBytesExceeded, error.Message);
            Assert.Contains("BarWithTooltip", error.Message);
            Assert.Contains("@hover_value", error.Message);
        }

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void Measure_CountsTheRenderedVisualNotJustTheTooltipStub()
        {
            var small = Build(0);
            var large = Build(4096);

            int smallBytes = DetailSurfacePayloadGuard.Measure(small.Visuals[1].Tooltip!, small);
            int largeBytes = DetailSurfacePayloadGuard.Measure(large.Visuals[1].Tooltip!, large);

            // The cost of a popover is what it renders, not the reference to it.
            Assert.Equal(4096, largeBytes - smallBytes);
        }

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void VisualWithoutADetailSurface_IsNotMeasured()
        {
            var manifest = new ReportManifest
            {
                Title = "Sales",
                Visuals =
                [
                    new VisualManifest
                    {
                        Name = "Plain",
                        VisualType = "BAR",
                        Columns = ["Month"],
                        Rows = [[new string('x', DetailSurfaceLimits.MaxManifestBytes * 2)]]
                    }
                ]
            };

            DetailSurfacePayloadGuard.Enforce(manifest); // a big visual is fine; only detail is budgeted
        }
    }
}
