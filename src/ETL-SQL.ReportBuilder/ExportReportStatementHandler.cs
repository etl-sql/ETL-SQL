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

            // Validate script-supplied PDF options before the report runs or a browser starts.
            var isPdf = string.Equals(stmt.Format, "PDF", StringComparison.OrdinalIgnoreCase);
            var pdfOptions = isPdf ? await BuildPdfExportOptionsAsync(stmt, context) : null;

            bool exists = await Task.Run(() => File.Exists(reportPath));
            if (!exists)
                throw new ExecutionException($"EXPORT REPORT: report file not found: '{reportPath}'");

            // ── Run the .rptsql in a nested scope of the current context ───────
            var source = await File.ReadAllTextAsync(reportPath);
            var tokens = new Lexer(source).Tokenize();
            var script = new Parser(tokens, source).Parse();

            // The report builds from its own definitions only: the caller's report state (and any
            // earlier EXPORT REPORT's) is set aside and restored once the manifest is built.
            string? oldPath = context.CurrentScriptPath;
            var callerReportState = ReportContextSnapshot.Capture(context.ReportContext);
            context.ReportContext.Clear();
            context.ReportContext.ReportTitleIsMarkdown = false;
            context.ReportContext.BaselineParameters.Clear();
            ReportManifest manifest;
            context.CurrentScriptPath = Path.GetFullPath(reportPath);
            var reportVars = new System.Collections.Generic.Dictionary<string, object?>(context.VarContext.CurrentVariables, StringComparer.OrdinalIgnoreCase);
            context.VarContext.PushScope(reportVars, new System.Collections.Generic.Dictionary<string, VariableMetadata>(StringComparer.OrdinalIgnoreCase));
            try
            {
                await context.Evaluate(script);

                // ── Build manifest from the report's own state ─────────────────
                var builder = new ManifestBuilder(context);
                manifest = await builder.BuildAsync(reportPath);
            }
            finally
            {
                context.VarContext.PopScope();
                context.CurrentScriptPath = oldPath;
                callerReportState.RestoreInto(context.ReportContext);
            }

            // ── Write the export file ──────────────────────────────────────────
            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir)) await Task.Run(() => Directory.CreateDirectory(dir));

            switch (stmt.Format.ToUpperInvariant())
            {
                case "PDF":
                    var pdfBytes = await new ReportPdfExporter(logger: logger).ExportAsync(manifest, pdfOptions, context.CancellationToken);
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

        private static async Task<PdfExportOptions> BuildPdfExportOptionsAsync(ExportReportStatement stmt, IExecutionContext context)
        {
            var host = stmt.Host == null ? null : (await context.EvaluateValue(stmt.Host, new Row()))?.ToString();
            var browserPath = stmt.BrowserPath == null ? null : (await context.EvaluateValue(stmt.BrowserPath, new Row()))?.ToString();

            if (!string.IsNullOrWhiteSpace(host))
            {
                if (!BrowserReportPdfExporter.IsNavigableHost(host))
                    throw new ExecutionException("EXPORT REPORT: HOST must be an absolute http or https URL.");
                try
                {
                    context.SecurityService.ValidateHost(new Uri(host).Host);
                }
                catch (ETL_SQL.Services.SecurityException ex)
                {
                    throw new ExecutionException($"EXPORT REPORT: HOST is not an allowed host. {ex.Message}");
                }
            }

            // A script may choose among the browsers the engine itself would discover; it may not
            // name an arbitrary executable, because the exporter starts that file as a process.
            if (!string.IsNullOrWhiteSpace(browserPath) && !BrowserReportPdfExporter.IsInstalledBrowser(browserPath))
                throw new ExecutionException(
                    "EXPORT REPORT: BROWSER_PATH must name an installed Chrome, Edge, or Chromium found on PATH or in a standard install location.");

            return new PdfExportOptions
            {
                Mode = ParsePdfMode(stmt.PdfMode),
                Host = host,
                BrowserPath = browserPath,
                Warn = message => context.Log($"EXPORT REPORT: {message}", ConsoleColor.Yellow)
            };
        }

        /// <summary>A copy of every script-owned value on an <see cref="IReportContext"/>.</summary>
        private sealed class ReportContextSnapshot
        {
            private readonly Action<IReportContext> _restore;

            private ReportContextSnapshot(Action<IReportContext> restore) => _restore = restore;

            public static ReportContextSnapshot Capture(IReportContext c)
            {
                var visuals = Copy(c.VisualDefinitions);
                var pages = Copy(c.PageDefinitions);
                var datasets = Copy(c.DatasetDefinitions);
                var containers = Copy(c.ContainerDefinitions);
                var navigation = Copy(c.NavigationDefinitions);
                var styles = Copy(c.StyleDefinitions);
                var buttons = Copy(c.ButtonDefinitions);
                var bookmarks = Copy(c.BookmarkDefinitions);
                var templates = Copy(c.TemplateDefinitions);
                var themes = Copy(c.ThemeDefinitions);
                var baseline = Copy(c.BaselineParameters);
                var (templatePath, title, titleIsMarkdown, description) = (c.TemplatePath, c.ReportTitle, c.ReportTitleIsMarkdown, c.ReportDescription);
                var (css, js, head, body, footer) = (c.ReportCss, c.ReportJs, c.ReportHtmlHead, c.ReportHtmlBody, c.ReportHtmlFooter);
                var (favicon, logo, background, theme, nav) = (c.ReportFavicon, c.ReportLogo, c.ReportBackground, c.ReportTheme, c.ReportNavigation);
                var (timeZone, locale, nullLabel) = (c.ReportTimeZone, c.ReportLocale, c.ReportNullLabel);

                return new ReportContextSnapshot(target =>
                {
                    target.Clear();
                    Fill(target.VisualDefinitions, visuals);
                    Fill(target.PageDefinitions, pages);
                    Fill(target.DatasetDefinitions, datasets);
                    Fill(target.ContainerDefinitions, containers);
                    Fill(target.NavigationDefinitions, navigation);
                    Fill(target.StyleDefinitions, styles);
                    Fill(target.ButtonDefinitions, buttons);
                    Fill(target.BookmarkDefinitions, bookmarks);
                    Fill(target.TemplateDefinitions, templates);
                    Fill(target.ThemeDefinitions, themes);
                    target.BaselineParameters.Clear();
                    Fill(target.BaselineParameters, baseline);
                    target.TemplatePath = templatePath;
                    target.ReportTitle = title;
                    target.ReportTitleIsMarkdown = titleIsMarkdown;
                    target.ReportDescription = description;
                    target.ReportCss = css;
                    target.ReportJs = js;
                    target.ReportHtmlHead = head;
                    target.ReportHtmlBody = body;
                    target.ReportHtmlFooter = footer;
                    target.ReportFavicon = favicon;
                    target.ReportLogo = logo;
                    target.ReportBackground = background;
                    target.ReportTheme = theme;
                    target.ReportNavigation = nav;
                    target.ReportTimeZone = timeZone;
                    target.ReportLocale = locale;
                    target.ReportNullLabel = nullLabel;
                });
            }

            public void RestoreInto(IReportContext target) => _restore(target);

            private static System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, T>> Copy<T>(
                System.Collections.Generic.IDictionary<string, T> source) => new(source);

            private static void Fill<T>(System.Collections.Generic.IDictionary<string, T> target,
                System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, T>> entries)
            {
                foreach (var entry in entries) target[entry.Key] = entry.Value;
            }
        }

        private static PdfExportMode ParsePdfMode(string? mode) =>
            (mode ?? "STATIC").ToUpperInvariant() switch
            {
                "STATIC" => PdfExportMode.Static,
                "AUTO" => PdfExportMode.Auto,
                "HOSTED" => PdfExportMode.Hosted,
                "BROWSER" => PdfExportMode.Browser,
                _ => throw new ExecutionException($"EXPORT REPORT: unsupported PDF_MODE '{mode}'")
            };
    }
}
