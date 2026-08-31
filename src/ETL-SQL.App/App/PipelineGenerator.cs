using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ETL_SQL.Common;

namespace ETL_SQL.App
{
    public class PipelineGenerator
    {
        public static async Task<int> Generate(string schemaPath, string outputPath, ILogger logger)
        {
            try
            {
                if (!File.Exists(schemaPath))
                {
                    logger.WriteLine($"Schema file not found: {schemaPath}", ConsoleColor.Red);
                    return 1;
                }

                logger.WriteLine($"Reading schema JSON: {schemaPath}");
                var jsonContent = await File.ReadAllTextAsync(schemaPath);

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
                };

                var spec = JsonSerializer.Deserialize<SpecPipeline>(jsonContent, options);
                if (spec == null)
                {
                    logger.WriteLine("Failed to deserialize the JSON schema specification.", ConsoleColor.Red);
                    return 1;
                }
                HydrateReviewMetadata(spec, jsonContent);

                var validationErrors = SpecPipelineValidator.Validate(spec);
                if (validationErrors.Count > 0)
                {
                    logger.WriteLine("Schema JSON does not match the ETL-SQL specification contract:", ConsoleColor.Red);
                    foreach (var error in validationErrors)
                    {
                        logger.WriteLine($"  - {error}", ConsoleColor.Red);
                    }
                    return 1;
                }

                var outputDir = Path.GetDirectoryName(outputPath) ?? "";
                if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                if (spec.Datasets != null && spec.Datasets.Count > 0)
                {
                    logger.WriteLine($"Compiling multi-dataset master pipeline: '{spec.PipelineName}' ({spec.Datasets.Count} datasets)");

                    var masterFileNameWithoutExtension = Path.GetFileNameWithoutExtension(outputPath);
                    var modulesDirName = $"{masterFileNameWithoutExtension}_modules";
                    var modulesDirPath = Path.Combine(outputDir, modulesDirName);

                    if (!Directory.Exists(modulesDirPath))
                    {
                        Directory.CreateDirectory(modulesDirPath);
                    }

                    // 1. Generate individual sub-modules
                    foreach (var ds in spec.Datasets)
                    {
                        var dsName = ds.Name ?? "unnamed_dataset";
                        logger.WriteLine($"  -> Compiling sub-module: '{dsName}'");

                        var subSpec = new SpecPipeline
                        {
                            PipelineName = dsName,
                            Metadata = spec.Metadata,
                            Source = ds.Source,
                            Destination = ds.Destination,
                            Schema = ds.Schema
                        };

                        var subEtlSql = CompilePipeline(subSpec, Path.GetFileName(schemaPath));
                        var subOutputPath = Path.Combine(modulesDirPath, $"{dsName}.etlsql");
                        // Defense in depth: even though dataset names are validated to a safe identifier
                        // format, confirm the normalized module path cannot escape the modules directory
                        // before writing.
                        EnsureWithinDirectory(modulesDirPath, subOutputPath);
                        await File.WriteAllTextAsync(subOutputPath, subEtlSql, Encoding.UTF8);
                    }

                    // 2. Generate Master Runner
                    var masterEtlSql = CompileMasterPipeline(spec, modulesDirName, Path.GetFileName(schemaPath));
                    await File.WriteAllTextAsync(outputPath, masterEtlSql, Encoding.UTF8);

                    logger.WriteLine($"Multi-dataset pipeline successfully compiled.", ConsoleColor.Green);
                    logger.WriteLine($"  Master Script: {outputPath}", ConsoleColor.Green);
                    logger.WriteLine($"  Modules Directory: {modulesDirPath}", ConsoleColor.Green);
                }
                else
                {
                    logger.WriteLine($"Compiling single-dataset pipeline: '{spec.PipelineName}'");
                    var etlSqlCode = CompilePipeline(spec, Path.GetFileName(schemaPath));
                    await File.WriteAllTextAsync(outputPath, etlSqlCode, Encoding.UTF8);
                    logger.WriteLine($"ETL-SQL script successfully generated: {outputPath}", ConsoleColor.Green);
                }

                return 0;
            }
            catch (Exception ex)
            {
                logger.WriteLine($"Error compiling pipeline: {ex.Message}", ConsoleColor.Red);
                logger.WriteLine(ex.ToString(), ConsoleColor.DarkGray);
                return 1;
            }
        }

        private static void HydrateReviewMetadata(SpecPipeline spec, string jsonContent)
        {
            using var document = JsonDocument.Parse(jsonContent, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

            var root = document.RootElement;
            HydrateReviewMetadata(spec, root);

            if (root.TryGetProperty("source", out var sourceElement) && spec.Source != null)
                HydrateReviewMetadata(spec.Source, sourceElement);

            if (root.TryGetProperty("destination", out var destinationElement) && spec.Destination != null)
                HydrateReviewMetadata(spec.Destination, destinationElement);

            if (root.TryGetProperty("schema", out var schemaElement) && schemaElement.ValueKind == JsonValueKind.Array && spec.Schema != null)
                HydrateColumnReviewMetadata(spec.Schema, schemaElement);

            if (root.TryGetProperty("datasets", out var datasetsElement) && datasetsElement.ValueKind == JsonValueKind.Array && spec.Datasets != null)
            {
                var count = Math.Min(spec.Datasets.Count, datasetsElement.GetArrayLength());
                for (var i = 0; i < count; i++)
                {
                    var dataset = spec.Datasets[i];
                    var datasetElement = datasetsElement[i];
                    HydrateReviewMetadata(dataset, datasetElement);

                    if (datasetElement.TryGetProperty("source", out var datasetSourceElement) && dataset.Source != null)
                        HydrateReviewMetadata(dataset.Source, datasetSourceElement);

                    if (datasetElement.TryGetProperty("destination", out var datasetDestinationElement) && dataset.Destination != null)
                        HydrateReviewMetadata(dataset.Destination, datasetDestinationElement);

                    if (datasetElement.TryGetProperty("schema", out var datasetSchemaElement) && datasetSchemaElement.ValueKind == JsonValueKind.Array && dataset.Schema != null)
                        HydrateColumnReviewMetadata(dataset.Schema, datasetSchemaElement);
                }
            }
        }

        private static void HydrateReviewMetadata(SpecPipeline spec, JsonElement element)
        {
            spec.Confidence = ReadConfidence(element);
            spec.SourceEvidence = ReadEvidence(element);
        }

        private static void HydrateReviewMetadata(SpecDataset spec, JsonElement element)
        {
            spec.Confidence = ReadConfidence(element);
            spec.SourceEvidence = ReadEvidence(element);
        }

        private static void HydrateReviewMetadata(SpecSource spec, JsonElement element)
        {
            spec.Confidence = ReadConfidence(element);
            spec.SourceEvidence = ReadEvidence(element);
        }

        private static void HydrateReviewMetadata(SpecDestination spec, JsonElement element)
        {
            spec.Confidence = ReadConfidence(element);
            spec.SourceEvidence = ReadEvidence(element);
        }

        private static void HydrateColumnReviewMetadata(List<SpecColumn> columns, JsonElement schemaElement)
        {
            var count = Math.Min(columns.Count, schemaElement.GetArrayLength());
            for (var i = 0; i < count; i++)
            {
                var column = columns[i];
                var columnElement = schemaElement[i];
                column.Confidence = ReadConfidence(columnElement);
                column.SourceEvidence = ReadEvidence(columnElement);
            }
        }

        private static double? ReadConfidence(JsonElement element)
        {
            if (element.TryGetProperty("confidence", out var confidenceElement) && confidenceElement.ValueKind == JsonValueKind.Number)
                return confidenceElement.GetDouble();

            return null;
        }

        private static List<SpecEvidence>? ReadEvidence(JsonElement element)
        {
            if (!element.TryGetProperty("source_evidence", out var evidenceElement) || evidenceElement.ValueKind != JsonValueKind.Array)
                return null;

            var evidence = new List<SpecEvidence>();
            foreach (var item in evidenceElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                evidence.Add(new SpecEvidence
                {
                    Document = ReadString(item, "document"),
                    Page = ReadInt(item, "page"),
                    Section = ReadString(item, "section"),
                    OriginalFieldName = ReadString(item, "original_field_name"),
                    Text = ReadString(item, "text")
                });
            }

            return evidence;
        }

        private static string? ReadString(JsonElement element, string propertyName)
            => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        private static int? ReadInt(JsonElement element, string propertyName)
            => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
                ? number
                : null;

        private static string CompileMasterPipeline(SpecPipeline spec, string modulesDirName, string specFileName)
        {
            var sb = new StringBuilder();
            var pipelineName = spec.PipelineName ?? "master_pipeline";
            var desc = spec.Metadata?.Description ?? "Master wrapper pipeline.";
            var owner = spec.Metadata?.Owner ?? "Data Team";
            var classification = spec.Metadata?.Classification ?? "internal";

            sb.AppendLine("-- =========================================================================");
            sb.AppendLine($"-- Master Pipeline: {pipelineName}");
            sb.AppendLine($"-- Description: {desc}");
            sb.AppendLine($"-- Owner: {owner}");
            sb.AppendLine($"-- Security Classification: {classification}");
            sb.AppendLine($"-- Generated from specification: {specFileName}");
            sb.AppendLine($"-- Generated at: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine("-- =========================================================================");
            sb.AppendLine();
            sb.AppendLine("BEGIN TRY");

            foreach (var ds in spec.Datasets!)
            {
                var dsName = ds.Name ?? "unnamed";
                // Escape every generated ETL-SQL string literal. Dataset names are already validated to a
                // safe identifier, so this is belt-and-suspenders against any future relaxation.
                sb.Indent(1).AppendLine($"PRINT 'Running specification module: {EscapeSqlString(dsName)}...';");
                sb.Indent(1).AppendLine($"RUN SCRIPT '{EscapeSqlString($"./{modulesDirName}/{dsName}.etlsql")}';");
                sb.Indent(1).AppendLine();
            }

            sb.Indent(1).AppendLine("PRINT 'All specification pipeline modules completed successfully.';");
            sb.AppendLine("END TRY");
            sb.AppendLine("BEGIN CATCH");
            sb.Indent(1).AppendLine("PRINT 'Specification pipeline execution aborted: ' + ERROR_MESSAGE();");
            sb.Indent(1).AppendLine("THROW;");
            sb.AppendLine("END CATCH");

            return sb.ToString();
        }

        private static string CompilePipeline(SpecPipeline spec, string specFileName)
        {
            var sb = new StringBuilder();
            var pipelineName = spec.PipelineName ?? "unnamed_pipeline";
            var desc = spec.Metadata?.Description ?? "Generated pipeline.";
            var owner = spec.Metadata?.Owner ?? "Data Team";
            var classification = spec.Metadata?.Classification ?? "internal";

            // 1. Header block
            sb.AppendLine("-- =========================================================================");
            sb.AppendLine($"-- Pipeline: {pipelineName}");
            sb.AppendLine($"-- Description: {desc}");
            sb.AppendLine($"-- Owner: {owner}");
            sb.AppendLine($"-- Security Classification: {classification}");
            sb.AppendLine($"-- Generated from specification: {specFileName}");
            sb.AppendLine($"-- Generated at: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine("-- =========================================================================");
            sb.AppendLine();

            // 2. Variable declarations and date handling for target filename
            var connType = spec.Destination?.ConnectorType ?? "FLATFILE";
            var format = spec.Destination?.Format ?? "CSV";
            var resolvedDestType = ResolveConnectorType(connType, format);

            var defaultExt = "csv";
            if (resolvedDestType.Equals("EXCEL", StringComparison.OrdinalIgnoreCase)) defaultExt = "xlsx";
            else if (resolvedDestType.Equals("JSON", StringComparison.OrdinalIgnoreCase)) defaultExt = "json";
            else if (resolvedDestType.Equals("XML", StringComparison.OrdinalIgnoreCase)) defaultExt = "xml";
            else if (resolvedDestType.Equals("PARQUET", StringComparison.OrdinalIgnoreCase)) defaultExt = "parquet";
            else if (resolvedDestType.Equals("AVRO", StringComparison.OrdinalIgnoreCase)) defaultExt = "avro";

            var namingPattern = spec.Destination?.NamingPattern ?? $"{pipelineName}_output.{defaultExt}";
            var hasDatePattern = namingPattern.Contains("{yyyy") || namingPattern.Contains("{HH");

            if (hasDatePattern)
            {
                sb.AppendLine("-- 2. DYNAMIC FILENAME GENERATION");
                var cleanPattern = namingPattern;
                if (namingPattern.Contains("{yyyyMMdd}"))
                {
                    cleanPattern = cleanPattern.Replace("{yyyyMMdd}", "' + @DateStr + '");
                    sb.AppendLine($"DECLARE @DateStr VARCHAR(8);");
                    sb.AppendLine($"SET @DateStr = FORMAT(GETDATE(), 'yyyyMMdd');");
                }
                if (namingPattern.Contains("{HHmmss}"))
                {
                    cleanPattern = cleanPattern.Replace("{HHmmss}", "' + @TimeStr + '");
                    sb.AppendLine($"DECLARE @TimeStr VARCHAR(6);");
                    sb.AppendLine($"SET @TimeStr = FORMAT(GETDATE(), 'HHmmss');");
                }
                if (namingPattern.Contains("{yyyyMMdd_HHmmss}"))
                {
                    cleanPattern = cleanPattern.Replace("{yyyyMMdd_HHmmss}", "' + @TimestampStr + '");
                    sb.AppendLine($"DECLARE @TimestampStr VARCHAR(15);");
                    sb.AppendLine($"SET @TimestampStr = FORMAT(GETDATE(), 'yyyyMMdd_HHmmss');");
                }

                sb.AppendLine($"DECLARE @FileName VARCHAR(100);");
                sb.AppendLine($"SET @FileName = '{cleanPattern}';");
                sb.AppendLine();
            }

            // 3. Outbound Destination Connection
            sb.AppendLine("-- 3. OUTBOUND DESTINATION CONNECTION");
            var path = spec.Destination?.Path ?? "outbound_dir";

            bool isFileDest = resolvedDestType.Equals("FLATFILE", StringComparison.OrdinalIgnoreCase)
                              || resolvedDestType.Equals("EXCEL", StringComparison.OrdinalIgnoreCase)
                              || resolvedDestType.Equals("JSON", StringComparison.OrdinalIgnoreCase)
                              || resolvedDestType.Equals("XML", StringComparison.OrdinalIgnoreCase)
                              || resolvedDestType.Equals("PARQUET", StringComparison.OrdinalIgnoreCase)
                              || resolvedDestType.Equals("AVRO", StringComparison.OrdinalIgnoreCase);

            if (isFileDest)
            {
                sb.AppendLine($"-- Base directory connection context (isolates physical drive paths)");
                sb.AppendLine($"CREATE CONNECTION target_dir AS DIRECTORY('{path}');");

                var fileRef = hasDatePattern ? "target_dir + '/' + @FileName" : $"target_dir + '/{namingPattern}'";

                if (resolvedDestType.Equals("FLATFILE", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine($"CREATE CONNECTION outbound_dest AS FLATFILE(");
                    sb.AppendLine($"    PATH = {fileRef},");
                    sb.AppendLine($"    FORMAT = '{format}',");

                    var hasHeader = spec.Destination?.HasHeader ?? true;
                    sb.AppendLine($"    HAS_HEADER = {(hasHeader ? "TRUE" : "FALSE")},");

                    var delim = spec.Destination?.Delimiter?.ToLower();
                    if (delim == "pipe")
                        sb.AppendLine("    DELIMITER = '|',");
                    else if (delim == "tab")
                        sb.AppendLine("    DELIMITER = '\\t',");
                    else if (delim == "comma")
                        sb.AppendLine("    DELIMITER = ',',");

                    var enc = spec.Destination?.Encoding ?? "UTF8";
                    sb.AppendLine($"    ENCODING = '{enc}'");
                    sb.AppendLine(");");
                }
                else if (resolvedDestType.Equals("EXCEL", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine($"CREATE CONNECTION outbound_dest AS EXCEL(");
                    sb.AppendLine($"    PATH = {fileRef},");
                    var sheetName = spec.Destination?.SheetName ?? "Sheet1";
                    sb.AppendLine($"    SHEET = '{EscapeSqlString(sheetName)}',");
                    var hasHeader = spec.Destination?.HasHeader ?? true;
                    sb.AppendLine($"    HEADER = {(hasHeader ? "ON" : "OFF")}");
                    sb.AppendLine(");");
                }
                else
                {
                    sb.AppendLine($"CREATE CONNECTION outbound_dest AS {resolvedDestType}(");
                    sb.AppendLine($"    PATH = {fileRef}");
                    sb.AppendLine(");");
                }
            }
            else
            {
                if (resolvedDestType.Equals("SQLITE", StringComparison.OrdinalIgnoreCase))
                {
                    // SQLite database connection template
                    sb.AppendLine($"-- CREATE CONNECTION outbound_dest AS SQLITE(");
                    sb.AppendLine($"--     DATABASE = '{path}'");
                    sb.AppendLine($"-- );");
                }
                else
                {
                    // Database connection template
                    sb.AppendLine($"-- CREATE CONNECTION outbound_dest AS {resolvedDestType}(");
                    sb.AppendLine($"--     SERVER = '...',");
                    sb.AppendLine($"--     DATABASE = '...',");
                    sb.AppendLine($"--     TABLE = '{path}'");
                    sb.AppendLine($"-- );");
                }
            }
            sb.AppendLine();

            // 4. Extract Block (User Contract)
            sb.AppendLine("-- =========================================================================");
            sb.AppendLine("-- 4. EXTRACT PHASE (USER CONTRACT)");
            sb.AppendLine("-- =========================================================================");
            sb.AppendLine("-- [USER TODO]: Define your source connection and query into #staging below.");
            sb.AppendLine("-- All columns must match the names defined in the schema validation below.");
            WriteEvidenceComments(sb, spec);
            WriteSourceContractComments(sb, spec);
            sb.AppendLine("/*");
            WriteSourceConnectionTemplate(sb, spec.Source);
            sb.AppendLine("SELECT ");
            if (spec.Schema != null && spec.Schema.Count > 0)
            {
                for (int i = 0; i < spec.Schema.Count; i++)
                {
                    var col = spec.Schema[i];
                    var comma = (i == spec.Schema.Count - 1) ? "" : ",";
                    var sourceName = string.IsNullOrWhiteSpace(col.SourceName) ? $"raw_{col.ColumnName}" : col.SourceName;
                    var extractionNote = GetExtractionNote(col);
                    sb.AppendLine($"    {sourceName,-28} AS {col.ColumnName}{extractionNote}{comma}");
                }
            }
            sb.AppendLine("INTO #staging");
            sb.AppendLine(GetSourceFromTemplate(spec.Source));
            sb.AppendLine("*/");
            sb.AppendLine();

            // 5. Ingestion Transaction & Checkpoints
            sb.AppendLine("BEGIN TRY");
            sb.Indent(1).AppendLine("-- 5. SCHEMATIC GATEKEEPER CHECK");
            sb.Indent(1).AppendLine("EXPECT SCHEMA #staging (");
            if (spec.Schema != null)
            {
                for (int i = 0; i < spec.Schema.Count; i++)
                {
                    var col = spec.Schema[i];
                    var typeStr = GetSqlTypeString(col);
                    var comma = (i == spec.Schema.Count - 1) ? "" : ",";
                    sb.Indent(2).AppendLine($"{col.ColumnName,-25} {typeStr}{comma}");
                }
            }
            sb.Indent(1).AppendLine(");");
            sb.AppendLine();

            // 6. Transforming, Casting, and Documenting Inline Tags
            sb.Indent(1).AppendLine("-- 6. TRANSFORMATION, CASTING & DATA QUALITY RULES");
            sb.Indent(1).AppendLine("SELECT");
            var rejectPolicy = spec.Source?.RejectPolicy?.ToLowerInvariant() ?? "fail_batch";
            if (spec.Schema != null)
            {
                for (int i = 0; i < spec.Schema.Count; i++)
                {
                    var col = spec.Schema[i];
                    var castExpr = GetCastingExpression(col);
                    var expectClause = GetExpectClause(col, rejectPolicy);
                    var comment = GetInlineComment(col);
                    var comma = (i == spec.Schema.Count - 1) ? "" : ",";
                    sb.Indent(2).AppendLine(
                        $"{castExpr,-50} AS {col.ColumnName,-25}{expectClause} {comment}{comma}".TrimEnd());
                }
            }
            sb.Indent(1).AppendLine("INTO #cleaned_data");
            sb.Indent(1).AppendLine("FROM #staging");

            var hasLookups = spec.Schema != null && spec.Schema.Any(c => c.MappingType?.ToLower() == "lookup");
            var hasAggregations = spec.Schema != null && spec.Schema.Any(c => c.MappingType?.ToLower() == "aggregation");

            if (hasLookups)
            {
                sb.Indent(1).AppendLine("-- [USER TODO]: Uncomment and complete reference lookup joins (L alias)");
                sb.Indent(1).AppendLine("-- LEFT JOIN target_db.dbo.LookupTable AS L ON #staging.SourceKey = L.SourceKey");
            }
            if (hasAggregations)
            {
                var nonAggCols = spec.Schema!
                    .Where(c => c.MappingType?.ToLower() != "aggregation")
                    .Select(c => c.ColumnName)
                    .ToList();
                var groupByList = string.Join(", ", nonAggCols);
                sb.Indent(1).AppendLine("-- [USER TODO]: Group by non-aggregated columns for calculations");
                sb.Indent(1).AppendLine($"-- GROUP BY {groupByList}");
            }

            var hasExpectRules = spec.Schema != null && spec.Schema.Any(c =>
                !c.Nullable ||
                c.IsKey == true ||
                !string.IsNullOrWhiteSpace(c.ValidationRegex) ||
                (c.AllowedValues != null && c.AllowedValues.Count > 0) ||
                (c.ExpectRules != null && c.ExpectRules.Count > 0) ||
                (c.FailRules != null && c.FailRules.Count > 0));

            if (hasExpectRules)
            {
                if (rejectPolicy == "quarantine")
                {
                    sb.Indent(1).AppendLine("ON FAILURE QUARANTINE TO #rejected_data WITH (HANDLING = SCRIPT);");
                }
                else if (rejectPolicy == "warn")
                {
                    sb.Indent(1).AppendLine("ON FAILURE WARN;");
                }
                else
                {
                    sb.Indent(1).AppendLine("ON FAILURE THROW;");
                }
            }
            else
            {
                sb.Indent(1).Append(";").AppendLine();
            }
            sb.AppendLine();

            // 7. Output generation & lineage logging
            sb.Indent(1).AppendLine("-- 7. PIPELINE INHERITED DATA GOVERNANCE");
            sb.Indent(1).AppendLine("INSERT TAG FOR TABLE #cleaned_data (");
            sb.Indent(2).AppendLine($"pipeline_source = '{specFileName}',");
            sb.Indent(2).AppendLine($"owner = '{owner}',");
            sb.Indent(2).AppendLine($"classification = '{classification}'");
            sb.Indent(1).AppendLine(");");
            sb.AppendLine();

            sb.Indent(1).AppendLine("-- 8. OUTBOUND UPLOAD");
            sb.Indent(1).AppendLine($"SELECT * INTO outbound_dest FROM #cleaned_data;");
            sb.AppendLine();

            sb.Indent(1).AppendLine("PRINT 'Pipeline execution completed successfully.';");
            sb.AppendLine("END TRY");
            sb.AppendLine("BEGIN CATCH");
            sb.Indent(1).AppendLine("PRINT 'Pipeline execution aborted: ' + ERROR_MESSAGE();");
            sb.Indent(1).AppendLine("THROW;");
            sb.AppendLine("END CATCH");

            return sb.ToString();
        }


        private static void WriteSourceContractComments(StringBuilder sb, SpecPipeline spec)
        {
            if (spec.Source == null && spec.Schema?.Any(HasColumnSourceMetadata) != true) return;

            sb.AppendLine("-- Source layout captured from the vendor specification:");
            if (spec.Source != null)
            {
                AppendCommentIfPresent(sb, "source connector", spec.Source.ConnectorType);
                AppendCommentIfPresent(sb, "source format", spec.Source.Format);
                AppendCommentIfPresent(sb, "source path", spec.Source.Path);
                AppendCommentIfPresent(sb, "source sheet", spec.Source.SheetName);
                AppendCommentIfPresent(sb, "header rows", spec.Source.HeaderRows?.ToString());
                AppendCommentIfPresent(sb, "skip rows", spec.Source.SkipRows?.ToString());
                AppendCommentIfPresent(sb, "record terminator", spec.Source.RecordTerminator);
                AppendCommentIfPresent(sb, "null tokens", spec.Source.NullTokens == null ? null : string.Join(", ", spec.Source.NullTokens));
                AppendCommentIfPresent(sb, "duplicate policy", spec.Source.DuplicatePolicy);
                AppendCommentIfPresent(sb, "reject policy", spec.Source.RejectPolicy);
                AppendCommentIfPresent(sb, "primary keys", spec.Source.PrimaryKeys == null ? null : string.Join(", ", spec.Source.PrimaryKeys));
            }

            if (spec.Schema != null)
            {
                foreach (var column in spec.Schema.Where(HasColumnSourceMetadata))
                {
                    var details = new List<string>();
                    AddDetail(details, "source", column.SourceName);
                    AddDetail(details, "position", column.StartPosition?.ToString());
                    AddDetail(details, "width", column.Width?.ToString());
                    AddDetail(details, "date_format", column.DateFormat);
                    AddDetail(details, "null_tokens", column.NullTokens == null ? null : string.Join("|", column.NullTokens));
                    AddDetail(details, "allowed_values", column.AllowedValues == null ? null : string.Join("|", column.AllowedValues));
                    AddDetail(details, "key", column.IsKey.HasValue ? column.IsKey.Value.ToString().ToLowerInvariant() : null);
                    if (details.Count > 0)
                    {
                        sb.AppendLine($"--   column {column.ColumnName}: {string.Join("; ", details)}");
                    }
                }
            }
        }

        private static void WriteEvidenceComments(StringBuilder sb, SpecPipeline spec)
        {
            var hasEvidence = spec.Confidence.HasValue
                              || spec.SourceEvidence is { Count: > 0 }
                              || spec.Source?.Confidence.HasValue == true
                              || spec.Source?.SourceEvidence is { Count: > 0 }
                              || spec.Destination?.Confidence.HasValue == true
                              || spec.Destination?.SourceEvidence is { Count: > 0 }
                              || spec.Schema?.Any(c => c.Confidence.HasValue || c.SourceEvidence is { Count: > 0 }) == true;
            if (!hasEvidence) return;

            sb.AppendLine("-- AI extraction review notes:");
            AppendConfidence(sb, "pipeline", spec.Confidence);
            AppendEvidence(sb, "pipeline", spec.SourceEvidence);
            AppendConfidence(sb, "source", spec.Source?.Confidence);
            AppendEvidence(sb, "source", spec.Source?.SourceEvidence);
            AppendConfidence(sb, "destination", spec.Destination?.Confidence);
            AppendEvidence(sb, "destination", spec.Destination?.SourceEvidence);

            if (spec.Schema != null)
            {
                foreach (var column in spec.Schema.Where(c => c.Confidence.HasValue || c.SourceEvidence is { Count: > 0 }))
                {
                    AppendConfidence(sb, $"column {column.ColumnName}", column.Confidence);
                    AppendEvidence(sb, $"column {column.ColumnName}", column.SourceEvidence);
                }
            }
        }

        private static void AppendConfidence(StringBuilder sb, string label, double? confidence)
        {
            if (confidence.HasValue)
                sb.AppendLine($"--   {label} confidence: {confidence.Value:0.###}");
        }

        private static void AppendEvidence(StringBuilder sb, string label, List<SpecEvidence>? evidence)
        {
            if (evidence == null) return;

            foreach (var item in evidence)
            {
                var parts = new List<string>();
                AddDetail(parts, "doc", item.Document);
                AddDetail(parts, "page", item.Page?.ToString());
                AddDetail(parts, "section", item.Section);
                AddDetail(parts, "field", item.OriginalFieldName);
                AddDetail(parts, "text", item.Text);
                if (parts.Count > 0)
                    sb.AppendLine($"--   {label} evidence: {string.Join("; ", parts)}");
            }
        }

        private static void WriteSourceConnectionTemplate(StringBuilder sb, SpecSource? source)
        {
            if (source == null)
            {
                sb.AppendLine("CREATE CONNECTION src_db AS POSTGRES(HOST='...', DATABASE='...', USER='...', PASSWORD='...');");
                return;
            }

            var connectorType = source.ConnectorType ?? "FLATFILE";
            var format = source.Format ?? "CSV";
            var resolvedType = ResolveConnectorType(connectorType, format);

            if (resolvedType.Equals("FLATFILE", StringComparison.OrdinalIgnoreCase))
            {
                var path = string.IsNullOrWhiteSpace(source.Path) ? "C:/Inbound/vendor_feed.csv" : source.Path;
                sb.AppendLine("CREATE CONNECTION src_file AS FLATFILE(");
                sb.AppendLine($"    PATH = '{EscapeSqlString(path)}',");
                sb.AppendLine($"    FORMAT = '{EscapeSqlString(format)}',");
                if (!string.IsNullOrWhiteSpace(source.Delimiter))
                    sb.AppendLine($"    DELIMITER = '{EscapeSqlString(ToDelimiterLiteral(source.Delimiter))}',");
                if (source.HasHeader.HasValue)
                    sb.AppendLine($"    HAS_HEADER = {(source.HasHeader.Value ? "TRUE" : "FALSE")},");
                if (!string.IsNullOrWhiteSpace(source.Encoding))
                    sb.AppendLine($"    ENCODING = '{EscapeSqlString(source.Encoding)}',");
                sb.AppendLine("    -- Review header_rows, skip_rows, null_tokens, and fixed-width layout before running.");
                sb.AppendLine(");");
                return;
            }
            else if (resolvedType.Equals("EXCEL", StringComparison.OrdinalIgnoreCase))
            {
                var path = string.IsNullOrWhiteSpace(source.Path) ? "C:/Inbound/vendor_feed.xlsx" : source.Path;
                sb.AppendLine("CREATE CONNECTION src_file AS EXCEL(");
                sb.AppendLine($"    PATH = '{EscapeSqlString(path)}',");
                if (!string.IsNullOrWhiteSpace(source.SheetName))
                    sb.AppendLine($"    SHEET = '{EscapeSqlString(source.SheetName)}',");
                var hasHeader = source.HasHeader ?? true;
                sb.AppendLine($"    HEADER = {(hasHeader ? "ON" : "OFF")}");
                sb.AppendLine(");");
                return;
            }
            else if (resolvedType.Equals("JSON", StringComparison.OrdinalIgnoreCase)
                     || resolvedType.Equals("XML", StringComparison.OrdinalIgnoreCase)
                     || resolvedType.Equals("PARQUET", StringComparison.OrdinalIgnoreCase)
                     || resolvedType.Equals("AVRO", StringComparison.OrdinalIgnoreCase))
            {
                var ext = resolvedType.ToLowerInvariant();
                var path = string.IsNullOrWhiteSpace(source.Path) ? $"C:/Inbound/vendor_feed.{ext}" : source.Path;
                sb.AppendLine($"CREATE CONNECTION src_file AS {resolvedType}(");
                sb.AppendLine($"    PATH = '{EscapeSqlString(path)}'");
                sb.AppendLine(");");
                return;
            }
            else if (resolvedType.Equals("SQLITE", StringComparison.OrdinalIgnoreCase))
            {
                var path = string.IsNullOrWhiteSpace(source.Path) ? "C:/Inbound/vendor_feed.db" : source.Path;
                sb.AppendLine("CREATE CONNECTION src_db AS SQLITE(");
                sb.AppendLine($"    DATABASE = '{EscapeSqlString(path)}'");
                sb.AppendLine(");");
                return;
            }

            sb.AppendLine($"CREATE CONNECTION src_db AS {resolvedType}(SERVER='...', DATABASE='...', USER='...', PASSWORD='...');");
        }

        private static string GetSourceFromTemplate(SpecSource? source)
        {
            if (source == null) return "FROM src_db.public.raw_table;";
            var connectorType = source.ConnectorType ?? "FLATFILE";
            var format = source.Format ?? "CSV";
            var resolvedType = ResolveConnectorType(connectorType, format);

            bool isFile = resolvedType.Equals("FLATFILE", StringComparison.OrdinalIgnoreCase)
                          || resolvedType.Equals("EXCEL", StringComparison.OrdinalIgnoreCase)
                          || resolvedType.Equals("JSON", StringComparison.OrdinalIgnoreCase)
                          || resolvedType.Equals("XML", StringComparison.OrdinalIgnoreCase)
                          || resolvedType.Equals("PARQUET", StringComparison.OrdinalIgnoreCase)
                          || resolvedType.Equals("AVRO", StringComparison.OrdinalIgnoreCase);

            if (isFile)
                return "FROM src_file;";

            if (resolvedType.Equals("SQLITE", StringComparison.OrdinalIgnoreCase))
                return "FROM src_db.raw_table;";

            return "FROM src_db.public.raw_table;";
        }

        private static string GetExtractionNote(SpecColumn col)
        {
            var notes = new List<string>();
            AddDetail(notes, "pos", col.StartPosition?.ToString());
            AddDetail(notes, "width", col.Width?.ToString());
            AddDetail(notes, "date", col.DateFormat);
            if (notes.Count == 0) return "";
            return $" /* {string.Join(", ", notes)} */";
        }

        private static bool HasColumnSourceMetadata(SpecColumn col)
            => !string.IsNullOrWhiteSpace(col.SourceName)
               || col.StartPosition.HasValue
               || col.Width.HasValue
               || !string.IsNullOrWhiteSpace(col.DateFormat)
               || col.NullTokens is { Count: > 0 }
               || col.AllowedValues is { Count: > 0 }
               || col.IsKey.HasValue;

        private static void AppendCommentIfPresent(StringBuilder sb, string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                sb.AppendLine($"--   {label}: {value}");
        }

        private static void AddDetail(List<string> details, string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                details.Add($"{label}={value}");
        }

        private static string ToDelimiterLiteral(string delimiter)
            => delimiter.ToLowerInvariant() switch
            {
                "comma" => ",",
                "tab" => "\\t",
                "pipe" => "|",
                "none" => "",
                _ => delimiter
            };

        private static string EscapeSqlString(string value) => value.Replace("'", "''");

        // Verifies that a candidate output path, once normalized, stays under the given directory.
        // Throws if it would escape (e.g. via traversal or an absolute path), so the caller's
        // try/catch turns it into a clean non-zero exit instead of an out-of-tree write.
        private static void EnsureWithinDirectory(string directory, string candidatePath)
        {
            var root = Path.GetFullPath(directory);
            var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
                ? root
                : root + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(candidatePath);

            if (!full.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Generated module path '{candidatePath}' resolves outside the modules directory '{directory}'.");
            }
        }

        private static string GetSqlTypeString(SpecColumn col)
        {
            var type = col.TypeFamily?.ToUpper() ?? "VARCHAR";
            if (type == "VARCHAR" && col.MaxLength.HasValue)
            {
                return $"VARCHAR({col.MaxLength.Value})";
            }
            if (type == "DECIMAL" && col.Precision.HasValue && col.Scale.HasValue)
            {
                return $"DECIMAL({col.Precision.Value},{col.Scale.Value})";
            }
            return type;
        }

        private static string GetCastingExpression(SpecColumn col)
        {
            var mappingType = col.MappingType?.ToLower() ?? "flat";
            var mappingRule = col.MappingRule ?? "";

            if (mappingType == "lookup")
            {
                return $"L.{col.ColumnName} /* [LOOKUP]: {mappingRule} */";
            }
            if (mappingType == "aggregation")
            {
                return $"SUM({col.ColumnName}) /* [AGGREGATION]: {mappingRule} */";
            }
            if (mappingType == "constant")
            {
                var isStr = col.TypeFamily?.ToUpper() == "VARCHAR";
                return isStr ? $"'{mappingRule.Replace("'", "''")}'" : mappingRule;
            }

            var type = col.TypeFamily?.ToUpper() ?? "VARCHAR";
            var colName = col.ColumnName;

            if (type == "VARCHAR")
            {
                if (col.MaxLength.HasValue)
                {
                    return col.Nullable
                        ? $"SUBSTRING(TRY_CAST({colName} AS VARCHAR), 1, {col.MaxLength.Value})"
                        : $"SUBSTRING(ISNULL(TRY_CAST({colName} AS VARCHAR), ''), 1, {col.MaxLength.Value})";
                }
                return col.Nullable
                    ? $"TRY_CAST({colName} AS VARCHAR)"
                    : $"ISNULL(TRY_CAST({colName} AS VARCHAR), '')";
            }

            if (type == "BIT")
            {
                return col.Nullable
                    ? $"TRY_CAST({colName} AS BIT)"
                    : $"ISNULL(TRY_CAST({colName} AS BIT), 0)";
            }

            // Numeric or date
            return $"TRY_CAST({colName} AS {GetSqlTypeString(col)})";
        }

        /// <summary>
        /// Builds the column's <c>EXPECT &lt;rule&gt; [ON FAILURE &lt;action&gt;]</c> clause from the
        /// spec. Rules are grammar, so a generated pipeline declares them in the statement — a
        /// generator that emitted them as comment tags would produce scripts whose quality rules
        /// silently do nothing, which is the failure the clause form exists to prevent.
        /// </summary>
        private static string GetExpectClause(SpecColumn col, string? rejectPolicy = null)
        {
            var rules = new List<string>();
            if (col.ExpectRules != null && col.ExpectRules.Count > 0)
            {
                // Spec-supplied rules are written by hand, so a MATCHES pattern arrives bare;
                // quote it here or the generated script will not parse.
                rules.AddRange(col.ExpectRules.Select(QuoteMatchesPattern));
            }
            else
            {
                if (!col.Nullable)
                {
                    rules.Add("NOT NULL");
                }
                if (col.IsKey == true)
                {
                    if (!rules.Contains("NOT NULL")) rules.Add("NOT NULL");
                    if (!rules.Contains("UNIQUE")) rules.Add("UNIQUE");
                }
                if (!string.IsNullOrWhiteSpace(col.ValidationRegex))
                {
                    // A regex is a string literal in the clause form, so it needs SQL quoting once
                    // rather than the doubled quoting the tag layer used to require.
                    rules.Add($"MATCHES '{col.ValidationRegex.Trim().Replace("'", "''")}'");
                }
                if (col.AllowedValues != null && col.AllowedValues.Count > 0)
                {
                    var vals = string.Join(", ", col.AllowedValues.Select(v => $"'{v.Replace("'", "''")}'"));
                    rules.Add($"IN ({vals})");
                }
            }

            if (rules.Count == 0) return "";

            // Rules combine with AND: inside a select list a comma separates columns, so it cannot
            // also separate rules.
            var action = ResolveFailAction(col, rejectPolicy);
            var clause = $" EXPECT {string.Join(" AND ", rules)}";
            return action == null ? clause : $"{clause} ON FAILURE {action}";
        }

        /// <summary>
        /// The column's failure action, from the spec's explicit action or the source's reject
        /// policy. Only the three real actions are accepted: a spec carrying something else would
        /// otherwise generate a script that does not parse, turning a bad spec into a broken
        /// pipeline instead of a pipeline with a defaulted action.
        /// </summary>
        private static string? ResolveFailAction(SpecColumn col, string? rejectPolicy)
        {
            var declared = col.FailRules is { Count: > 0 } ? col.FailRules[0] : col.FailAction;
            var action = declared?.Trim().ToUpperInvariant();
            if (action is "THROW" or "WARN" or "QUARANTINE") return action;

            if (string.IsNullOrWhiteSpace(rejectPolicy)) return null;
            return rejectPolicy.Trim().ToLowerInvariant() switch
            {
                "quarantine" => "QUARANTINE",
                "fail_batch" or "throw" or "reject" => "THROW",
                "warn" => "WARN",
                _ => null
            };
        }

        /// <summary>
        /// Wraps a bare <c>MATCHES</c> pattern in a string literal. A regex cannot be lexed as
        /// tokens — '@', quotes, and operators would all tokenize — so the clause grammar requires
        /// the literal form.
        /// </summary>
        private static string QuoteMatchesPattern(string rule)
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                rule.Trim(), @"^(?<lead>(NOT\s+)?MATCHES)\s+(?<pattern>.+)$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!match.Success) return rule.Trim();

            var pattern = match.Groups["pattern"].Value.Trim();
            if (pattern.StartsWith('\'') && pattern.EndsWith('\'')) return rule.Trim();
            return $"{match.Groups["lead"].Value} '{pattern.Replace("'", "''")}'";
        }

        /// <summary>
        /// Descriptive tags only — a description and any stewardship tags the spec carries. These
        /// are the things a comment is for: strip them and the pipeline still loads the same rows.
        /// </summary>
        private static string GetInlineComment(SpecColumn col)
        {
            var tagsList = new List<string>();

            if (!string.IsNullOrEmpty(col.Description))
            {
                tagsList.Add($"@d: '{col.Description.Replace("'", "''")}'");
            }

            if (col.Tags != null && col.Tags.Count > 0)
            {
                foreach (var tag in col.Tags)
                {
                    if (tag.Equals("none", StringComparison.OrdinalIgnoreCase)) continue;
                    var cleanTag = tag.TrimStart('@').ToLowerInvariant();
                    tagsList.Add($"@{cleanTag}");
                }
            }

            if (tagsList.Count == 0) return "";
            return $"/* {string.Join("; ", tagsList)} */";
        }

        private static string ResolveConnectorType(string connectorType, string format)
        {
            if (connectorType.Equals("FLATFILE", StringComparison.OrdinalIgnoreCase))
            {
                var fmtUpper = format.ToUpperInvariant();
                if (fmtUpper == "EXCEL" || fmtUpper == "XLSX" || fmtUpper == "XLS" || fmtUpper == "XLSM")
                    return "EXCEL";
                if (fmtUpper == "JSON")
                    return "JSON";
                if (fmtUpper == "XML")
                    return "XML";
                if (fmtUpper == "PARQUET")
                    return "PARQUET";
                if (fmtUpper == "AVRO")
                    return "AVRO";
            }
            return connectorType;
        }
    }

    internal static class SpecPipelineValidator
    {
        // Documented safe identifier for dataset names: must start with a letter, then letters,
        // digits, underscore, or hyphen, up to 64 chars. This is the only character set that may
        // appear in a generated module filename, RUN SCRIPT path, or PRINT literal, so it forbids
        // path separators ('/' '\'), traversal ('.'), quotes, and newlines by construction.
        internal static readonly Regex DatasetNameRegex =
            new(@"^[A-Za-z][A-Za-z0-9_-]{0,63}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // Windows reserved device names cannot be used as bare filenames even with a safe charset.
        private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        private static readonly HashSet<string> ConnectorTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "FLATFILE", "MSSQL", "POSTGRES", "MYSQL", "ORACLE", "SNOWFLAKE", "BIGQUERY", "SQLITE"
        };

        private static readonly HashSet<string> Formats = new(StringComparer.OrdinalIgnoreCase)
        {
            "CSV", "TSV", "PIPE", "EXCEL", "JSON", "XML", "PARQUET", "AVRO", "DB_TABLE"
        };

        private static readonly HashSet<string> Delimiters = new(StringComparer.OrdinalIgnoreCase)
        {
            "comma", "tab", "pipe", "none"
        };

        private static readonly HashSet<string> Encodings = new(StringComparer.OrdinalIgnoreCase)
        {
            "UTF8", "ANSI", "ASCII", "UNICODE"
        };

        private static readonly HashSet<string> TextQualifiers = new(StringComparer.OrdinalIgnoreCase)
        {
            "doublequote", "singlequote", "none"
        };

        private static readonly HashSet<string> TypeFamilies = new(StringComparer.OrdinalIgnoreCase)
        {
            "INT", "DECIMAL", "VARCHAR", "DATE", "DATETIME", "BIT"
        };

        private static readonly HashSet<string> MappingTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "lookup", "aggregation", "constant", "flat"
        };

        private static readonly HashSet<string> DuplicatePolicies = new(StringComparer.OrdinalIgnoreCase)
        {
            "allow", "first_wins", "last_wins", "reject"
        };

        private static readonly HashSet<string> RejectPolicies = new(StringComparer.OrdinalIgnoreCase)
        {
            "fail_batch", "quarantine", "warn"
        };

        public static List<string> Validate(SpecPipeline spec)
        {
            var errors = new List<string>();

            RequireText(spec.PipelineName, "pipeline_name", errors);
            ValidateReviewMetadata("root", spec.Confidence, spec.SourceEvidence, errors);
            if (spec.Metadata == null)
            {
                errors.Add("metadata is required.");
            }
            else
            {
                RequireText(spec.Metadata.Description, "metadata.description", errors);
                RequireEnum(spec.Metadata.Classification, "metadata.classification", errors, new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "public", "internal", "confidential", "restricted"
                });
                RequireText(spec.Metadata.Owner, "metadata.owner", errors);
            }

            var hasDatasets = spec.Datasets is { Count: > 0 };
            var hasRootDataset = spec.Source != null || spec.Destination != null || spec.Schema is { Count: > 0 };

            if (hasDatasets && hasRootDataset)
            {
                errors.Add("Use either root-level destination/schema or datasets[], not both.");
            }
            else if (hasDatasets)
            {
                ValidateDatasets(spec.Datasets!, errors);
            }
            else
            {
                ValidateDatasetBody("root", spec.Source, spec.Destination, spec.Schema, errors);
            }

            return errors;
        }

        private static void ValidateDatasets(List<SpecDataset> datasets, List<string> errors)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < datasets.Count; i++)
            {
                var dataset = datasets[i];
                var path = $"datasets[{i}]";
                RequireText(dataset.Name, $"{path}.name", errors);
                ValidateReviewMetadata(path, dataset.Confidence, dataset.SourceEvidence, errors);
                if (!string.IsNullOrWhiteSpace(dataset.Name))
                {
                    ValidateDatasetName(dataset.Name, $"{path}.name", errors);
                    // Duplicate check is case-insensitive, so two names that collide to the same
                    // generated module filename (e.g. "Sales" / "sales") are rejected here.
                    if (!names.Add(dataset.Name))
                    {
                        errors.Add($"{path}.name duplicates another dataset name (names are compared case-insensitively).");
                    }
                }

                ValidateDatasetBody(path, dataset.Source, dataset.Destination, dataset.Schema, errors);
            }
        }

        private static void ValidateDatasetName(string name, string path, List<string> errors)
        {
            if (!DatasetNameRegex.IsMatch(name))
            {
                errors.Add($"{path} '{name}' is not a valid dataset identifier. Use a letter followed by letters, digits, '_' or '-' (max 64 chars); path separators, quotes, and whitespace are not allowed.");
                return;
            }

            if (ReservedDeviceNames.Contains(name))
            {
                errors.Add($"{path} '{name}' is a reserved device name and cannot be used as a module name.");
            }
        }

        private static void ValidateDatasetBody(string path, SpecSource? source, SpecDestination? destination, List<SpecColumn>? schema, List<string> errors)
        {
            if (source != null)
            {
                ValidateSource($"{path}.source", source, errors);
            }

            if (destination == null)
            {
                errors.Add($"{path}.destination is required.");
            }
            else
            {
                ValidateDestination($"{path}.destination", destination, errors);
            }

            if (schema == null || schema.Count == 0)
            {
                errors.Add($"{path}.schema must contain at least one column.");
            }
            else
            {
                ValidateColumns($"{path}.schema", schema, errors);
            }
        }

        private static void ValidateSource(string path, SpecSource source, List<string> errors)
        {
            ValidateReviewMetadata(path, source.Confidence, source.SourceEvidence, errors);

            if (!string.IsNullOrWhiteSpace(source.ConnectorType))
                RequireEnum(source.ConnectorType, $"{path}.connector_type", errors, ConnectorTypes);

            if (!string.IsNullOrWhiteSpace(source.Format))
                RequireEnum(source.Format, $"{path}.format", errors, Formats);

            if (!string.IsNullOrWhiteSpace(source.Delimiter))
                RequireEnum(source.Delimiter, $"{path}.delimiter", errors, Delimiters);

            if (!string.IsNullOrWhiteSpace(source.TextQualifier))
                RequireEnum(source.TextQualifier, $"{path}.text_qualifier", errors, TextQualifiers);

            if (!string.IsNullOrWhiteSpace(source.Encoding))
                RequireEnum(source.Encoding, $"{path}.encoding", errors, Encodings);

            if (source.HeaderRows.HasValue && source.HeaderRows < 0)
                errors.Add($"{path}.header_rows must be zero or greater.");

            if (source.SkipRows.HasValue && source.SkipRows < 0)
                errors.Add($"{path}.skip_rows must be zero or greater.");

            if (!string.IsNullOrWhiteSpace(source.DuplicatePolicy))
                RequireEnum(source.DuplicatePolicy, $"{path}.duplicate_policy", errors, DuplicatePolicies);

            if (!string.IsNullOrWhiteSpace(source.RejectPolicy))
                RequireEnum(source.RejectPolicy, $"{path}.reject_policy", errors, RejectPolicies);

            if (source.PrimaryKeys is { Count: > 0 } && source.PrimaryKeys.Any(string.IsNullOrWhiteSpace))
                errors.Add($"{path}.primary_keys cannot contain blank values.");
        }

        private static void ValidateDestination(string path, SpecDestination destination, List<string> errors)
        {
            ValidateReviewMetadata(path, destination.Confidence, destination.SourceEvidence, errors);

            RequireEnum(destination.ConnectorType, $"{path}.connector_type", errors, ConnectorTypes);
            RequireEnum(destination.Format, $"{path}.format", errors, Formats);
            RequireText(destination.Path, $"{path}.path", errors);

            if (!string.IsNullOrWhiteSpace(destination.Delimiter))
                RequireEnum(destination.Delimiter, $"{path}.delimiter", errors, Delimiters);

            if (!string.IsNullOrWhiteSpace(destination.TextQualifier))
                RequireEnum(destination.TextQualifier, $"{path}.text_qualifier", errors, TextQualifiers);

            if (!string.IsNullOrWhiteSpace(destination.Encoding))
                RequireEnum(destination.Encoding, $"{path}.encoding", errors, Encodings);
        }

        private static void ValidateColumns(string path, List<SpecColumn> columns, List<string> errors)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < columns.Count; i++)
            {
                var column = columns[i];
                var columnPath = $"{path}[{i}]";
                ValidateReviewMetadata(columnPath, column.Confidence, column.SourceEvidence, errors);
                RequireText(column.ColumnName, $"{columnPath}.column_name", errors);
                if (!string.IsNullOrWhiteSpace(column.ColumnName) && !names.Add(column.ColumnName))
                {
                    errors.Add($"{columnPath}.column_name duplicates another column in the same schema.");
                }

                RequireEnum(column.TypeFamily, $"{columnPath}.type_family", errors, TypeFamilies);

                if (column.MaxLength.HasValue && column.MaxLength <= 0)
                    errors.Add($"{columnPath}.max_length must be greater than zero.");

                if (column.Precision.HasValue && column.Precision <= 0)
                    errors.Add($"{columnPath}.precision must be greater than zero.");

                if (column.Scale.HasValue && column.Scale < 0)
                    errors.Add($"{columnPath}.scale must be zero or greater.");

                if (column.Precision.HasValue && column.Scale.HasValue && column.Scale > column.Precision)
                    errors.Add($"{columnPath}.scale cannot be greater than precision.");

                if (!string.IsNullOrWhiteSpace(column.ValidationRegex))
                {
                    try
                    {
                        _ = new Regex(column.ValidationRegex);
                    }
                    catch (ArgumentException ex)
                    {
                        errors.Add($"{columnPath}.validation_regex is not a valid regular expression: {ex.Message}");
                    }
                }

                if (!string.IsNullOrWhiteSpace(column.MappingType))
                    RequireEnum(column.MappingType, $"{columnPath}.mapping_type", errors, MappingTypes);

                if (column.StartPosition.HasValue && column.StartPosition <= 0)
                    errors.Add($"{columnPath}.start_position must be greater than zero.");

                if (column.Width.HasValue && column.Width <= 0)
                    errors.Add($"{columnPath}.width must be greater than zero.");

                if (column.AllowedValues is { Count: > 0 } && column.AllowedValues.Any(string.IsNullOrWhiteSpace))
                    errors.Add($"{columnPath}.allowed_values cannot contain blank values.");

                if (column.NullTokens is { Count: > 0 } && column.NullTokens.Any(t => t == null))
                    errors.Add($"{columnPath}.null_tokens cannot contain null values.");
            }
        }

        private static void ValidateReviewMetadata(string path, double? confidence, List<SpecEvidence>? evidence, List<string> errors)
        {
            if (confidence.HasValue && (confidence < 0 || confidence > 1))
                errors.Add($"{path}.confidence must be between 0 and 1.");

            if (evidence == null) return;

            for (var i = 0; i < evidence.Count; i++)
            {
                var item = evidence[i];
                var evidencePath = $"{path}.source_evidence[{i}]";
                if (item.Page.HasValue && item.Page <= 0)
                    errors.Add($"{evidencePath}.page must be greater than zero.");

                if (string.IsNullOrWhiteSpace(item.Text)
                    && string.IsNullOrWhiteSpace(item.Section)
                    && string.IsNullOrWhiteSpace(item.OriginalFieldName))
                {
                    errors.Add($"{evidencePath} must include text, section, or original_field_name.");
                }
            }
        }

        private static void RequireText(string? value, string path, List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(value))
                errors.Add($"{path} is required.");
        }

        private static void RequireEnum(string? value, string path, List<string> errors, HashSet<string> allowed)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                errors.Add($"{path} is required.");
                return;
            }

            if (!allowed.Contains(value))
                errors.Add($"{path} has unsupported value '{value}'. Allowed values: {string.Join(", ", allowed.OrderBy(v => v))}.");
        }
    }

    public static class StringBuilderExtensions
    {
        public static StringBuilder Indent(this StringBuilder sb, int count)
        {
            sb.Append(new string(' ', count * 4));
            return sb;
        }
    }

    // JSON Model Classes
    public class SpecPipeline
    {
        [JsonPropertyName("pipeline_name")]
        public string? PipelineName { get; set; }

        [JsonPropertyName("metadata")]
        public SpecMetadata? Metadata { get; set; }

        [JsonPropertyName("confidence")]
        public double? Confidence { get; set; }

        [JsonPropertyName("source_evidence")]
        public List<SpecEvidence>? SourceEvidence { get; set; }

        [JsonPropertyName("source")]
        public SpecSource? Source { get; set; }

        [JsonPropertyName("destination")]
        public SpecDestination? Destination { get; set; }

        [JsonPropertyName("schema")]
        public List<SpecColumn>? Schema { get; set; }

        [JsonPropertyName("datasets")]
        public List<SpecDataset>? Datasets { get; set; }
    }

    public class SpecDataset
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("confidence")]
        public double? Confidence { get; set; }

        [JsonPropertyName("source_evidence")]
        public List<SpecEvidence>? SourceEvidence { get; set; }

        [JsonPropertyName("source")]
        public SpecSource? Source { get; set; }

        [JsonPropertyName("destination")]
        public SpecDestination? Destination { get; set; }

        [JsonPropertyName("schema")]
        public List<SpecColumn>? Schema { get; set; }
    }

    public class SpecMetadata
    {
        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("classification")]
        public string? Classification { get; set; }

        [JsonPropertyName("owner")]
        public string? Owner { get; set; }
    }

    public class SpecEvidence
    {
        [JsonPropertyName("document")]
        public string? Document { get; set; }

        [JsonPropertyName("page")]
        public int? Page { get; set; }

        [JsonPropertyName("section")]
        public string? Section { get; set; }

        [JsonPropertyName("original_field_name")]
        public string? OriginalFieldName { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }

    public class SpecSource
    {
        [JsonPropertyName("confidence")]
        public double? Confidence { get; set; }

        [JsonPropertyName("source_evidence")]
        public List<SpecEvidence>? SourceEvidence { get; set; }

        [JsonPropertyName("connector_type")]
        public string? ConnectorType { get; set; }

        [JsonPropertyName("format")]
        public string? Format { get; set; }

        [JsonPropertyName("path")]
        public string? Path { get; set; }

        [JsonPropertyName("delimiter")]
        public string? Delimiter { get; set; }

        [JsonPropertyName("text_qualifier")]
        public string? TextQualifier { get; set; }

        [JsonPropertyName("encoding")]
        public string? Encoding { get; set; }

        [JsonPropertyName("has_header")]
        public bool? HasHeader { get; set; }

        [JsonPropertyName("header_rows")]
        public int? HeaderRows { get; set; }

        [JsonPropertyName("skip_rows")]
        public int? SkipRows { get; set; }

        [JsonPropertyName("sheet_name")]
        public string? SheetName { get; set; }

        [JsonPropertyName("record_terminator")]
        public string? RecordTerminator { get; set; }

        [JsonPropertyName("null_tokens")]
        public List<string>? NullTokens { get; set; }

        [JsonPropertyName("primary_keys")]
        public List<string>? PrimaryKeys { get; set; }

        [JsonPropertyName("duplicate_policy")]
        public string? DuplicatePolicy { get; set; }

        [JsonPropertyName("reject_policy")]
        public string? RejectPolicy { get; set; }
    }

    public class SpecDestination
    {
        [JsonPropertyName("confidence")]
        public double? Confidence { get; set; }

        [JsonPropertyName("source_evidence")]
        public List<SpecEvidence>? SourceEvidence { get; set; }

        [JsonPropertyName("connector_type")]
        public string? ConnectorType { get; set; }

        [JsonPropertyName("format")]
        public string? Format { get; set; }

        [JsonPropertyName("delimiter")]
        public string? Delimiter { get; set; }

        [JsonPropertyName("text_qualifier")]
        public string? TextQualifier { get; set; }

        [JsonPropertyName("encoding")]
        public string? Encoding { get; set; }

        [JsonPropertyName("naming_pattern")]
        public string? NamingPattern { get; set; }

        [JsonPropertyName("path")]
        public string? Path { get; set; }

        [JsonPropertyName("has_header")]
        public bool? HasHeader { get; set; }

        [JsonPropertyName("sheet_name")]
        public string? SheetName { get; set; }
    }

    public class SpecColumn
    {
        [JsonPropertyName("column_name")]
        public string? ColumnName { get; set; }

        [JsonPropertyName("confidence")]
        public double? Confidence { get; set; }

        [JsonPropertyName("source_evidence")]
        public List<SpecEvidence>? SourceEvidence { get; set; }

        [JsonPropertyName("source_name")]
        public string? SourceName { get; set; }

        [JsonPropertyName("start_position")]
        public int? StartPosition { get; set; }

        [JsonPropertyName("width")]
        public int? Width { get; set; }

        [JsonPropertyName("type_family")]
        public string? TypeFamily { get; set; }

        [JsonPropertyName("max_length")]
        public int? MaxLength { get; set; }

        [JsonPropertyName("precision")]
        public int? Precision { get; set; }

        [JsonPropertyName("scale")]
        public int? Scale { get; set; }

        [JsonPropertyName("nullable")]
        public bool Nullable { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("validation_regex")]
        public string? ValidationRegex { get; set; }

        [JsonPropertyName("date_format")]
        public string? DateFormat { get; set; }

        [JsonPropertyName("null_tokens")]
        public List<string>? NullTokens { get; set; }

        [JsonPropertyName("allowed_values")]
        public List<string>? AllowedValues { get; set; }

        [JsonPropertyName("is_key")]
        public bool? IsKey { get; set; }

        [JsonPropertyName("tags")]
        public List<string>? Tags { get; set; }

        [JsonPropertyName("expect_rules")]
        public List<string>? ExpectRules { get; set; }

        [JsonPropertyName("fail_rules")]
        public List<string>? FailRules { get; set; }

        [JsonPropertyName("fail_action")]
        public string? FailAction { get; set; }

        [JsonPropertyName("mapping_type")]
        public string? MappingType { get; set; }

        [JsonPropertyName("mapping_rule")]
        public string? MappingRule { get; set; }
    }
}
