using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles EXPECT SCHEMA statements.
    /// Compares the declared column manifest against the actual schema of a #temp table or
    /// named connection, raising an ExecutionException (or logging a warning) on drift.
    /// </summary>
    public class ExpectSchemaStatementHandler : IStatementHandler
    {
        private readonly ILogger _logger;
        public Type SupportedStatementType => typeof(ExpectSchemaStatement);

        public ExpectSchemaStatementHandler(ILogger logger) => _logger = logger;

        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (ExpectSchemaStatement)statement;

            _logger.Debug($"EXPECT SCHEMA checking '{stmt.Target}'");

            // Resolve the actual schema from the data source
            Dictionary<string, string> actual = await GetActualSchemaAsync(stmt.Target, context);

            // Build a diff
            var issues = new List<string>();

            foreach (var expected in stmt.Columns)
            {
                if (!actual.TryGetValue(expected.ColumnName, out var actualType))
                {
                    issues.Add($"  MISSING   : {expected.ColumnName} (expected {expected.DataType})");
                }
                else if (actualType != "UNKNOWN" &&
                         !SameTypeFamily(expected.DataType, actualType))
                {
                    issues.Add($"  TYPE DRIFT: {expected.ColumnName} — expected {NormalizeBase(expected.DataType)}, found {NormalizeBase(actualType)}");
                }
            }

            if (issues.Count == 0)
            {
                _logger.Debug($"EXPECT SCHEMA '{stmt.Target}' passed — no drift detected.");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Schema drift on '{stmt.Target}':");
            foreach (var issue in issues) sb.AppendLine(issue);
            var message = sb.ToString().TrimEnd();

            if (stmt.WarnOnDrift)
            {
                _logger.Warning(message);
            }
            else
            {
                _logger.Error(message);
                throw new ExecutionException(message);
            }
        }

        // ── Schema retrieval ────────────────────────────────────────────────────

        private static async Task<Dictionary<string, string>> GetActualSchemaAsync(
            string target, IExecutionContext context)
        {
            if (!context.Connections.TryGetValue(target, out var ds))
                throw new ExecutionException($"EXPECT SCHEMA: target '{target}' not found. Declare it with CREATE TABLE or CREATE CONNECTION before using EXPECT SCHEMA.");

            // InMemoryDataSource (#temp tables and in-memory connections) carries full type info
            if (ds is InMemoryDataSource mem)
            {
                return mem.Schema.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.DataType ?? "VARCHAR",
                    StringComparer.OrdinalIgnoreCase);
            }

            // For all other connectors: read the first batch to discover column names.
            // Type info is not reliably available across all connectors, so types are marked UNKNOWN
            // and only column presence is checked.
            var schema = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            await foreach (var batch in ds.ReadBatches(1))
            {
                foreach (var col in batch.ColumnNames)
                    schema[col] = "UNKNOWN";
                break;
            }
            return schema;
        }

        // ── Type family comparison ───────────────────────────────────────────────

        private static bool SameTypeFamily(string expected, string actual) =>
            GetFamily(expected) == GetFamily(actual);

        private enum TypeFamily { Integer, Decimal, String, Date, Boolean, Binary, Unknown }

        private static TypeFamily GetFamily(string rawType)
        {
            var t = NormalizeBase(rawType).ToUpperInvariant();
            return t switch
            {
                "INT" or "INTEGER" or "BIGINT" or "SMALLINT" or "TINYINT"
                    => TypeFamily.Integer,

                "DECIMAL" or "NUMERIC" or "MONEY" or "SMALLMONEY"
                or "FLOAT" or "REAL" or "DOUBLE"
                    => TypeFamily.Decimal,

                "VARCHAR" or "NVARCHAR" or "CHAR" or "NCHAR" or "TEXT" or "NTEXT"
                or "VARCHAR2" or "CLOB" or "STRING"
                    => TypeFamily.String,

                "DATE" or "DATETIME" or "DATETIME2" or "SMALLDATETIME"
                or "TIMESTAMP" or "DATETIMEOFFSET"
                    => TypeFamily.Date,

                "BIT" or "BOOLEAN" or "BOOL"
                    => TypeFamily.Boolean,

                "VARBINARY" or "BINARY" or "BLOB" or "IMAGE"
                    => TypeFamily.Binary,

                _ => TypeFamily.Unknown
            };
        }

        // Strip length/precision: "VARCHAR(50)" → "VARCHAR", "DECIMAL(18,2)" → "DECIMAL"
        private static string NormalizeBase(string dataType) =>
            dataType.Contains('(') ? dataType[..dataType.IndexOf('(')] : dataType;
    }
}
