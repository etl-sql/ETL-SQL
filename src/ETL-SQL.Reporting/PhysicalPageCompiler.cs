using System;
using System.Collections.Generic;
using System.Linq;

namespace ETL_SQL.Reporting;

public class PhysicalPageCompiler
{
    public List<PhysicalPageModel> Compile(PageManifest page, ReportManifest manifest)
    {
        var result = new List<PhysicalPageModel>();
        var layout = page.PrintLayout ?? new PageLayoutDefinitionManifest
        {
            PageSize = "Letter",
            Orientation = "Portrait",
            MarginTop = 1.0m,
            MarginBottom = 1.0m,
            MarginLeft = 1.0m,
            MarginRight = 1.0m,
            Units = "in"
        };

        var currentPhysicalPage = new PhysicalPageModel { PageNumber = 1, Layout = layout };
        result.Add(currentPhysicalPage);

        double currentY = 0;
        double maxYForPage = 11.0 - (double)(layout.MarginTop ?? 1.0m) - (double)(layout.MarginBottom ?? 1.0m);

        if (layout.Orientation?.Equals("Landscape", StringComparison.OrdinalIgnoreCase) == true)
        {
            maxYForPage = 8.5 - (double)(layout.MarginTop ?? 1.0m) - (double)(layout.MarginBottom ?? 1.0m);
        }

        var rows = ParseStructureRows(page.Structure);

        foreach (var row in rows)
        {
            double maxRowHeight = 0;

            // First pass: compute height needed for this row
            var rowVisuals = new List<VisualManifest>();
            foreach (var slot in row.Slots)
            {
                if (page.SlotMap.TryGetValue(slot, out var visName))
                {
                    var vis = manifest.Visuals.FirstOrDefault(v => v.Name.Equals(visName, StringComparison.OrdinalIgnoreCase));
                    if (vis != null && vis.PrintLayout?.ExcludeFromPrint != true)
                    {
                        rowVisuals.Add(vis);
                        double visHeight = MeasureVisualHeight(vis);
                        if (visHeight > maxRowHeight) maxRowHeight = visHeight;
                    }
                }
            }

            if (rowVisuals.Count == 0) continue;

            // Should we page break before?
            bool forceBreakBefore = rowVisuals.Any(v => v.PrintLayout?.PageBreakBefore == true);

            if (forceBreakBefore || (currentY > 0 && currentY + maxRowHeight > maxYForPage && !CanSplit(rowVisuals)))
            {
                currentPhysicalPage = new PhysicalPageModel { PageNumber = result.Count + 1, Layout = layout };
                result.Add(currentPhysicalPage);
                currentY = 0;
            }

            if (CanSplit(rowVisuals) && currentY + maxRowHeight > maxYForPage)
            {
                // Implement table splitting across pages
                SplitAndAddVisuals(rowVisuals, ref currentPhysicalPage, ref currentY, maxYForPage, result, layout);
            }
            else
            {
                foreach (var vis in rowVisuals)
                {
                    currentPhysicalPage.Visuals.Add(new PlacedVisual
                    {
                        Visual = vis,
                        TopOffset = currentY,
                        Height = maxRowHeight
                    });
                }
                currentY += maxRowHeight;
            }

            // Should we page break after?
            bool forceBreakAfter = rowVisuals.Any(v => v.PrintLayout?.PageBreakAfter == true);
            if (forceBreakAfter)
            {
                currentPhysicalPage = new PhysicalPageModel { PageNumber = result.Count + 1, Layout = layout };
                result.Add(currentPhysicalPage);
                currentY = 0;
            }
        }

        // Remove empty trailing pages
        result.RemoveAll(p => p.Visuals.Count == 0 && p.PageNumber > 1);

        return result;
    }

    private double MeasureVisualHeight(VisualManifest vis)
    {
        // Headers and padding roughly 0.75 inches
        double baseHeight = 0.75;

        if (vis.VisualType.Equals("TABLE", StringComparison.OrdinalIgnoreCase))
        {
            // Title + Column headers = ~0.75, each row ~0.25
            return baseHeight + (vis.Rows.Count * 0.25);
        }

        // Default chart height
        return 4.0;
    }

    private bool CanSplit(List<VisualManifest> visuals)
    {
        // We can only split if the row contains a single TABLE visual that is allowed to split
        if (visuals.Count == 1 && visuals[0].VisualType.Equals("TABLE", StringComparison.OrdinalIgnoreCase))
        {
            return visuals[0].PrintLayout?.KeepTogether != true;
        }
        return false;
    }

    private void SplitAndAddVisuals(List<VisualManifest> visuals, ref PhysicalPageModel currentPage, ref double currentY, double maxYForPage, List<PhysicalPageModel> result, PageLayoutDefinitionManifest layout)
    {
        var vis = visuals[0]; // We only split if it's a single table
        int totalRows = vis.Rows.Count;
        int currentRow = 0;

        double headerHeight = 0.75; // Title + Column Headers
        double rowHeight = 0.25;

        if (totalRows == 0)
        {
            // Empty table
            currentPage.Visuals.Add(new PlacedVisual
            {
                Visual = vis,
                TopOffset = currentY,
                Height = headerHeight
            });
            currentY += headerHeight;
            return;
        }

        while (currentRow < totalRows)
        {
            double availableSpace = maxYForPage - currentY;
            if (availableSpace < headerHeight + rowHeight)
            {
                // Not enough space for even one row, move to next page
                currentPage = new PhysicalPageModel { PageNumber = result.Count + 1, Layout = layout };
                result.Add(currentPage);
                currentY = 0;
                availableSpace = maxYForPage;
            }

            int rowsThatFit = (int)((availableSpace - headerHeight) / rowHeight);
            if (rowsThatFit < 1) rowsThatFit = 1; // force at least one row if somehow space is tiny

            int endRow = Math.Min(currentRow + rowsThatFit, totalRows);
            int rowsInSlice = endRow - currentRow;
            double sliceHeight = headerHeight + (rowsInSlice * rowHeight);

            currentPage.Visuals.Add(new PlacedVisual
            {
                Visual = vis,
                TopOffset = currentY,
                Height = sliceHeight,
                StartRowIndex = currentRow,
                EndRowIndex = endRow - 1
            });

            currentY += sliceHeight;
            currentRow = endRow;

            if (currentRow < totalRows)
            {
                // We need another page
                currentPage = new PhysicalPageModel { PageNumber = result.Count + 1, Layout = layout };
                result.Add(currentPage);
                currentY = 0;
            }
        }
    }

    private List<StructureRow> ParseStructureRows(string structure)
    {
        var rows = new List<StructureRow>();
        if (string.IsNullOrWhiteSpace(structure)) return rows;

        var lines = structure.Split(new[] { '/', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var slots = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Distinct().ToList();
            rows.Add(new StructureRow { Slots = slots });
        }
        return rows;
    }

    private class StructureRow
    {
        public List<string> Slots { get; set; } = new();
    }
}
