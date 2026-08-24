using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Portal.Models;
using ETL_SQL.Reporting.Authoring;

namespace ETL_SQL.Portal.Services;

/// <summary>Portal adapter for the host-neutral report-designer script generator.</summary>
public sealed class DesignerScriptGenerationService
{
    private readonly ETL_SQL.Reporting.Authoring.DesignerScriptGenerationService _inner = new();

    public string Generate(DesignerStateDto state) => _inner.Generate(state.ToAuthoringState());
}

internal static class DesignerAuthoringStateAdapter
{
    internal static DesignerAuthoringState ToAuthoringState(this DesignerStateDto state) => new(
        (state.Pages ?? []).Select(page => new DesignerAuthoringPage(
            page.Id,
            page.Name,
            page.Mode,
            (page.Visuals ?? []).Select(ToAuthoringVisual).ToList())).ToList(),
        (state.Datasets ?? []).Select(dataset => new DesignerAuthoringDataset(
            dataset.Id,
            dataset.Name,
            dataset.Query)).ToList(),
        state.ReportStyle is null
            ? null
            : new DesignerAuthoringReportStyle(
                state.ReportStyle.Theme,
                state.ReportStyle.Accent,
                state.ReportStyle.Background,
                state.ReportStyle.Surface,
                state.ReportStyle.Text),
        // Preserve the null/empty distinction: null means the client does not edit bookmarks.
        state.Bookmarks?.Select(ToAuthoringBookmark).ToList());

    private static DesignerAuthoringBookmark ToAuthoringBookmark(DesignerBookmarkDto bookmark) => new(
        bookmark.Id,
        bookmark.Name,
        bookmark.Title,
        bookmark.Page,
        bookmark.IsDefault,
        bookmark.Parameters?
            .Select(p => new DesignerAuthoringBookmarkParameter(p.Name, p.Value)).ToList(),
        bookmark.State?
            .Select(s => new DesignerAuthoringBookmarkState(s.ObjectName, s.Property, s.On)).ToList());

    private static DesignerAuthoringVisual ToAuthoringVisual(DesignerVisualDto visual) => new(
        visual.Id,
        visual.Name,
        visual.Type,
        visual.GridCol,
        visual.GridRow,
        visual.GridColSpan,
        visual.GridRowSpan,
        visual.Title,
        visual.Dataset,
        visual.Mappings ?? new Dictionary<string, string>(),
        visual.Options ?? new Dictionary<string, string>(),
        visual.ContainerId);
}
