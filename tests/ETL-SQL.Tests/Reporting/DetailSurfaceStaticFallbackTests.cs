using System.Collections.Generic;
using ETL_SQL.Reporting;
using Xunit;

namespace ETL_SQL.Tests.Reporting
{
    /// <summary>
    /// Static surfaces (PDF, print, Markdown, email, terminal, plain text, snapshots) may
    /// summarise a detail surface instead of expanding it, but must never imply that hover
    /// is available. These tests pin that contract.
    /// </summary>
    public class DetailSurfaceStaticFallbackTests
    {
        private static VisualManifest VisualWith(TooltipManifest? tooltip) => new()
        {
            Name = "BarWithTooltip",
            VisualType = "BAR",
            Columns = new List<string> { "Month", "Revenue" },
            Rows = new List<List<string?>> { new() { "January", "320" } },
            Tooltip = tooltip
        };

        // ── Wording contract ───────────────────────────────────────────────────

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void NoTooltip_ProducesNoDescription()
        {
            Assert.Null(DetailSurfaceProjection.Describe(null));
        }

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void TransientText_IsReproducedVerbatim()
        {
            var described = DetailSurfaceProjection.Describe(new TooltipManifest
            {
                Type = "text",
                Mode = TooltipManifest.TooltipMode,
                Text = "Revenue for the month"
            });

            Assert.Equal("Detail: Revenue for the month", described);
        }

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void Popover_NamesItsVisuals_WithoutImplyingHover()
        {
            var described = DetailSurfaceProjection.Describe(new TooltipManifest
            {
                Type = "container",
                Mode = TooltipManifest.PopoverMode,
                ContainerRef = "TooltipBox",
                ResolvedVisuals = new List<string> { "MonthDetail" }
            });

            Assert.Equal("Interactive detail available in browser: MonthDetail.", described);
        }

        [Theory]
        [Trait("Category", "Smoke.Reporting")]
        [InlineData("hover")]
        [InlineData("Hover")]
        [InlineData("mouse over")]
        [InlineData("point at")]
        public void StaticDescriptions_NeverClaimHover(string forbidden)
        {
            var popover = DetailSurfaceProjection.Describe(new TooltipManifest
            {
                Type = "container",
                Mode = TooltipManifest.PopoverMode,
                ContainerRef = "TooltipBox",
                ResolvedVisuals = new List<string> { "MonthDetail" }
            })!;
            var text = DetailSurfaceProjection.Describe(new TooltipManifest
            {
                Type = "text",
                Mode = TooltipManifest.TooltipMode,
                Text = "Revenue for the month"
            })!;

            Assert.DoesNotContain(forbidden, popover, System.StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(forbidden, text, System.StringComparison.OrdinalIgnoreCase);
        }

        // ── Version compatibility: manifests published before `mode` existed ────

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void LegacyContainerManifest_WithoutMode_IsTreatedAsPopover()
        {
            // Type is the only signal an older manifest carries.
            var legacy = new TooltipManifest { Type = "container", Mode = null!, ContainerRef = "Box" };
            Assert.True(DetailSurfaceProjection.IsPopover(legacy));
        }

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void LegacyInlineManifest_WithVisuals_IsTreatedAsPopover()
        {
            var legacy = new TooltipManifest
            {
                Type = "inline",
                Mode = null!,
                Visuals = new List<string> { "Trend" }
            };
            Assert.True(DetailSurfaceProjection.IsPopover(legacy));
        }

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void LegacyTextManifest_WithoutMode_IsTreatedAsTooltip()
        {
            var legacy = new TooltipManifest { Type = "text", Mode = null!, Text = "hi" };
            Assert.False(DetailSurfaceProjection.IsPopover(legacy));
        }

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void ResolvedVisuals_PreferredOverAuthoredInlineList()
        {
            // The resolved list follows the container graph; the authored list may be empty.
            var manifest = new TooltipManifest
            {
                Type = "container",
                Mode = TooltipManifest.PopoverMode,
                ContainerRef = "Box",
                ResolvedVisuals = new List<string> { "A", "B" }
            };

            Assert.Equal(new[] { "A", "B" }, DetailSurfaceProjection.ResolvedVisualNames(manifest));
        }

        // ── Markdown / email surface ───────────────────────────────────────────

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void MarkdownExport_DescribesAPopoverInsteadOfExpandingIt()
        {
            var manifest = new ReportManifest
            {
                Title = "Sales",
                Visuals = new List<VisualManifest>
                {
                    VisualWith(new TooltipManifest
                    {
                        Type = "container",
                        Mode = TooltipManifest.PopoverMode,
                        ContainerRef = "TooltipBox",
                        ResolvedVisuals = new List<string> { "MonthDetail" }
                    })
                },
                Pages = new List<PageManifest>
                {
                    new() { Name = "Main", Structure = "A", SlotMap = { ["A"] = "BarWithTooltip" } }
                }
            };

            var markdown = new MarkdownRenderer().Render(manifest);

            Assert.Contains("Interactive detail available in browser: MonthDetail.", markdown);
            Assert.DoesNotContain("hover", markdown, System.StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void MarkdownExport_InlinesTransientTooltipText()
        {
            var manifest = new ReportManifest
            {
                Title = "Sales",
                Visuals = new List<VisualManifest>
                {
                    VisualWith(new TooltipManifest
                    {
                        Type = "text",
                        Mode = TooltipManifest.TooltipMode,
                        Text = "Revenue for the month"
                    })
                },
                Pages = new List<PageManifest>
                {
                    new() { Name = "Main", Structure = "A", SlotMap = { ["A"] = "BarWithTooltip" } }
                }
            };

            var markdown = new MarkdownRenderer().Render(manifest);

            Assert.Contains("Detail: Revenue for the month", markdown);
        }

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void MarkdownExport_WithoutTooltip_AddsNoDetailLine()
        {
            var manifest = new ReportManifest
            {
                Title = "Sales",
                Visuals = new List<VisualManifest> { VisualWith(null) },
                Pages = new List<PageManifest>
                {
                    new() { Name = "Main", Structure = "A", SlotMap = { ["A"] = "BarWithTooltip" } }
                }
            };

            var markdown = new MarkdownRenderer().Render(manifest);

            Assert.DoesNotContain("Interactive detail available", markdown);
            Assert.DoesNotContain("Detail:", markdown);
        }
    }
}
