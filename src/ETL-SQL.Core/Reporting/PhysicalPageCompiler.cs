using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core;

namespace ETL_SQL.Core.Reporting;

public class PhysicalPageCompiler
{
    public List<PhysicalPageModel> Compile(CreatePageStatement page, IReadOnlyDictionary<string, CreateVisualStatement> visualRegistry)
    {
        var result = new List<PhysicalPageModel>();
        var layout = page.PrintLayout ?? new PageLayoutDefinition 
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
            double maxRowHeight = 2.0; // Assume 2 inches default per row
            
            // Should we page break before?
            bool forceBreakBefore = false;
            foreach (var slot in row.Slots)
            {
                if (page.SlotMap.TryGetValue(slot, out var visName) && visualRegistry.TryGetValue(visName, out var vis))
                {
                    if (vis.PrintLayout?.PageBreakBefore == true) forceBreakBefore = true;
                }
            }
            
            if (forceBreakBefore || currentY + maxRowHeight > maxYForPage)
            {
                currentPhysicalPage = new PhysicalPageModel { PageNumber = result.Count + 1, Layout = layout };
                result.Add(currentPhysicalPage);
                currentY = 0;
            }
            
            foreach (var slot in row.Slots)
            {
                if (page.SlotMap.TryGetValue(slot, out var visName) && visualRegistry.TryGetValue(visName, out var vis))
                {
                    if (vis.PrintLayout?.ExcludeFromPrint == true) continue;
                    
                    currentPhysicalPage.Visuals.Add(new PlacedVisual
                    {
                        Visual = vis,
                        TopOffset = currentY,
                        Height = maxRowHeight
                    });
                }
            }
            
            currentY += maxRowHeight;
            
            // Should we page break after?
            bool forceBreakAfter = false;
            foreach (var slot in row.Slots)
            {
                if (page.SlotMap.TryGetValue(slot, out var visName) && visualRegistry.TryGetValue(visName, out var vis))
                {
                    if (vis.PrintLayout?.PageBreakAfter == true) forceBreakAfter = true;
                }
            }
            
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
