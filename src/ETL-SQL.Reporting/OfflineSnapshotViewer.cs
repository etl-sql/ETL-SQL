using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace ETL_SQL.Reporting
{
    /// <summary>
    /// Renders a <see cref="ReportManifest"/> as one self-contained HTML file that opens a report
    /// with no server, no package reader, and no network.
    ///
    /// <para>The shared browser runtime has carried an offline branch for some time — a bookmark
    /// applies from the manifest's precomputed envelope, detail popovers read rows the manifest
    /// already holds — but nothing shipped that actually set <c>window.__ETLSNAP__</c>, so the
    /// behaviour was implemented and tested and never reachable by a reader. This is the host that
    /// makes it reachable: <c>etl-sql-report offline</c> reads the encrypted <c>.etlsnap</c> package
    /// on the machine that is entitled to decrypt it, and writes the result as a page anyone can
    /// open.</para>
    ///
    /// <para>Self-contained is enforced, not merely intended. The runtime, its stylesheet, the
    /// feedback surface, and the manifest are inlined, and the page declares a
    /// <c>default-src 'none'</c> Content-Security-Policy so a viewer cannot silently degrade into
    /// something that only works while a server happens to be reachable.</para>
    /// </summary>
    public static class OfflineSnapshotViewer
    {
        private const string RuntimeScriptResource = "runtime.report-runtime.js";
        private const string RuntimeStyleResource = "runtime.report-runtime.css";
        private const string FeedbackScriptResource = "runtime.feedback.js";

        /// <summary>
        /// U+2028 and U+2029. Written as code points rather than escapes because both are line
        /// terminators in C# source as well as in JavaScript, so a literal one here would end the
        /// line it appears on.
        /// </summary>
        private static readonly string LineSeparator = char.ConvertFromUtf32(0x2028);
        private static readonly string ParagraphSeparator = char.ConvertFromUtf32(0x2029);

        /// <summary>
        /// Blocks every network origin, including the page's own. Inline script and style are the
        /// only executable content, and they are the content the exporter wrote.
        ///
        /// <para><c>frame-ancestors</c> is deliberately absent: it is ignored when a policy is
        /// delivered in a <c>meta</c> element, and every viewer would log a console error for a
        /// directive that was never going to take effect. There is no response header to put it in —
        /// the file is opened from disk.</para>
        /// </summary>
        private const string ContentSecurityPolicy =
            "default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; "
            + "img-src data: blob:; font-src data:; connect-src 'none'; form-action 'none'; "
            + "base-uri 'none'";

        /// <summary>
        /// Builds the viewer document.
        /// </summary>
        /// <param name="manifest">The snapshot's manifest, rows included.</param>
        /// <param name="capturedAtUtc">
        /// When the snapshot was taken, surfaced to the reader. Frozen figures that do not say when
        /// they froze are the failure mode an offline viewer is most likely to cause.
        /// </param>
        public static string Build(ReportManifest manifest, DateTimeOffset? capturedAtUtc = null)
        {
            if (manifest is null) throw new ArgumentNullException(nameof(manifest));

            // The same projection the Portal and the Player send to a browser: a stored snapshot must
            // not hand the page server-only properties a freshly built manifest would have dropped.
            var manifestJson = ScriptSafe(BrowserDeliveryProjection.Serialize(manifest));
            var title = string.IsNullOrWhiteSpace(manifest.Title) ? "ETL-SQL report snapshot" : manifest.Title!;
            var captured = (capturedAtUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();

            var html = new StringBuilder(1 << 20);
            html.Append("<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n");
            html.Append("<meta charset=\"utf-8\">\n");
            html.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">\n");
            html.Append("<meta http-equiv=\"Content-Security-Policy\" content=\"").Append(ContentSecurityPolicy).Append("\">\n");
            html.Append("<meta name=\"generator\" content=\"etl-sql-report offline\">\n");
            html.Append("<title>").Append(HtmlEscape(title)).Append("</title>\n");
            html.Append("<style>\n").Append(ReadAsset(RuntimeStyleResource)).Append("\n</style>\n");
            html.Append("</head>\n<body>\n");
            html.Append("<div id=\"root\"></div>\n");

            html.Append("<script>\n");
            html.Append("window.__ETLSNAP__ = true;\n");
            html.Append("window.__SNAPSHOT_CAPTURED_AT__ = \"")
                .Append(captured.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture))
                .Append("\";\n");
            html.Append("window.__MANIFEST__ = ").Append(manifestJson).Append(";\n");
            html.Append("</script>\n");

            html.Append("<script>\n").Append(ReadAsset(FeedbackScriptResource)).Append("\n</script>\n");
            html.Append("<script>\n").Append(ReadAsset(RuntimeScriptResource)).Append("\n</script>\n");
            html.Append("</body>\n</html>\n");
            return html.ToString();
        }

        /// <summary>
        /// Neutralises the sequences that would end the surrounding script element early or break the
        /// enclosing statement. Serialized report content is author-controlled, so a title containing
        /// a closing tag would otherwise close the block and turn the rest of the manifest into
        /// markup; U+2028 and U+2029 are valid inside a JSON string but terminate a JavaScript line.
        /// </summary>
        private static string ScriptSafe(string json) =>
            json.Replace("<", "\\u003c", StringComparison.Ordinal)
                .Replace(LineSeparator, "\\u2028", StringComparison.Ordinal)
                .Replace(ParagraphSeparator, "\\u2029", StringComparison.Ordinal);

        private static string HtmlEscape(string value) =>
            value.Replace("&", "&amp;", StringComparison.Ordinal)
                 .Replace("<", "&lt;", StringComparison.Ordinal)
                 .Replace(">", "&gt;", StringComparison.Ordinal);

        private static string ReadAsset(string logicalName)
        {
            var assembly = typeof(OfflineSnapshotViewer).GetTypeInfo().Assembly;
            using var stream = assembly.GetManifestResourceStream(logicalName)
                ?? throw new InvalidOperationException(
                    $"Embedded browser runtime asset '{logicalName}' is missing. Run scripts/sync-assets.ps1 "
                    + "and rebuild ETL-SQL.Reporting.");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var text = reader.ReadToEnd();

            // An asset that grew a closing tag would break out of the element it is inlined into, and
            // the page would still render enough to look like it worked.
            if (text.Contains("</script", StringComparison.OrdinalIgnoreCase)
                || text.Contains("</style", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Browser runtime asset '{logicalName}' contains a closing tag and cannot be inlined.");
            }

            return text;
        }
    }
}
