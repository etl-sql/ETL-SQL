using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using System.Linq;
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
                    AllowTrailingCommas = true
                };

                var spec = JsonSerializer.Deserialize<SpecPipeline>(jsonContent, options);
                if (spec == null)
                {
                    logger.WriteLine("Failed to deserialize the JSON schema specification.", ConsoleColor.Red);
                    return 1;
                }

                logger.WriteLine($"Compiling ETL-SQL script for pipeline: '{spec.PipelineName}'");
                var etlSqlCode = CompilePipeline(spec, Path.GetFileName(schemaPath));

                var outputDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                await File.WriteAllTextAsync(outputPath, etlSqlCode, Encoding.UTF8);
                logger.WriteLine($"ETL-SQL script successfully generated: {outputPath}", ConsoleColor.Green);
                return 0;
            }
            catch (Exception ex)
            {
                logger.WriteLine($"Error compiling pipeline: {ex.Message}", ConsoleColor.Red);
                logger.WriteLine(ex.ToString(), ConsoleColor.DarkGray);
                return 1;
            }
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
            var namingPattern = spec.Destination?.NamingPattern ?? $"{pipelineName}_output.csv";
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
            var connType = spec.Destination?.ConnectorType ?? "FLATFILE";
            var format = spec.Destination?.Format ?? "CSV";
            var path = spec.Destination?.Path ?? "outbound_dir";

            if (connType.Equals("FLATFILE", StringComparison.OrdinalIgnoreCase))
            {
                var fileRef = hasDatePattern ? $"'{path}' + '/' + @FileName" : $"'{path}/{namingPattern}'";
                if (!hasDatePattern)
                {
                    sb.AppendLine($"-- Base directory connection context");
                    sb.AppendLine($"CREATE CONNECTION dest_dir AS DIRECTORY('{path}');");
                    fileRef = $"dest_dir + '/{namingPattern}'";
                }
                
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
            else
            {
                // Database connection template
                sb.AppendLine($"-- CREATE CONNECTION outbound_dest AS {connType}(");
                sb.AppendLine($"--     SERVER = '...',");
                sb.AppendLine($"--     DATABASE = '...',");
                sb.AppendLine($"--     TABLE = '{path}'");
                sb.AppendLine($"-- );");
            }
            sb.AppendLine();

            // 4. Extract Block (User Contract)
            sb.AppendLine("-- =========================================================================");
            sb.AppendLine("-- 4. EXTRACT PHASE (USER CONTRACT)");
            sb.AppendLine("-- =========================================================================");
            sb.AppendLine("-- [USER TODO]: Define your source connection and query into #staging below.");
            sb.AppendLine("-- All columns must match the names defined in the schema validation below.");
            sb.AppendLine("/*");
            sb.AppendLine("CREATE CONNECTION src_db AS POSTGRES(HOST='...', DATABASE='...', USER='...', PASSWORD='...');");
            sb.AppendLine("SELECT ");
            if (spec.Schema != null && spec.Schema.Count > 0)
            {
                for (int i = 0; i < spec.Schema.Count; i++)
                {
                    var col = spec.Schema[i];
                    var comma = (i == spec.Schema.Count - 1) ? "" : ",";
                    sb.AppendLine($"    raw_{col.ColumnName,-20} AS {col.ColumnName}{comma}");
                }
            }
            sb.AppendLine("INTO #staging");
            sb.AppendLine("FROM src_db.public.raw_table;");
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
            sb.Indent(1).AppendLine("-- 6. TRANSFORMATION, CASTING & GOVERNANCE TAGGING");
            sb.Indent(1).AppendLine("SELECT");
            if (spec.Schema != null)
            {
                for (int i = 0; i < spec.Schema.Count; i++)
                {
                    var col = spec.Schema[i];
                    var castExpr = GetCastingExpression(col);
                    var comment = GetInlineComment(col);
                    var comma = (i == spec.Schema.Count - 1) ? "" : ",";
                    sb.Indent(2).AppendLine($"{castExpr,-50} AS {col.ColumnName,-25} {comment}{comma}");
                }
            }
            sb.Indent(1).AppendLine("INTO #cleaned_data");
            sb.Indent(1).AppendLine("FROM #staging;");
            sb.AppendLine();

            // 7. Regex Formatting Checks
            var hasRegex = spec.Schema != null && spec.Schema.Any(c => !string.IsNullOrEmpty(c.ValidationRegex));
            if (hasRegex)
            {
                sb.Indent(1).AppendLine("-- 7. FORMAT VALIDATION GATES (REGULAR EXPRESSIONS)");
                foreach (var col in spec.Schema!.Where(c => !string.IsNullOrEmpty(c.ValidationRegex)))
                {
                    var escapedRegex = col.ValidationRegex!.Replace("'", "''");
                    sb.Indent(1).AppendLine($"IF EXISTS (SELECT 1 FROM #cleaned_data WHERE {col.ColumnName} IS NOT NULL AND REGEXP_LIKE({col.ColumnName}, '{escapedRegex}') = 0)");
                    sb.Indent(1).AppendLine("BEGIN");
                    sb.Indent(2).AppendLine($"THROW 50002, 'Specification format violation: column [{col.ColumnName}] contains values that fail validation pattern.', 16;");
                    sb.Indent(1).AppendLine("END");
                }
                sb.AppendLine();
            }

            // 8. Output generation & lineage logging
            sb.Indent(1).AppendLine("-- 8. PIPELINE INHERITED DATA GOVERNANCE");
            sb.Indent(1).AppendLine("TAG #cleaned_data WITH (");
            sb.Indent(2).AppendLine($"pipeline_source = '{specFileName}',");
            sb.Indent(2).AppendLine($"owner = '{owner}',");
            sb.Indent(2).AppendLine($"classification = '{classification}'");
            sb.Indent(1).AppendLine(");");
            sb.AppendLine();

            sb.Indent(1).AppendLine("-- 9. OUTBOUND UPLOAD");
            sb.Indent(1).AppendLine("SELECT * INTO outbound_dest FROM #cleaned_data;");
            sb.AppendLine();

            sb.Indent(1).AppendLine("PRINT 'Pipeline execution completed successfully.';");
            sb.AppendLine("END TRY");
            sb.AppendLine("BEGIN CATCH");
            sb.Indent(1).AppendLine("PRINT 'Pipeline execution aborted: ' + ERROR_MESSAGE();");
            sb.Indent(1).AppendLine("THROW;");
            sb.AppendLine("END CATCH");

            return sb.ToString();
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

        private static string GetInlineComment(SpecColumn col)
        {
            var tagsList = new List<string>();
            if (!string.IsNullOrEmpty(col.Description))
            {
                tagsList.Add($"@d: {col.Description}");
            }
            if (col.Tags != null && col.Tags.Count > 0)
            {
                foreach (var tag in col.Tags)
                {
                    if (tag.Equals("none", StringComparison.OrdinalIgnoreCase)) continue;
                    tagsList.Add($"@{tag.ToLower()}");
                }
            }

            if (tagsList.Count == 0) return "";
            return $"/*{string.Join("; ", tagsList)}*/";
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

    public class SpecDestination
    {
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
    }

    public class SpecColumn
    {
        [JsonPropertyName("column_name")]
        public string? ColumnName { get; set; }

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

        [JsonPropertyName("tags")]
        public List<string>? Tags { get; set; }
    }
}
