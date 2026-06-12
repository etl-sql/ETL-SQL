using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the CONVERT FILE ENCODING statement.
    /// </summary>
    public class ConvertFileEncodingStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(ConvertFileEncodingStatement);

        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (ConvertFileEncodingStatement)statement;

            string srcVal = (await context.EvaluateValue(stmt.Source, new Row()))?.ToString() ?? "";
            string destVal = (await context.EvaluateValue(stmt.Destination, new Row()))?.ToString() ?? "";
            string fromEncVal = (await context.EvaluateValue(stmt.FromEncoding, new Row()))?.ToString() ?? "";
            string toEncVal = (await context.EvaluateValue(stmt.ToEncoding, new Row()))?.ToString() ?? "";

            string source = context.ResolvePath(srcVal);
            string dest = context.ResolvePath(destVal);

            // Security check
            context.SecurityService.ValidatePath(source);
            context.SecurityService.ValidatePath(dest);
            context.SecurityService.ValidateWriteAccess(dest);
            context.SecurityService.ValidateFileType(dest);

            // Runaway operation count
            context.IncrementOperationCount(OperationType.FileSystem, source, 1);

            if (context.IsWhatIf)
            {
                context.Log($"WHAT IF: Would convert file encoding of '{source}' ({fromEncVal}) to '{dest}' ({toEncVal})", ConsoleColor.Yellow);
                return;
            }

            bool overwrite = true;
            if (stmt.Overwrite != null)
            {
                var ovr = await context.EvaluateValue(stmt.Overwrite, new Row());
                if (ovr != null)
                {
                    if (ovr is bool b) overwrite = b;
                    else if (string.Equals(ovr.ToString(), "OFF", StringComparison.OrdinalIgnoreCase)) overwrite = false;
                    else if (string.Equals(ovr.ToString(), "FALSE", StringComparison.OrdinalIgnoreCase)) overwrite = false;
                }
            }

            if (!File.Exists(source))
                throw new ExecutionException($"Source file not found: {source}", null, stmt.Line, stmt.Column);

            if (File.Exists(dest))
            {
                if (overwrite) File.Delete(dest);
                else throw new ExecutionException($"Destination file already exists and OVERWRITE is OFF: {dest}", null, stmt.Line, stmt.Column);
            }

            Encoding fromEncoding = ParseEncoding(fromEncVal);
            Encoding toEncoding = ParseEncoding(toEncVal);

            if (context.IsVerbose)
                context.Log($"[ConvertEncoding] Converting '{source}' ({fromEncoding.EncodingName}) -> '{dest}' ({toEncoding.EncodingName})");

            using (var reader = new StreamReader(source, fromEncoding))
            using (var writer = new StreamWriter(dest, false, toEncoding))
            {
                char[] buffer = new char[8192];
                int read;
                while ((read = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    context.CancellationToken.ThrowIfCancellationRequested();
                    await writer.WriteAsync(buffer, 0, read);
                }
            }

            if (context.IsVerbose)
                context.Log($"[ConvertEncoding] Conversion completed successfully.");
        }

        private Encoding ParseEncoding(string encName)
        {
            try
            {
                return encName.ToUpperInvariant() switch
                {
                    "UTF8" or "UTF-8" => Encoding.UTF8,
                    "ANSI" or "LATIN1" or "ISO-8859-1" => Encoding.GetEncoding("ISO-8859-1"),
                    "UTF16" or "UTF-16" or "UNICODE" => Encoding.Unicode,
                    "UTF16BE" or "UTF-16BE" => Encoding.BigEndianUnicode,
                    "UTF32" or "UTF-32" => Encoding.UTF32,
                    "ASCII" => Encoding.ASCII,
                    _ => Encoding.GetEncoding(encName)
                };
            }
            catch (Exception ex)
            {
                throw new ExecutionException($"Unsupported or invalid encoding: '{encName}'. Details: {ex.Message}");
            }
        }
    }
}
