using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Reporting;

namespace ETL_SQL.ReportBuilder
{
    /// <summary>
    /// Handles: EXPORT REPORT 'path.rptsql' FORMAT PDF|CSV|MARKDOWN TO 'output.ext'
    ///
    /// Runs the report script in a nested scope of the current evaluator, builds a
    /// ReportManifest, then writes the rendered file. The Orchestrator owns scheduling;
    /// this handler is pure engine-level work.
    /// </summary>
    public class ExportReportStatementHandler(ILogger logger) : IStatementHandler
    {
        public Type SupportedStatementType => typeof(ExportReportStatement);

        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (ExportReportStatement)statement;

            var reportPathRaw = (await context.EvaluateValue(stmt.ReportPath, new Row()))?.ToString()
                ?? throw new ExecutionException("EXPORT REPORT: report path evaluated to null");

            var outputPathRaw = (await context.EvaluateValue(stmt.OutputPath, new Row()))?.ToString()
                ?? throw new ExecutionException("EXPORT REPORT: output path evaluated to null");

            var reportPath = context.ResolvePath(reportPathRaw);
            var outputPath = context.ResolvePath(outputPathRaw);

            if (!File.Exists(reportPath))
                throw new ExecutionException($"EXPORT REPORT: report file not found: '{reportPath}'");

            // ── Run the .rptsql in a nested scope of the current context ───────
            var source = await File.ReadAllTextAsync(reportPath);
            var tokens = new Lexer(source).Tokenize();
            var script = new Parser(tokens, source).Parse();

            string? oldPath = context.CurrentScriptPath;
            context.CurrentScriptPath = Path.GetFullPath(reportPath);
            context.VarContext.PushScope(new System.Collections.Generic.Dictionary<string, object?>(),
                              new System.Collections.Generic.Dictionary<string, VariableMetadata>());
            try
            {
                await context.Evaluate(script);
            }
            finally
            {
                context.VarContext.PopScope();
                context.CurrentScriptPath = oldPath;
            }

            // ── Build manifest from current context state ──────────────────────
            var builder  = new ManifestBuilder(context);
            var manifest = await builder.BuildAsync(reportPath);

            // ── Write the export file ──────────────────────────────────────────
            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            switch (stmt.Format.ToUpperInvariant())
            {
                case "PDF":
                    var pdfBytes = new PdfExporter().Export(manifest);
                    await File.WriteAllBytesAsync(outputPath, pdfBytes);
                    break;

                case "CSV":
                    await File.WriteAllTextAsync(outputPath, new CsvRenderer().Render(manifest), Encoding.UTF8);
                    break;

                case "MARKDOWN":
                    await File.WriteAllTextAsync(outputPath, new MarkdownRenderer().Render(manifest), Encoding.UTF8);
                    break;

                default:
                    throw new ExecutionException($"EXPORT REPORT: unsupported format '{stmt.Format}'");
            }

            logger.Debug("EXPORT REPORT: wrote {Format} to '{Output}'", stmt.Format, outputPath);
        }
    }
}
