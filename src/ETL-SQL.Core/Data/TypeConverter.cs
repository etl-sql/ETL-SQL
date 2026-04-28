using System;
using System.Collections;
using ETL_SQL.Common;

namespace ETL_SQL.Core.Data
{
    /// <summary>
    /// Registry for type conversion logic.
    /// Replaces large switch statements for SQL type casting.
    /// </summary>
    public static class TypeConverter
    {
        private static readonly Dictionary<string, Func<object, object?>> _converters = new(StringComparer.OrdinalIgnoreCase)
        {
            ["INT"] = v => Convert.ToDecimal(v),
            ["INTEGER"] = v => Convert.ToDecimal(v),
            ["DECIMAL"] = v => Convert.ToDecimal(v),
            ["MONEY"] = v => Convert.ToDecimal(v),
            ["NUMERIC"] = v => Convert.ToDecimal(v),
            ["FLOAT"] = v => Convert.ToDouble(v),
            ["DOUBLE"] = v => Convert.ToDouble(v),
            ["BIT"] = v => Convert.ToBoolean(v),
            ["BOOLEAN"] = v => Convert.ToBoolean(v),
            ["BOOL"] = v => Convert.ToBoolean(v),
            ["TINYINT"] = v => Convert.ToDecimal(v),
            ["SMALLINT"] = v => Convert.ToDecimal(v),
            ["BIGINT"] = v => Convert.ToDecimal(v),
            ["REAL"] = v => Convert.ToSingle(v),
            ["DATETIME"] = v => EvaluationUtils.SafeTryParseDate(v.ToString() ?? "", out var dt) ? dt : DateTime.Parse(v.ToString() ?? ""),
            ["DATE"] = v => EvaluationUtils.SafeTryParseDate(v.ToString() ?? "", out var dt) ? dt : DateTime.Parse(v.ToString() ?? ""),
            ["TIMESTAMP"] = v => EvaluationUtils.SafeTryParseDate(v.ToString() ?? "", out var dt) ? dt : DateTime.Parse(v.ToString() ?? ""),

            ["TIME"] = v => TimeSpan.Parse(v.ToString() ?? "00:00:00"),
            ["STRING"] = v => v.ToString(),
            ["VARCHAR"] = v => v.ToString(),
            ["NVARCHAR"] = v => v.ToString(),
            ["TEXT"] = v => v.ToString(),
            ["NTEXT"] = v => v.ToString(),
            ["CHAR"] = v => v.ToString(),
            ["NCHAR"] = v => v.ToString(),
            ["JSON"] = v => {
                var s = v.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(s)) return s;
                System.Text.Json.JsonDocument.Parse(s); // Validates JSON structure
                return s;
            },
            ["VARCHAR2"] = v => v.ToString(),
            ["XML"] = v => {
                var s = v.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(s)) return s;
                System.Xml.Linq.XDocument.Parse(s); // Validates XML structure
                return s;
            },
            ["PATH"] = v => v.ToString(),
            ["ENCRYPTED"] = v => v.ToString(),
            ["GEOMETRY"] = v => v.ToString(),
            ["GEOGRAPHY"] = v => v.ToString(),
            ["HIERARCHYID"] = v => v.ToString(),
            ["VARBINARY"] = v => v is byte[] b ? b : Convert.FromBase64String(v.ToString() ?? ""),
            ["BINARY"] = v => v is byte[] b ? b : Convert.FromBase64String(v.ToString() ?? ""),
            ["IMAGE"] = v => {
                if (v is byte[] b) return b;
                string s = v.ToString() ?? "";
                if (s.Contains("/") || s.Contains("\\") || s.Contains("."))
                {
                    var lower = s.ToLowerInvariant();
                    if (lower.EndsWith(".jpg") || lower.EndsWith(".jpeg") || lower.EndsWith(".png") || lower.EndsWith(".gif") || lower.EndsWith(".svg"))
                        return s;
                    
                    throw new ArgumentException("Invalid image extension. Supported types are: .jpg, .jpeg, .png, .gif, .svg");
                }
                try { return Convert.FromBase64String(s); } catch { return s; }
            },
            ["MINMAX"] = v => ConvertToMinMax(v),
            ["BLOB"] = v => v is byte[] b ? b : Convert.FromBase64String(v.ToString() ?? ""),
            ["LOB"] = v => v is byte[] b ? b : Convert.FromBase64String(v.ToString() ?? ""),
            ["UNIQUEIDENTIFIER"] = v => v is Guid g ? g : Guid.Parse(v.ToString() ?? Guid.Empty.ToString()),
            ["GUID"] = v => v is Guid g ? g : Guid.Parse(v.ToString() ?? Guid.Empty.ToString()),
            ["UUID"] = v => v is Guid g ? g : Guid.Parse(v.ToString() ?? Guid.Empty.ToString()),
            ["DATETIMEOFFSET"] = v => DateTime.Parse(v.ToString() ?? ""),
            ["VECTOR"] = v => v.ToString(),
            ["SENSITIVE"] = v => v,
            ["SECRET"] = v => v
        };

        /// <summary>Casts a value to the specified SQL type name.</summary>
        public static object? Cast(object value, string? typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return value;
            var baseType = typeName.Split('(')[0].ToUpperInvariant();
            if (_converters.TryGetValue(baseType, out var converter))
            {
                try
                {
                    return converter(value);
                }
                catch (Exception ex)
                {
                    throw new ETL_SQL.Core.Common.Exceptions.ExecutionException($"Failed to cast value '{value}' to type '{typeName}': {ex.Message}", ex);
                }
            }
            return value;
        }

        /// <summary>Registers a custom type converter.</summary>
        public static void Register(string typeName, Func<object, object?> converter) => _converters[typeName] = converter;

        private static MinMaxValue ConvertToMinMax(object value)
        {
            if (value is MinMaxValue mm) return mm;
            if (value is IList list && list.Count >= 2)
                return new MinMaxValue(list[0], list[1]);
            
            return new MinMaxValue(value, value);
        }
    }
}
