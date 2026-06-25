using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Parser;

namespace ETL_SQL.Engine.Handlers;
/// <summary>
/// Handles ALTER VISUAL, PAGE, CONTAINER, etc. statements.
/// Performs a partial update (patch) of the existing object definition.
/// </summary>
public class AlterReportObjectStatementHandler(ILogger logger) : IStatementHandler
{
    private readonly ILogger _logger = logger;
    public Type SupportedStatementType => typeof(AlterReportObjectStatement);

    public Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (AlterReportObjectStatement)statement;

        switch (stmt.ObjectType)
        {
            case ReportObjectType.Visual:
                UpdateVisual(stmt, context);
                break;
            case ReportObjectType.Page:
                UpdatePage(stmt, context);
                break;
            case ReportObjectType.Container:
                UpdateContainer(stmt, context);
                break;
            case ReportObjectType.Template:
                UpdateTemplate(stmt, context);
                break;
            default:
                throw new ExecutionException($"ALTER not yet implemented for {stmt.ObjectType}", null, stmt.Line, stmt.Column);
        }

        _logger.Debug("{ObjectType} '{ObjectName}' altered.", stmt.ObjectType, stmt.Name);
        context.Log($"{stmt.ObjectType} '{stmt.Name}' updated.");

        return Task.CompletedTask;
    }

    private void UpdateVisual(AlterReportObjectStatement stmt, IExecutionContext context)
    {
        if (!context.ReportContext.VisualDefinitions.TryGetValue(stmt.Name, out var visual))
        {
            throw new ExecutionException($"Visual '{stmt.Name}' does not exist.", null, stmt.Line, stmt.Column);
        }

        // Apply patches using 'with' since record properties are init-only
        var updated = visual with
        {
            Source = stmt.Source ?? visual.Source,
            Mappings = stmt.Mappings ?? visual.Mappings,
            Options = stmt.Options ?? visual.Options,
            AxisOptions = stmt.AxisOptions ?? visual.AxisOptions,
            Actions = stmt.Actions ?? visual.Actions,
            Styles = stmt.Styles ?? visual.Styles,
            StyleName = stmt.StyleName ?? visual.StyleName,
            Title = stmt.Title ?? visual.Title,
            Subtitle = stmt.Subtitle ?? visual.Subtitle,
            Tooltip = stmt.Tooltip ?? visual.Tooltip
        };

        context.ReportContext.VisualDefinitions[stmt.Name] = updated;
    }

    private void UpdatePage(AlterReportObjectStatement stmt, IExecutionContext context)
    {
        if (!context.ReportContext.PageDefinitions.TryGetValue(stmt.Name, out var page))
        {
            throw new ExecutionException($"Page '{stmt.Name}' does not exist.", null, stmt.Line, stmt.Column);
        }

        var updated = page with
        {
            Title = stmt.Title ?? page.Title,
            Subtitle = stmt.Subtitle ?? page.Subtitle,
            Tooltip = stmt.Tooltip ?? page.Tooltip,
            Styles = stmt.Styles ?? page.Styles,
            StyleName = stmt.StyleName ?? page.StyleName
        };

        context.ReportContext.PageDefinitions[stmt.Name] = updated;
    }

    private void UpdateContainer(AlterReportObjectStatement stmt, IExecutionContext context)
    {
        if (!context.ReportContext.ContainerDefinitions.TryGetValue(stmt.Name, out var container))
        {
            throw new ExecutionException($"Container '{stmt.Name}' does not exist.", null, stmt.Line, stmt.Column);
        }

        var updated = container with
        {
            Styles = stmt.Styles ?? container.Styles,
            StyleName = stmt.StyleName ?? container.StyleName,
            Title = stmt.Title ?? container.Title,
            Subtitle = stmt.Subtitle ?? container.Subtitle,
            Tooltip = stmt.Tooltip ?? container.Tooltip
        };

        context.ReportContext.ContainerDefinitions[stmt.Name] = updated;
    }

    private void UpdateTemplate(AlterReportObjectStatement stmt, IExecutionContext context)
    {
        if (!context.ReportContext.TemplateDefinitions.TryGetValue(stmt.Name, out var template))
        {
            throw new ExecutionException($"Template '{stmt.Name}' does not exist.", null, stmt.Line, stmt.Column);
        }

        // Patching Options dictionary
        if (stmt.Options != null)
        {
            foreach (var opt in stmt.Options)
            {
                // VisualOption uses Key/Value strings
                template.Options[opt.Key] = opt.Value;
            }
        }

        // Persistence
        try
        {
            var templateDir = context.ReportContext.TemplatePath;
            string fileName = stmt.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? stmt.Name : stmt.Name + ".json";
            string filePath = Path.Combine(templateDir, fileName);
            string resolvedPath = context.ResolvePath(filePath);

            if (!resolvedPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                throw new ExecutionException($"Security Violation: Template files must have .json extension. Target: {resolvedPath}", null, stmt.Line, stmt.Column);

            context.IncrementOperationCount(OperationType.FileSystem, resolvedPath);
            var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(template.Options, jsonOptions);
            File.WriteAllText(resolvedPath, json);
        }
        catch (Exception ex)
        {
            _logger.Warning("Failed to persist altered template '{TemplateName}' to disk: {Message}", stmt.Name, ex.Message);
        }
    }
}

