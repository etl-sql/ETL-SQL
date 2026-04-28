using System;
using System.IO;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles DROP VISUAL, PAGE, DATASET, CONTAINER, NAVIGATION, STYLE, BUTTON statements.
    /// Removes definitions from the report context.
    /// </summary>
    public class DropReportObjectStatementHandler(ILogger logger) : IStatementHandler
    {
        private readonly ILogger _logger = logger;
        public Type SupportedStatementType => typeof(DropReportObjectStatement);

        public Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (DropReportObjectStatement)statement;
            bool removed = false;

            switch (stmt.ObjectType)
            {
                case ReportObjectType.Visual:
                    context.ReportContext.VisualDefinitions.Remove(stmt.Name);
                    break;
                case ReportObjectType.Page:
                    removed = context.ReportContext.PageDefinitions.Remove(stmt.Name);
                    break;
                case ReportObjectType.Dataset:
                    removed = context.ReportContext.DatasetDefinitions.Remove(stmt.Name);
                    // Also attempt to drop the actual temp table if it exists
                    if (context.Connections.ContainsKey(stmt.Name))
                    {
                        context.Connections.Remove(stmt.Name);
                    }
                    break;
                case ReportObjectType.Container:
                    removed = context.ReportContext.ContainerDefinitions.Remove(stmt.Name);
                    break;
                case ReportObjectType.Navigation:
                    removed = context.ReportContext.NavigationDefinitions.Remove(stmt.Name);
                    break;
                case ReportObjectType.Style:
                    removed = context.ReportContext.StyleDefinitions.Remove(stmt.Name);
                    break;
                case ReportObjectType.Button:
                    removed = context.ReportContext.ButtonDefinitions.Remove(stmt.Name);
                    break;
                case ReportObjectType.Template:
                    removed = context.ReportContext.TemplateDefinitions.Remove(stmt.Name);
                    if (removed)
                    {
                        try
                        {
                            var templateDir = context.ReportContext.TemplatePath;
                            string fileName = stmt.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? stmt.Name : stmt.Name + ".json";
                            string filePath = Path.Combine(templateDir, fileName);
                            string resolvedPath = context.ResolvePath(filePath);

                            if (File.Exists(resolvedPath))
                            {
                                if (!resolvedPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                                    throw new ExecutionException($"Security Violation: Cannot delete non-JSON file via DROP TEMPLATE. Target: {resolvedPath}", null, stmt.Line, stmt.Column);

                                context.IncrementOperationCount(OperationType.FileSystem, resolvedPath);
                                File.Delete(resolvedPath);
                                _logger.Debug("Deleted template file: {Path}", resolvedPath);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Warning("Failed to delete template file for '{TemplateName}': {Message}", stmt.Name, ex.Message);
                        }
                    }
                    break;
                case ReportObjectType.Theme:
                    removed = context.ReportContext.ThemeDefinitions.Remove(stmt.Name);
                    if (removed)
                    {
                        try
                        {
                            var themeDir = Path.Combine(context.ReportContext.TemplatePath, "Themes");
                            string fileName = stmt.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? stmt.Name : stmt.Name + ".json";
                            string filePath = context.ResolvePath(Path.Combine(themeDir, fileName));
                            if (File.Exists(filePath)) File.Delete(filePath);
                        }
                        catch (Exception ex)
                        {
                            _logger.Warning("Failed to delete theme file for '{ThemeName}': {Message}", stmt.Name, ex.Message);
                        }
                    }
                    break;
                default:
                    throw new ExecutionException($"Unsupported report object type: {stmt.ObjectType}", null, stmt.Line, stmt.Column);
            }

            if (!removed && !stmt.IfExists)
            {
                throw new ExecutionException($"{stmt.ObjectType} '{stmt.Name}' does not exist.", null, stmt.Line, stmt.Column);
            }

            if (removed)
            {
                _logger.Debug("{ObjectType} '{ObjectName}' dropped.", stmt.ObjectType, stmt.Name);
                context.Log($"{stmt.ObjectType} '{stmt.Name}' dropped.");
            }
            else
            {
                _logger.Debug("{ObjectType} '{ObjectName}' did not exist (IF EXISTS specified).", stmt.ObjectType, stmt.Name);
            }

            return Task.CompletedTask;
        }
    }
}

