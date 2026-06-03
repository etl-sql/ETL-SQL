using System;
using System.Collections.Generic;
using System.Text.Json;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Engine.Lineage
{
    /// <summary>
    /// Reverse of <see cref="OpenLineageExporter"/>: parses OpenLineage RunEvent JSON (a .jsonl file
    /// may contain one RunEvent per line) back into <see cref="LineageEntry"/> rows so previously
    /// exported lineage can be re-imported via CREATE LINEAGE ... FROM.
    ///
    /// Round-trip notes (inherent to the OpenLineage shape we emit):
    /// - One entry is produced per (output table, column) with paired source table/column lists,
    ///   so multi-source columns survive the lineage-tracker's location-based dedup.
    /// - Tags are emitted by the exporter at the output (table) level, so re-imported tags land as
    ///   table-level metadata even if they originated on a specific column.
    /// - TransformationKind is reconstructed best-effort from the OpenLineage transformation subtype.
    /// </summary>
    public static class OpenLineageImporter
    {
        public static List<LineageEntry> Import(string content)
        {
            var entries = new List<LineageEntry>();
            if (string.IsNullOrWhiteSpace(content)) return entries;

            foreach (var rawLine in content.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length == 0) continue;

                JsonDocument doc;
                try
                {
                    doc = JsonDocument.Parse(line);
                }
                catch (JsonException ex)
                {
                    throw new ExecutionException($"Invalid OpenLineage JSON: {ex.Message}");
                }

                using (doc)
                {
                    ImportRunEvent(doc.RootElement, entries);
                }
            }

            return entries;
        }

        private static void ImportRunEvent(JsonElement root, List<LineageEntry> entries)
        {
            if (root.ValueKind != JsonValueKind.Object) return;
            if (!root.TryGetProperty("outputs", out var outputs) || outputs.ValueKind != JsonValueKind.Array) return;

            foreach (var output in outputs.EnumerateArray())
            {
                if (!output.TryGetProperty("name", out var nameEl)) continue;
                var table = nameEl.GetString();
                if (string.IsNullOrEmpty(table)) continue;

                if (!output.TryGetProperty("facets", out var facets) || facets.ValueKind != JsonValueKind.Object)
                    continue;

                ImportColumnLineage(table, facets, entries);
                ImportTableTags(table, facets, entries);
            }
        }

        private static void ImportColumnLineage(string table, JsonElement facets, List<LineageEntry> entries)
        {
            if (!facets.TryGetProperty("columnLineage", out var colLin) || colLin.ValueKind != JsonValueKind.Object) return;
            if (!colLin.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Object) return;

            foreach (var field in fields.EnumerateObject())
            {
                var srcTables = new List<string>();
                var srcColumns = new List<string>();
                var kind = TransformationKind.Unknown;
                string? transformExpr = null;

                if (field.Value.TryGetProperty("inputFields", out var inputs) && inputs.ValueKind == JsonValueKind.Array)
                {
                    foreach (var inp in inputs.EnumerateArray())
                    {
                        var st = inp.TryGetProperty("name", out var n) ? n.GetString() : null;
                        if (string.IsNullOrEmpty(st)) continue;

                        var sc = inp.TryGetProperty("field", out var f) ? f.GetString() : null;
                        srcTables.Add(st);
                        srcColumns.Add(sc ?? string.Empty);

                        if (kind == TransformationKind.Unknown
                            && inp.TryGetProperty("transformations", out var trs)
                            && trs.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var tr in trs.EnumerateArray())
                            {
                                var subtype = tr.TryGetProperty("subtype", out var sub) ? sub.GetString() : null;
                                kind = ReverseTransform(subtype);
                                if (tr.TryGetProperty("description", out var d))
                                {
                                    var ds = d.GetString();
                                    if (!string.IsNullOrEmpty(ds)) transformExpr = ds;
                                }
                                break;
                            }
                        }
                    }
                }

                entries.Add(new LineageEntry(table, "IMPORTED")
                {
                    TargetColumn = field.Name,
                    SourceTables = srcTables,
                    SourceColumns = srcColumns,
                    TransformationKind = kind,
                    TransformationExpression = transformExpr
                });
            }
        }

        private static void ImportTableTags(string table, JsonElement facets, List<LineageEntry> entries)
        {
            if (!facets.TryGetProperty("tags", out var tagsFacet) || tagsFacet.ValueKind != JsonValueKind.Object) return;
            if (!tagsFacet.TryGetProperty("tags", out var tagArr) || tagArr.ValueKind != JsonValueKind.Array) return;

            var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tag in tagArr.EnumerateArray())
            {
                var k = tag.TryGetProperty("name", out var nm) ? nm.GetString() : null;
                if (string.IsNullOrEmpty(k)) continue;
                var v = tag.TryGetProperty("value", out var vv) ? vv.GetString() : null;
                meta[k] = v ?? string.Empty;
            }

            if (meta.Count > 0)
                entries.Add(new LineageEntry(table, "IMPORTED") { Metadata = meta });
        }

        /// <summary>Best-effort reverse of <c>OpenLineageExporter.MapTransformationType</c>.</summary>
        private static TransformationKind ReverseTransform(string? subtype) => subtype?.ToUpperInvariant() switch
        {
            "IDENTITY" => TransformationKind.PassThrough,
            "FUNCTION" => TransformationKind.FunctionCall,
            "CONDITIONAL" => TransformationKind.Conditional,
            "ARITHMETIC" => TransformationKind.Arithmetic,
            "AGGREGATE" => TransformationKind.Aggregation,
            "WINDOW" => TransformationKind.WindowFunction,
            "LITERAL" => TransformationKind.Literal,
            "SUBQUERY" => TransformationKind.Subquery,
            "CAST" => TransformationKind.Cast,
            "UNKNOWN" => TransformationKind.Unknown,
            null => TransformationKind.Unknown,
            _ => Enum.TryParse<TransformationKind>(subtype, true, out var k) ? k : TransformationKind.Unknown
        };
    }
}
