using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Common;

namespace ETL_SQL.Engine
{
    public partial class Evaluator
    {
        public Task<bool> ValidateCheckConstraint(Expression expression, Row row) =>
            _constraintValidator.ValidateCheckConstraint(expression, row);

        public Task<bool> ValidateForeignKey(ForeignKeyReference reference, List<string> sourceColumns, Row row) =>
            _constraintValidator.ValidateForeignKey(reference, sourceColumns, row);

        public string ResolvePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;

            // Strip surrounding double-quotes that Windows "Copy as path" adds (e.g. "C:\tmp\file.csv")
            if (path.Length >= 2 && path[0] == '"' && path[^1] == '"')
                path = path[1..^1];

            string resolved = path;
            var parts = path.Split(new[] { '/', '\\' }, 2);
            var connName = parts[0];
            if (_connections.TryGetValue(connName, out var ds))
            {
                var baseUri = ds.Path;
                if (!string.IsNullOrEmpty(baseUri) && baseUri != "MSSQL" && baseUri != "POSTGRES" && baseUri != "MYSQL" && baseUri != "SQLITE" && baseUri != "ORACLE")
                {
                    if (parts.Length > 1) resolved = Path.Combine(baseUri, parts[1]);
                    else resolved = baseUri;
                }
            }

            // Security Hardening: Always return full paths and validate
            // If the path contains a placeholder, we skip full-path resolution to avoid breaking the placeholder
            if (resolved.Contains("${"))
            {
                return resolved;
            }

            string basePath = WorkingDirectory;
            if (!string.IsNullOrEmpty(CurrentScriptPath) &&
                !BundleUri.TryParse(CurrentScriptPath, out _))
            {
                var scriptDir = Path.GetDirectoryName(CurrentScriptPath);
                if (!string.IsNullOrEmpty(scriptDir)) basePath = scriptDir;
            }

            var fullPath = Path.IsPathRooted(resolved)
                ? Path.GetFullPath(resolved)
                : Path.GetFullPath(resolved, basePath);

            // Canonicalize symlinks so callers always receive the real path
            fullPath = ETL_SQL.Services.SecurityService.ResolvePathSymlinks(fullPath);

            if (_securityService != null)
            {
                _securityService.ValidatePath(fullPath);
                // Security Hardening: We removed ValidateFileType from ResolvePath because it was causing 
                // false positives for non-data-source operations like RUN SCRIPT. 
                // Data connectors now perform their own explicit file type validation.
            }
            else
            {
                // Internal test fallback: Log a warning if the service is missing
                _logger.Debug("Security validation skipped for path {Path}; SecurityService not initialized", fullPath);
            }
            
            return fullPath;
        }
    }
}
