using System;
using System.IO;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the WAITFOR FILE UNLOCKED statement, polling until the file exists and is not locked.
    /// </summary>
    public class WaitForFileStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(WaitForFileStatement);

        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (WaitForFileStatement)statement;

            var rawPath = (await context.EvaluateValue(stmt.Path, new Row()))?.ToString() ?? "";
            if (string.IsNullOrEmpty(rawPath))
                throw new ExecutionException("WAITFOR FILE UNLOCKED requires a non-empty file path.", null, stmt.Line, stmt.Column);

            string resolvedPath = context.ResolvePath(rawPath);

            // Security validation
            context.SecurityService.ValidatePath(resolvedPath);

            int timeoutSec = 30; // Default timeout
            if (stmt.Timeout != null)
            {
                var tVal = await context.EvaluateValue(stmt.Timeout, new Row());
                if (tVal != null && int.TryParse(tVal.ToString(), out var tSec))
                    timeoutSec = tSec;
            }

            int pollIntervalMs = 500; // Default poll interval
            if (stmt.PollInterval != null)
            {
                var pVal = await context.EvaluateValue(stmt.PollInterval, new Row());
                if (pVal != null && int.TryParse(pVal.ToString(), out var pMs))
                    pollIntervalMs = pMs;
            }

            if (context.IsVerbose)
                context.Log($"[WaitForFile] Waiting for '{resolvedPath}' to arrive and unlock. Timeout: {timeoutSec}s, Poll: {pollIntervalMs}ms");

            var start = DateTime.UtcNow;
            var timeout = TimeSpan.FromSeconds(timeoutSec);
            bool success = false;

            while (DateTime.UtcNow - start < timeout)
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                if (File.Exists(resolvedPath))
                {
                    try
                    {
                        using (var fs = File.Open(resolvedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                        {
                            success = true;
                            break;
                        }
                    }
                    catch (IOException)
                    {
                        // File exists but is locked (in use by another process)
                    }
                }

                await Task.Delay(pollIntervalMs, context.CancellationToken);
            }

            if (!success)
            {
                throw new ExecutionException($"Timeout waiting for file to arrive and unlock: {resolvedPath}", null, stmt.Line, stmt.Column);
            }

            if (context.IsVerbose)
                context.Log($"[WaitForFile] File '{resolvedPath}' is unlocked and ready.");
        }
    }
}
