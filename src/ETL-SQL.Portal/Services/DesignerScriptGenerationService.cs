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
            (page.Visuals ?? []).Select(ToAuthoringVisual).ToList(),
            page.PrintLayout is null ? null : new DesignerAuthoringPageLayout(
                page.PrintLayout.PageSize, page.PrintLayout.Orientation,
                page.PrintLayout.MarginTop, page.PrintLayout.MarginRight,
                page.PrintLayout.MarginBottom, page.PrintLayout.MarginLeft,
                page.PrintLayout.Units, page.PrintLayout.Overflow,
                page.PrintLayout.CustomWidth, page.PrintLayout.CustomHeight))).ToList(),
        (state.Datasets ?? []).Select(dataset => new DesignerAuthoringDataset(
            dataset.Id,
            dataset.Name,
            dataset.Query,
            dataset.Ttl)).ToList(),
        state.ReportStyle is null
            ? null
            : new DesignerAuthoringReportStyle(
                state.ReportStyle.Theme,
                state.ReportStyle.Accent,
                state.ReportStyle.Background,
                state.ReportStyle.Surface,
                state.ReportStyle.Text),
        // Preserve the null/empty distinction: null means the client does not edit bookmarks.
        state.Bookmarks?.Select(ToAuthoringBookmark).ToList(),
        // Preserve the same distinction for DECLARE statements.
        state.Parameters?.Select(parameter => new DesignerAuthoringParameter(
            parameter.Name,
            parameter.DataType,
            parameter.InitialValue,
            parameter.IsInput,
            parameter.IsOutput,
            parameter.IsRequired,
            parameter.IsSensitive,
            parameter.IsBlockScoped)).ToList(),
        // Carried across so a client that echoes parse output back is not silently reshaped. Nothing
        // downstream reads it: the generator and the patcher never write CREATE CONNECTION.
        state.Connections?.Select(connection =>
            new DesignerAuthoringConnection(connection.Name, connection.Text)).ToList());

    /// <summary>
    /// The way back: host-neutral authoring state to the DTO shape the browser consumes. The
    /// null/empty distinction on bookmarks and parameters survives in both directions — null means
    /// "this client does not edit them", which is not the same as "there are none".
    /// </summary>
    internal static DesignerStateDto ToStateDto(this DesignerAuthoringState state) => new(
        (state.Pages ?? []).Select(page => new DesignerPageDto(
            page.Id,
            page.Name,
            page.Mode,
            (page.Visuals ?? []).Select(ToVisualDto).ToList(),
            page.PrintLayout is null ? null : new DesignerPageLayoutDto(
                page.PrintLayout.PageSize, page.PrintLayout.Orientation,
                page.PrintLayout.MarginTop, page.PrintLayout.MarginRight,
                page.PrintLayout.MarginBottom, page.PrintLayout.MarginLeft,
                page.PrintLayout.Units, page.PrintLayout.Overflow,
                page.PrintLayout.CustomWidth, page.PrintLayout.CustomHeight))).ToList(),
        // TTL travels in both directions. It was dropped here, so a round-trip through the browser
        // handed the patcher a dataset whose TTL was null — indistinguishable from "the author
        // cleared it" — and the clause was deleted from a script nobody had asked to change.
        (state.Datasets ?? []).Select(dataset => new DesignerDatasetDto(
            dataset.Id,
            dataset.Name,
            dataset.Query,
            dataset.Ttl)).ToList(),
        state.ReportStyle is null
            ? null
            : new DesignerReportStyleDto(
                state.ReportStyle.Theme,
                state.ReportStyle.Accent,
                state.ReportStyle.Background,
                state.ReportStyle.Surface,
                state.ReportStyle.Text),
        state.Bookmarks?.Select(ToBookmarkDto).ToList(),
        state.Parameters?.Select(parameter => new DesignerParameterDto(
            parameter.Name,
            parameter.DataType,
            parameter.InitialValue,
            parameter.IsInput,
            parameter.IsOutput,
            parameter.IsRequired,
            parameter.IsSensitive,
            parameter.IsBlockScoped)).ToList(),
        state.Connections?.Select(connection =>
            new DesignerConnectionDto(connection.Name, connection.Text)).ToList());

    private static DesignerBookmarkDto ToBookmarkDto(DesignerAuthoringBookmark bookmark) => new(
        bookmark.Id,
        bookmark.Name,
        bookmark.Title,
        bookmark.Page,
        bookmark.IsDefault,
        bookmark.Parameters?
            .Select(p => new DesignerBookmarkParameterDto(p.Name, p.Value)).ToList(),
        bookmark.State?
            .Select(s => new DesignerBookmarkStateDto(s.ObjectName, s.Property, s.On)).ToList());

    private static DesignerVisualDto ToVisualDto(DesignerAuthoringVisual visual) => new(
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
        visual.ContainerId,
        visual.Formatting is null ? null : ToFormattingDto(visual.Formatting));

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
        visual.ContainerId,
        visual.Formatting is null ? null : ToAuthoringFormatting(visual.Formatting));

    private static DesignerVisualFormattingDto ToFormattingDto(DesignerAuthoringVisualFormatting formatting) => new(
        formatting.Title is null ? null : new DesignerTextFormattingDto(
            formatting.Title.Text, formatting.Title.Color, formatting.Title.Font,
            formatting.Title.Size, formatting.Title.Weight, formatting.Title.Align),
        formatting.Subtitle is null ? null : new DesignerTextFormattingDto(
            formatting.Subtitle.Text, formatting.Subtitle.Color, formatting.Subtitle.Font,
            formatting.Subtitle.Size, formatting.Subtitle.Weight, formatting.Subtitle.Align),
        formatting.XAxis,
        formatting.YAxis,
        formatting.Palette,
        formatting.ConditionalRules?.Select(rule => new DesignerConditionalFormattingRuleDto(
            rule.Condition, rule.BackgroundColor, rule.FontColor)).ToList(),
        formatting.Fields?.ToDictionary(
            field => field.Key,
            field => new DesignerFieldFormattingDto(
                field.Value.Format, field.Value.Align, field.Value.DisplayName,
                field.Value.DataBar, field.Value.DataBarColor,
                field.Value.ColorScaleFrom, field.Value.ColorScaleTo),
            StringComparer.OrdinalIgnoreCase));

    private static DesignerAuthoringVisualFormatting ToAuthoringFormatting(DesignerVisualFormattingDto formatting) => new(
        formatting.Title is null ? null : new DesignerAuthoringTextFormatting(
            formatting.Title.Text, formatting.Title.Color, formatting.Title.Font,
            formatting.Title.Size, formatting.Title.Weight, formatting.Title.Align),
        formatting.Subtitle is null ? null : new DesignerAuthoringTextFormatting(
            formatting.Subtitle.Text, formatting.Subtitle.Color, formatting.Subtitle.Font,
            formatting.Subtitle.Size, formatting.Subtitle.Weight, formatting.Subtitle.Align),
        formatting.XAxis,
        formatting.YAxis,
        formatting.Palette,
        formatting.ConditionalRules?.Select(rule => new DesignerAuthoringConditionalFormattingRule(
            rule.Condition, rule.BackgroundColor, rule.FontColor)).ToList(),
        formatting.Fields?.ToDictionary(
            field => field.Key,
            field => new DesignerAuthoringFieldFormatting(
                field.Value.Format, field.Value.Align, field.Value.DisplayName,
                field.Value.DataBar, field.Value.DataBarColor,
                field.Value.ColorScaleFrom, field.Value.ColorScaleTo),
            StringComparer.OrdinalIgnoreCase));
}
