using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;

namespace ETL_SQL.Engine.Lineage
{
    /// <summary>
    /// Serializes ETL-SQL lineage data to the OpenLineage RunEvent format (Linux Foundation standard).
    /// Supports file (.jsonl append) and HTTP endpoint export modes.
    /// </summary>
    public static class OpenLineageExporter
    {
        private static readonly HttpClient _http = new();

        public static async Task ExportToFileAsync(
            ILineageTracker tracker,
            string sessionId,
            string? scriptName,
            string filePath,
            ILogger logger,
            CancellationToken ct = default)
        {
            try
            {
                var json = BuildRunEvent(tracker, sessionId, scriptName);
                var dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                await File.AppendAllTextAsync(filePath, json + "\n", ct);
                logger.Info("OpenLineage event appended to: {Path}", filePath);
            }
            catch (Exception ex)
            {
                logger.Warning("OpenLineage file export failed: {Message}", ex.Message);
            }
        }

        public static async Task ExportToHttpAsync(
            ILineageTracker tracker,
            string sessionId,
            string? scriptName,
            string endpoint,
            ILogger logger,
            CancellationToken ct = default)
        {
            try
            {
                var json = BuildRunEvent(tracker, sessionId, scriptName);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _http.PostAsync(endpoint, content, ct);
                if (!response.IsSuccessStatusCode)
                    logger.Warning("OpenLineage HTTP export returned {Status}: {Endpoint}", (int)response.StatusCode, endpoint);
            }
            catch (Exception ex)
            {
                logger.Warning("OpenLineage HTTP export failed: {Message}", ex.Message);
            }
        }

        /// <summary>
        /// Builds an OpenLineage RunEvent JSON string from the current lineage tracker state.
        /// The result is a single-line JSON object suitable for .jsonl files.
        /// </summary>
        public static string BuildRunEvent(ILineageTracker tracker, string sessionId, string? scriptName)
        {
            var entries = tracker.GetFullLineage().ToList();
            var now = DateTime.UtcNow.ToString("O");
            var runId = Guid.NewGuid().ToString();
            var jobName = scriptName ?? "etl-sql-script";

            // Index all namespace resolutions up-front
            var nsCache = new Dictionary<string, (string ns, string name)>(StringComparer.OrdinalIgnoreCase);
            var outputTables = new Dictionary<string, List<LineageEntry>>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in entries)
            {
                if (!outputTables.ContainsKey(entry.TargetTable))
                    outputTables[entry.TargetTable] = new List<LineageEntry>();
                outputTables[entry.TargetTable].Add(entry);

                foreach (var src in entry.SourceTables)
                {
                    if (!nsCache.ContainsKey(src))
                        nsCache[src] = ResolveNamespace(src, sessionId);
                }
            }
            foreach (var target in outputTables.Keys)
            {
                if (!nsCache.ContainsKey(target))
                    nsCache[target] = ResolveNamespace(target, sessionId);
            }

            // Pure source tables: appear as sources but never as targets
            var inputs = nsCache
                .Where(kv => !outputTables.ContainsKey(kv.Key))
                .Select(kv => $"{{\"namespace\":{JsonStr(kv.Value.ns)},\"name\":{JsonStr(kv.Value.name)}}}")
                .ToList();

            // Output tables with columnLineage and tag facets
            var outputs = new List<string>();
            foreach (var (target, targetEntries) in outputTables)
            {
                var (targetNs, targetName) = nsCache[target];
                var colLineageJson = BuildColumnLineageFacet(targetEntries, sessionId, nsCache);
                var tagsJson = BuildTagsFacet(targetEntries);

                var facetParts = new List<string>();
                if (colLineageJson != null) facetParts.Add(colLineageJson);
                if (tagsJson != null) facetParts.Add(tagsJson);

                var facets = facetParts.Count > 0
                    ? $",\"facets\":{{{string.Join(",", facetParts)}}}"
                    : "";
                outputs.Add($"{{\"namespace\":{JsonStr(targetNs)},\"name\":{JsonStr(targetName)}{facets}}}");
            }

            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append($"\"eventType\":\"COMPLETE\",");
            sb.Append($"\"eventTime\":{JsonStr(now)},");
            sb.Append($"\"producer\":\"https://etl-sql/openlineage\",");
            sb.Append($"\"schemaURL\":\"https://openlineage.io/spec/2-0-2/OpenLineage.json\",");
            sb.Append($"\"run\":{{");
            sb.Append($"\"runId\":{JsonStr(runId)},");
            sb.Append($"\"facets\":{{\"nominalTime\":{{");
            sb.Append($"\"_producer\":\"https://etl-sql/openlineage\",");
            sb.Append($"\"_schemaURL\":\"https://openlineage.io/spec/facets/1-0-0/NominalTimeRunFacet.json\",");
            sb.Append($"\"nominalStartTime\":{JsonStr(now)},");
            sb.Append($"\"nominalEndTime\":{JsonStr(now)}");
            sb.Append("}}},");
            sb.Append($"\"job\":{{\"namespace\":\"etl-sql\",\"name\":{JsonStr(jobName)}}},");
            sb.Append($"\"inputs\":[{string.Join(",", inputs)}],");
            sb.Append($"\"outputs\":[{string.Join(",", outputs)}]");
            sb.Append('}');
            return sb.ToString();
        }

        private static string? BuildColumnLineageFacet(
            List<LineageEntry> entries,
            string sessionId,
            Dictionary<string, (string ns, string name)> nsCache)
        {
            var byCol = entries
                .Where(e => e.TargetColumn != null)
                .GroupBy(e => e.TargetColumn!, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!byCol.Any()) return null;

            var fieldParts = new List<string>();
            foreach (var colGroup in byCol)
            {
                var inputFields = new List<string>();
                foreach (var entry in colGroup)
                {
                    if (!entry.SourceTables.Any()) continue;
                    foreach (var srcTable in entry.SourceTables)
                    {
                        if (!nsCache.TryGetValue(srcTable, out var srcNs))
                            srcNs = ResolveNamespace(srcTable, sessionId);

                        var srcCols = entry.SourceColumns.Any()
                            ? entry.SourceColumns
                            : new List<string> { colGroup.Key };

                        foreach (var srcCol in srcCols)
                        {
                            var (tType, tSubtype) = MapTransformationType(entry.TransformationKind);
                            var desc = entry.TransformationExpression ?? "";
                            var transformJson =
                                $"{{\"type\":{JsonStr(tType)},\"subtype\":{JsonStr(tSubtype)},\"description\":{JsonStr(desc)},\"masking\":false}}";
                            inputFields.Add(
                                $"{{\"namespace\":{JsonStr(srcNs.ns)},\"name\":{JsonStr(srcNs.name)},\"field\":{JsonStr(srcCol)},\"transformations\":[{transformJson}]}}");
                        }
                    }
                }

                if (inputFields.Any())
                    fieldParts.Add($"{JsonStr(colGroup.Key)}:{{\"inputFields\":[{string.Join(",", inputFields)}]}}");
            }

            if (!fieldParts.Any()) return null;

            return $"\"columnLineage\":{{" +
                   $"\"_producer\":\"https://etl-sql/openlineage\"," +
                   $"\"_schemaURL\":\"https://openlineage.io/spec/facets/1-0-0/ColumnLineageDatasetFacet.json\"," +
                   $"\"fields\":{{{string.Join(",", fieldParts)}}}}}";
        }

        private static string? BuildTagsFacet(List<LineageEntry> entries)
        {
            var allTags = entries
                .SelectMany(e => e.Metadata)
                .GroupBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.Last())
                .ToList();

            if (!allTags.Any()) return null;

            var tagItems = allTags.Select(kv =>
                $"{{\"name\":{JsonStr(kv.Key)},\"value\":{JsonStr(kv.Value)}}}");

            return $"\"tags\":{{" +
                   $"\"_producer\":\"https://etl-sql/openlineage\"," +
                   $"\"_schemaURL\":\"https://openlineage.io/spec/facets/1-0-0/TagsDatasetFacet.json\"," +
                   $"\"tags\":[{string.Join(",", tagItems)}]}}";
        }

        private static (string type, string subtype) MapTransformationType(TransformationKind kind)
            => kind switch
            {
                TransformationKind.PassThrough => ("DIRECT", "IDENTITY"),
                TransformationKind.Unknown => ("DIRECT", "UNKNOWN"),
                TransformationKind.FunctionCall => ("INDIRECT", "FUNCTION"),
                TransformationKind.CaseExpression => ("INDIRECT", "CONDITIONAL"),
                TransformationKind.Arithmetic => ("INDIRECT", "ARITHMETIC"),
                TransformationKind.StringOperation => ("INDIRECT", "FUNCTION"),
                TransformationKind.Aggregation => ("INDIRECT", "AGGREGATE"),
                TransformationKind.WindowFunction => ("INDIRECT", "WINDOW"),
                TransformationKind.Conditional => ("INDIRECT", "CONDITIONAL"),
                TransformationKind.Literal => ("INDIRECT", "LITERAL"),
                TransformationKind.Subquery => ("INDIRECT", "SUBQUERY"),
                TransformationKind.Cast => ("INDIRECT", "CAST"),
                _ => ("INDIRECT", kind.ToString().ToUpperInvariant())
            };

        internal static (string ns, string name) ResolveNamespace(string tableName, string sessionId)
        {
            if (tableName.StartsWith('#') || tableName.StartsWith('@'))
                return ($"etl-sql://session/{sessionId}", tableName);
            if (tableName.StartsWith("report:", StringComparison.OrdinalIgnoreCase))
            {
                var rName = tableName[7..];
                return ($"etl-sql://report/{rName}", rName);
            }
            if (tableName.StartsWith("dataset:", StringComparison.OrdinalIgnoreCase))
            {
                var dName = tableName[8..];
                return ($"etl-sql://dataset/{dName}", dName);
            }
            if (tableName.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                return ("file://", tableName[7..]);
            // External table: connectionAlias.schema.table or connectionAlias.table
            return ("etl-sql://external", tableName);
        }

        private static string JsonStr(string? s)
            => s == null ? "null" : JsonSerializer.Serialize(s);
    }
}
