using System;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles statements that override security guardrails (e.g., SET ALLOW_FILE_TYPE_ACCESS ON).
    /// These overrides are only honored by the SecurityService if the current script is executing 
    /// within an approved 'Safe Zone'.
    /// </summary>
    public class SetSecurityOverrideStatementHandler(ILogger logger) : IStatementHandler
    {
        private readonly ILogger _logger = logger;

        public Type SupportedStatementType => typeof(SetSecurityOverrideStatement);

        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (SetSecurityOverrideStatement)statement;

            string overrideName = "";
            switch (stmt.Override)
            {
                case SecurityOverride.FileTypeAccess:
                    context.AllowUnknownFileTypes = stmt.Enabled;
                    overrideName = "ALLOW_FILE_TYPE_ACCESS";
                    break;
                case SecurityOverride.FileTypeExtension:
                    if (stmt.Value != null)
                    {
                        var extObj = await context.EvaluationContext.EvaluateValue(stmt.Value, new Row());
                        string ext = extObj?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(ext))
                        {
                            if (!ext.StartsWith(".")) ext = "." + ext;
                            context.AllowedFileTypeOverrides.Add(ext);
                            overrideName = $"ALLOW_FILE_TYPE_ACCESS = '{ext}'";
                        }
                    }
                    break;
                case SecurityOverride.LargeFileCount:
                    context.AllowLargeFileOperationCount = stmt.Enabled;
                    overrideName = $"ALLOW_GREATER_THAN_{context.SecurityService.MaxFileOperations}_FILE";
                    break;
                case SecurityOverride.DeepRecursion:
                    context.AllowDeepRecursion = stmt.Enabled;
                    overrideName = $"ALLOW_RECURSIVE_GREATER_THAN_{context.SecurityService.MaxRecursiveDepth}_LAYERS";
                    break;
                case SecurityOverride.LargeStringResults:
                    context.AllowLargeStringResults = stmt.Enabled;
                    overrideName = "ALLOW_LARGE_STRING_RESULTS";
                    break;
            }

            string state = stmt.Enabled ? "ON" : "OFF";
            if (stmt.Override == SecurityOverride.FileTypeExtension) state = "ADDED";

            // Mandatory audit log for security overrides
            _logger.WriteLine($"Audit: Security override {overrideName} turned {state} by script.", ConsoleColor.Yellow);
        }
    }
}
