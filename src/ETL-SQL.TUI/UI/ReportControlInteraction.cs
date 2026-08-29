using System.Globalization;
using System.Text.Json;
using ETL_SQL.Core;
using ETL_SQL.Reporting;

namespace ETL_SQL.TUI.UI;

/// <summary>Terminal interaction semantics for parameter-bound report controls.</summary>
public static class ReportControlInteraction
{
    private static readonly HashSet<string> InteractiveTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "SLICER", "MULTISELECT", "DATEPICKER", "RELDATEPICKER", "REDATEPICKER",
        "SLIDER", "SEARCH", "CHECKBOX", "TEXTBOX", "NUMBERBOX"
    };

    public static IReadOnlyList<VisualManifest> GetControls(ReportManifest manifest, int pageIndex)
    {
        if (manifest.Pages.Count == 0) return [];

        var page = manifest.Pages[Math.Clamp(pageIndex, 0, manifest.Pages.Count - 1)];
        var controls = new List<VisualManifest>();
        var visitedContainers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddItem(string name)
        {
            var visual = manifest.Visuals.FirstOrDefault(candidate =>
                candidate.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (visual is not null)
            {
                if (InteractiveTypes.Contains(visual.VisualType)) controls.Add(visual);
                return;
            }

            var container = (manifest.Containers ?? []).FirstOrDefault(candidate =>
                candidate.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (container is null || !visitedContainers.Add(container.Name)) return;
            foreach (var child in (container.SlotMap ?? [])
                .OrderBy(pair => pair.Key).Select(pair => pair.Value).Distinct())
                AddItem(child);
        }

        var pageItems = page.SlotMap.OrderBy(pair => pair.Key).Select(pair => pair.Value).Distinct().ToList();
        if (pageItems.Count == 0)
            pageItems = manifest.Visuals.Select(visual => visual.Name).ToList();
        foreach (var name in pageItems)
            AddItem(name);

        return controls;
    }

    public static string? GetParameterName(VisualManifest visual) =>
        visual.Actions.FirstOrDefault(action =>
            action.Type.Equals("SET_PARAMETER", StringComparison.OrdinalIgnoreCase)
            && action.Trigger.Equals("ON_CHANGE", StringComparison.OrdinalIgnoreCase))?.ParameterName
        ?? visual.Actions.FirstOrDefault(action =>
            action.Type.Equals("SET_PARAMETER", StringComparison.OrdinalIgnoreCase))?.ParameterName;

    public static string GetCurrentValue(ReportManifest manifest, VisualManifest visual)
    {
        var parameter = GetParameterName(visual);
        if (!string.IsNullOrWhiteSpace(parameter))
        {
            if (manifest.Parameters.TryGetValue(parameter, out var value)) return value ?? string.Empty;
            var alternate = parameter.StartsWith('@') ? parameter[1..] : "@" + parameter;
            if (manifest.Parameters.TryGetValue(alternate, out value)) return value ?? string.Empty;
        }
        return visual.DefaultValue ?? visual.Options.GetValueOrDefault("DEFAULT") ?? string.Empty;
    }

    public static IReadOnlyList<string> GetChoices(VisualManifest visual)
    {
        var action = visual.Actions.FirstOrDefault(candidate =>
            candidate.Type.Equals("SET_PARAMETER", StringComparison.OrdinalIgnoreCase));
        var valueColumn = action?.ValueColumn
            ?? visual.Options.GetValueOrDefault("mapping:value")
            ?? visual.Columns.FirstOrDefault();
        var columnIndex = valueColumn is null
            ? 0
            : visual.Columns.FindIndex(column => column.Equals(valueColumn, StringComparison.OrdinalIgnoreCase));
        if (columnIndex < 0) columnIndex = 0;

        return visual.Rows
            .Where(row => columnIndex < row.Count && !string.IsNullOrWhiteSpace(row[columnIndex]))
            .Select(row => row[columnIndex]!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool TryNormalizeValue(
        VisualManifest visual,
        string value,
        out string normalized,
        out string? error)
    {
        normalized = value.Trim();
        error = null;
        var type = visual.VisualType.ToUpperInvariant();

        if (type == "DATEPICKER"
            && !DateOnly.TryParseExact(normalized, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out _))
        {
            error = "Enter a date in YYYY-MM-DD format.";
            return false;
        }

        if (type is "SLIDER" or "NUMBERBOX")
        {
            if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                error = "Enter a numeric value.";
                return false;
            }
            if (visual.Min is not null && number < visual.Min)
            {
                error = $"Enter a value at or above {visual.Min.Value.ToString(CultureInfo.InvariantCulture)}.";
                return false;
            }
            if (visual.Max is not null && number > visual.Max)
            {
                error = $"Enter a value at or below {visual.Max.Value.ToString(CultureInfo.InvariantCulture)}.";
                return false;
            }
            normalized = number.ToString(CultureInfo.InvariantCulture);
        }
        else if (type == "CHECKBOX")
        {
            if (normalized.Equals("true", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("on", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("yes", StringComparison.OrdinalIgnoreCase)
                || normalized == "1") normalized = "true";
            else if (normalized.Equals("false", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("off", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("no", StringComparison.OrdinalIgnoreCase)
                || normalized == "0") normalized = "false";
            else
            {
                error = "Enter ON or OFF.";
                return false;
            }
        }
        else if (type == "MULTISELECT")
        {
            var values = normalized.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var choices = GetChoices(visual);
            var invalid = values.FirstOrDefault(value =>
                !choices.Contains(value, StringComparer.OrdinalIgnoreCase));
            if (invalid is not null)
            {
                error = $"'{invalid}' is not an available choice.";
                return false;
            }
            normalized = JsonSerializer.Serialize(values);
        }

        return true;
    }

    public static async Task<int> ApplyAsync(
        IExecutionContext context,
        ReportManifest manifest,
        VisualManifest visual,
        string value)
    {
        var parameter = GetParameterName(visual);
        if (string.IsNullOrWhiteSpace(parameter))
            throw new InvalidOperationException($"Report control '{visual.Name}' has no SET_PARAMETER action.");
        return await ReportInteractionRefresher.RefreshAffectedVisualsAsync(
            context, manifest, [(parameter, value)], isInteraction: false);
    }
}
