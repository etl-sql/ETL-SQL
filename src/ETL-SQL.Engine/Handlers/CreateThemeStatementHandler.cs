using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Reporting;

namespace ETL_SQL.Engine.Handlers;
/// <summary>
/// Handles CREATE THEME statements.
/// Registers the theme in memory and saves a JSON file to {TemplatePath}/Themes/.
/// The JSON file is an ECharts-compatible theme object that can be loaded by
/// echarts.registerTheme() at report render time.
/// </summary>
public class CreateThemeStatementHandler(ILogger logger) : IStatementHandler
{
    private readonly ILogger _logger = logger;
    public Type SupportedStatementType => typeof(CreateThemeStatement);

    public Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (CreateThemeStatement)statement;

        if (stmt.Mode == ObjectCreationMode.Create && context.ReportContext.ThemeDefinitions.ContainsKey(stmt.Name))
            throw new ExecutionException($"Theme '{stmt.Name}' already exists. Use CREATE OR ALTER THEME.", null, stmt.Line, stmt.Column);

        context.ReportContext.ThemeDefinitions[stmt.Name] = stmt;

        try
        {
            SaveThemeToDisk(stmt, context);
        }
        catch (Exception ex)
        {
            _logger.Warning("Failed to persist theme '{ThemeName}' to disk: {Message}", stmt.Name, ex.Message);
        }

        _logger.Debug("Theme '{ThemeName}' registered.", stmt.Name);
        context.Log($"Theme '{stmt.Name}' {(stmt.Mode == ObjectCreationMode.CreateOrAlter ? "updated" : "created")}.");
        return Task.CompletedTask;
    }

    private void SaveThemeToDisk(CreateThemeStatement stmt, IExecutionContext context)
    {
        var themeDir = Path.Combine(context.ReportContext.TemplatePath, "Themes");
        Directory.CreateDirectory(themeDir);

        var fileName = stmt.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? stmt.Name : stmt.Name + ".json";
        var filePath = context.ResolvePath(Path.Combine(themeDir, fileName));

        if (!filePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw new ExecutionException($"Security violation: theme file must have .json extension. Target: {filePath}", null, stmt.Line, stmt.Column);

        context.IncrementOperationCount(OperationType.FileSystem, filePath);

        var themeJson = ThemeBuilder.BuildEChartsTheme(stmt.Properties);
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(filePath, themeJson.ToJsonString(options));
        _logger.Debug("Persisted theme '{ThemeName}' to {Path}", stmt.Name, filePath);
    }

    /// <summary>
    /// Translates Report-SQL theme properties to the ECharts theme JSON structure.
    /// Supported keys (case-insensitive):
    ///   BACKGROUND        → backgroundColor
    ///   TEXT_COLOR        → textStyle.color + title/legend/axis label colors
    ///   ACCENT_COLOR      → colors[0]
    ///   COLORS            → colors array (comma-separated hex list)
    ///   AXIS_COLOR        → axis line + axis label colors
    ///   GRID_COLOR        → splitLine colors
    ///   BORDER_COLOR      → axis line colors
    /// All other keys are passed through as-is to the root of the theme object.
    /// </summary>
    public static JsonObject BuildEChartsTheme(Dictionary<string, string> props)
        => ThemeBuilder.BuildEChartsTheme(props);
}

