using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Engine.Handlers
{
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

            var themeJson = BuildEChartsTheme(stmt.Properties);
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
        {
            var theme = new JsonObject();

            // Colors array
            if (props.TryGetValue("COLORS", out var colorsStr))
            {
                var arr = new JsonArray();
                foreach (var c in colorsStr.Split(','))
                {
                    var hex = c.Trim();
                    if (!string.IsNullOrEmpty(hex)) arr.Add(hex);
                }
                theme["color"] = arr;
            }
            else if (props.TryGetValue("ACCENT_COLOR", out var accent))
            {
                theme["color"] = new JsonArray { accent };
            }

            // Background
            if (props.TryGetValue("BACKGROUND", out var bg))
                theme["backgroundColor"] = bg;

            // Text color (affects multiple sub-objects)
            var textColor = props.TryGetValue("TEXT_COLOR", out var tc) ? tc : null;
            var axisColor = props.TryGetValue("AXIS_COLOR", out var ac) ? ac : textColor;
            var gridColor = props.TryGetValue("GRID_COLOR", out var gc) ? gc : null;

            if (textColor != null)
            {
                theme["textStyle"] = new JsonObject { ["color"] = textColor };
                theme["title"] = new JsonObject
                {
                    ["textStyle"]    = new JsonObject { ["color"] = textColor },
                    ["subtextStyle"] = new JsonObject { ["color"] = textColor }
                };
                theme["legend"] = new JsonObject
                {
                    ["textStyle"] = new JsonObject { ["color"] = textColor }
                };
            }

            // Axis styling (applied to both category and value axes)
            if (axisColor != null || gridColor != null)
            {
                var axisObj = BuildAxisObject(axisColor, gridColor);
                theme["categoryAxis"] = axisObj.DeepClone();
                theme["valueAxis"]    = axisObj.DeepClone();
                theme["timeAxis"]     = axisObj.DeepClone();
                theme["logAxis"]      = axisObj.DeepClone();
            }

            // Pass-through: any key not recognised above goes to root as-is
            var handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "COLORS", "ACCENT_COLOR", "BACKGROUND", "TEXT_COLOR", "AXIS_COLOR", "GRID_COLOR", "BORDER_COLOR" };

            foreach (var kv in props)
            {
                if (!handled.Contains(kv.Key))
                    theme[kv.Key.ToLowerInvariant()] = kv.Value;
            }

            return theme;
        }

        private static JsonObject BuildAxisObject(string? axisColor, string? gridColor)
        {
            var obj = new JsonObject();
            if (axisColor != null)
            {
                obj["axisLine"]  = new JsonObject { ["lineStyle"] = new JsonObject { ["color"] = axisColor } };
                obj["axisTick"]  = new JsonObject { ["lineStyle"] = new JsonObject { ["color"] = axisColor } };
                obj["axisLabel"] = new JsonObject { ["textStyle"] = new JsonObject { ["color"] = axisColor } };
            }
            if (gridColor != null)
                obj["splitLine"] = new JsonObject { ["lineStyle"] = new JsonObject { ["color"] = new JsonArray { gridColor } } };
            return obj;
        }
    }
}

