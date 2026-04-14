using System;
using System.Collections.Generic;

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
            ["INT"] = v => Convert.ToInt32(v),
            ["INTEGER"] = v => Convert.ToInt32(v),
            ["DECIMAL"] = v => Convert.ToDecimal(v),
            ["MONEY"] = v => Convert.ToDecimal(v),
            ["NUMERIC"] = v => Convert.ToDecimal(v),
            ["FLOAT"] = v => Convert.ToDouble(v),
            ["DOUBLE"] = v => Convert.ToDouble(v),
            ["BIT"] = v => Convert.ToBoolean(v),
            ["BOOLEAN"] = v => Convert.ToBoolean(v),
            ["BOOL"] = v => Convert.ToBoolean(v),
            ["TINYINT"] = v => Convert.ToByte(v),
            ["SMALLINT"] = v => Convert.ToInt16(v),
            ["BIGINT"] = v => Convert.ToInt64(v),
            ["REAL"] = v => Convert.ToSingle(v),
            ["DATETIME"] = v => DateTime.Parse(v.ToString() ?? ""),
            ["DATE"] = v => DateTime.Parse(v.ToString() ?? ""),
            ["TIMESTAMP"] = v => DateTime.Parse(v.ToString() ?? ""),
            ["TIME"] = v => TimeSpan.Parse(v.ToString() ?? "00:00:00"),
            ["STRING"] = v => v.ToString(),
            ["VARCHAR"] = v => v.ToString(),
            ["NVARCHAR"] = v => v.ToString(),
            ["TEXT"] = v => v.ToString(),
            ["NTEXT"] = v => v.ToString(),
            ["CHAR"] = v => v.ToString(),
            ["NCHAR"] = v => v.ToString(),
            ["JSON"] = v => v.ToString(),
            ["XML"] = v => v.ToString(),
            ["PATH"] = v => v.ToString(),
            ["ENCRYPTED"] = v => v.ToString(),
            ["VARBINARY"] = v => v is byte[] b ? b : Convert.FromBase64String(v.ToString() ?? ""),
            ["BINARY"] = v => v is byte[] b ? b : Convert.FromBase64String(v.ToString() ?? ""),
            ["IMAGE"] = v => v is byte[] b ? b : Convert.FromBase64String(v.ToString() ?? ""),
            ["BLOB"] = v => v is byte[] b ? b : Convert.FromBase64String(v.ToString() ?? ""),
            ["LOB"] = v => v is byte[] b ? b : Convert.FromBase64String(v.ToString() ?? ""),
            ["UNIQUEIDENTIFIER"] = v => v is Guid g ? g : Guid.Parse(v.ToString() ?? Guid.Empty.ToString()),
            ["GUID"] = v => v is Guid g ? g : Guid.Parse(v.ToString() ?? Guid.Empty.ToString()),
            ["UUID"] = v => v is Guid g ? g : Guid.Parse(v.ToString() ?? Guid.Empty.ToString()),
            ["DATETIMEOFFSET"] = v => DateTime.Parse(v.ToString() ?? ""),
            ["VECTOR"] = v => v.ToString()
        };

        /// <summary>Casts a value to the specified SQL type name.</summary>
        public static object? Cast(object value, string typeName)
        {
            var baseType = typeName.Split('(')[0].ToUpperInvariant();
            if (_converters.TryGetValue(baseType, out var converter))
            {
                return converter(value);
            }
            return value;
        }

        /// <summary>Registers a custom type converter.</summary>
        public static void Register(string typeName, Func<object, object?> converter) => _converters[typeName] = converter;
    }
}
