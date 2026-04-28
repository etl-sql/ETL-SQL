using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles CREATE TEMPLATE statements (Phase 12B).
    /// Registers the template in memory and persists it as a JSON file in the TemplatePath.
    /// Security: Only .json files are allowed.
    /// </summary>
    public class CreateTemplateStatementHandler(ILogger logger) : IStatementHandler
    {
        private readonly ILogger _logger = logger;
        public Type SupportedStatementType => typeof(CreateTemplateStatement);

        public Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (CreateTemplateStatement)statement;

            // 1. Register in memory
            if (stmt.Mode == ObjectCreationMode.Create && context.ReportContext.TemplateDefinitions.ContainsKey(stmt.Name))
            {
                throw new ExecutionException($"Template '{stmt.Name}' already exists. Use CREATE OR ALTER or DROP TEMPLATE first.", null, stmt.Line, stmt.Column);
            }

            context.ReportContext.TemplateDefinitions[stmt.Name] = stmt;

            // 2. Persist to disk
            try
            {
                SaveTemplateToDisk(stmt, context);
            }
            catch (Exception ex)
            {
                _logger.Warning("Failed to persist template '{TemplateName}' to disk: {Message}", stmt.Name, ex.Message);
                // We proceed even if disk save fails, as it's in memory for the current session
            }

            _logger.Debug("Template '{TemplateName}' registered and persisted.", stmt.Name);
            context.Log($"Template '{stmt.Name}' {(stmt.Mode == ObjectCreationMode.CreateOrAlter ? "updated" : "created")}.");

            return Task.CompletedTask;
        }

        private void SaveTemplateToDisk(CreateTemplateStatement stmt, IExecutionContext context)
        {
            var templateDir = context.ReportContext.TemplatePath;
            
            // Ensure directory exists
            if (!Directory.Exists(templateDir))
            {
                Directory.CreateDirectory(templateDir);
                _logger.Debug("Created template directory: {Path}", templateDir);
            }

            // Filename must end in .json
            string fileName = stmt.Name;
            if (!fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                fileName += ".json";
            }

            string filePath = Path.Combine(templateDir, fileName);
            
            // Security: Resolve and validate path
            string resolvedPath = context.ResolvePath(filePath);
            
            // Security: Enforce .json extension even after joining
            if (!resolvedPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                 throw new ExecutionException($"Security Violation: Template files must have .json extension. Target: {resolvedPath}", null, stmt.Line, stmt.Column);
            }

            context.IncrementOperationCount(OperationType.FileSystem, resolvedPath);

            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(stmt.Options, options);

            File.WriteAllText(resolvedPath, json);
            _logger.Debug("Persisted template '{TemplateName}' to {Path}", stmt.Name, resolvedPath);
        }
    }
}

