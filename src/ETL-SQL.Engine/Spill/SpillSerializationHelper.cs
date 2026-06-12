using System.Text.Json;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Spill
{
    /// <summary>
    /// Canonical JSON-to-CLR deserialization helpers shared by all external spill engines.
    /// </summary>
    internal static class SpillSerializationHelper
    {
        /// <summary>
        /// Converts a <see cref="JsonElement"/> to a typed CLR value (decimal, double, bool, string, or null).
        /// Does not apply <see cref="CompoundKey.NormalizeValue"/>; use <see cref="UnwrapValue"/> for key contexts.
        /// </summary>
        internal static object? UnwrapJsonElement(JsonElement element) =>
            element.ValueKind switch
            {
                JsonValueKind.Number when element.TryGetDecimal(out var d) => d,
                JsonValueKind.Number => element.GetDouble(),
                JsonValueKind.String => TryParseString(element.GetString()),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => (object?)element.GetRawText()
            };

        /// <summary>
        /// Full deserialization pipeline for key/hash/comparison values: handles null, DBNull,
        /// JsonElement, and applies <see cref="CompoundKey.NormalizeValue"/> for type consistency.
        /// </summary>
        internal static object? UnwrapValue(object? val)
        {
            if (val == null || val == DBNull.Value) return null;
            if (val is JsonElement je) val = UnwrapJsonElement(je);
            return CompoundKey.NormalizeValue(val);
        }

        private static object? TryParseString(string? s) => s;
    }
}
