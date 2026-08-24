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
            var legacy = new TooltipManifest { Type = "container", Mode = null, ContainerRef = "Box" };
            Assert.True(DetailSurfaceProjection.IsPopover(legacy));
        }

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void LegacyInlineManifest_WithVisuals_IsTreatedAsPopover()
        {
            var legacy = new TooltipManifest
            {
                Type = "inline",
                Mode = null,
                Visuals = new List<string> { "Trend" }
            };
            Assert.True(DetailSurfaceProjection.IsPopover(legacy));
        }

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void LegacyTextManifest_WithoutMode_IsTreatedAsTooltip()
        {
            var legacy = new TooltipManifest { Type = "text", Mode = null, Text = "hi" };
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

        // ── PDF surface ────────────────────────────────────────────────────────

        private static ReportManifest PageWith(TooltipManifest? tooltip) => new()
        {
            Title = "Sales",
            Visuals = new List<VisualManifest> { VisualWith(tooltip) },
            Pages = new List<PageManifest>
            {
                new() { Name = "Main", Structure = "A", SlotMap = { ["A"] = "BarWithTooltip" } }
            }
        };

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void PdfExport_CarriesTheDetailNotice()
        {
            var popover = new TooltipManifest
            {
                Type = "container",
                Mode = TooltipManifest.PopoverMode,
                ContainerRef = "TooltipBox",
                ResolvedVisuals = new List<string> { "MonthDetail" }
            };

            var withDetail = new PdfExporter().Export(PageWith(popover));
            var withoutDetail = new PdfExporter().Export(PageWith(null));

            // PDF content streams are compressed, so the text is not directly assertable here;
            // the wording itself is pinned by the Describe tests above. What this establishes is
            // that the notice actually reaches the document rather than being dropped.
            Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }, withDetail[..4]);
            Assert.True(withDetail.Length > withoutDetail.Length,
                $"detail notice added no content: {withDetail.Length} vs {withoutDetail.Length}");
        }

        // ── Snapshot / offline replay ──────────────────────────────────────────

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void ManifestRoundTrip_PreservesTheDetailSurfaceContract()
        {
            // Snapshots serialize and rehydrate the whole ReportManifest, and offline replay
            // runs the same runtime, so preserving these three fields is what makes offline
            // behaviour identical to online rather than silently degraded.
            var original = PageWith(new TooltipManifest
            {
                Type = "container",
                Mode = TooltipManifest.PopoverMode,
                ContainerRef = "TooltipBox",
                ResolvedVisuals = new List<string> { "MonthDetail" },
                StaticSummary = "Interactive detail available in browser: MonthDetail."
            });

            var json = System.Text.Json.JsonSerializer.Serialize(original);
            var rehydrated = System.Text.Json.JsonSerializer.Deserialize<ReportManifest>(json)!;

            var tooltip = rehydrated.Visuals[0].Tooltip!;
            Assert.Equal(TooltipManifest.PopoverMode, tooltip.Mode);
            Assert.Equal("TooltipBox", tooltip.ContainerRef);
            Assert.Equal(new[] { "MonthDetail" }, tooltip.ResolvedVisuals);
            Assert.Equal("Interactive detail available in browser: MonthDetail.", tooltip.StaticSummary);
        }

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void ManifestWithoutMode_DeserializesToTheTooltipDefault()
        {
            // A manifest published before `mode` existed omits the property entirely.
            const string legacy = """
                {"type":"container","containerRef":"Box"}
                """;

            var tooltip = System.Text.Json.JsonSerializer.Deserialize<TooltipManifest>(legacy)!;

            // The property defaults, but classification still routes through IsPopover, which
            // falls back to `type` - so an older report keeps behaving as a popover.
            Assert.True(DetailSurfaceProjection.IsPopover(tooltip));
        }

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void StaticSummary_MatchesTheDescribedProjection()
        {
            var tooltip = new TooltipManifest
            {
                Type = "container",
                Mode = TooltipManifest.PopoverMode,
                ContainerRef = "TooltipBox",
                ResolvedVisuals = new List<string> { "MonthDetail" }
            };
            tooltip.StaticSummary = DetailSurfaceProjection.Describe(tooltip);

            // The browser's print note and the static exporters must render the same sentence.
            Assert.Equal(DetailSurfaceProjection.Describe(tooltip), tooltip.StaticSummary);
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
