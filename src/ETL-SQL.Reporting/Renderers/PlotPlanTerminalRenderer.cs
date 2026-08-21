using System;
using System.Globalization;
using System.Linq;
using ETL_SQL.Reporting.Semantics;
using ETL_SQL.Reporting.Semantics.Runtime;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace ETL_SQL.Reporting.Renderers;

internal static class PlotPlanTerminalRenderer
{
    public static IRenderable Render(PlotPlan plan)
    {
        plan.Validate();
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Mark");
        table.AddColumn("Series");
        table.AddColumn("Category / X");
        table.AddColumn(new TableColumn("Value / Y").RightAligned());

        foreach (var layer in plan.Layers)
        {
            var series = plan.Series.FirstOrDefault(item => item.Key == layer.SeriesKey)?.Label ?? layer.Id;
            foreach (var datum in layer.Data)
            {
                var x = Channel(datum, FieldChannel.X) ?? Channel(datum, FieldChannel.Theta);
                var y = Channel(datum, FieldChannel.Y) ?? Channel(datum, FieldChannel.Y2) ?? Channel(datum, FieldChannel.Radius);
                table.AddRow(
                    Markup.Escape(layer.Mark.ToString()),
                    Markup.Escape(series),
                    Markup.Escape(x is null ? $"row {datum.RowIndex + 1}" : PlotPlanResolver.Display(x)),
                    datum.IsGap ? "[grey]gap[/]" : Markup.Escape(y is null ? "" : PlotPlanResolver.Display(y)));
            }
        }

        return new Panel(new Rows(
            new Markup($"[grey]{Markup.Escape(plan.AccessibleSummary)}[/]"),
            table))
        {
            Header = new PanelHeader(Markup.Escape(plan.Title ?? plan.SpecId)),
            Border = BoxBorder.Rounded,
            Expand = false
        };
    }

    private static ChartValue? Channel(ResolvedDatum datum, FieldChannel channel) =>
        datum.Channels.FirstOrDefault(item => item.Channel == channel)?.Value;
}
