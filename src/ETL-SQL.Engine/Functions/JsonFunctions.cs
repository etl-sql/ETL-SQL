using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Functions;
using ETL_SQL.Data;
using ETL_SQL.Engine.Functions;
using JArray = System.Text.Json.Nodes.JsonArray;
using JNode = System.Text.Json.Nodes.JsonNode;
using JObject = System.Text.Json.Nodes.JsonObject;
using JValue = System.Text.Json.Nodes.JsonValue;

namespace ETL_SQL.Engine.Functions
{
    /// <summary>
    /// Provides JSON scalar and table-valued functions: JSON_VALUE, JSON_QUERY, JSON_MODIFY,
    /// ISJSON, JSON_EXISTS, JSON_OBJECT, JSON_ARRAY, JSON_TABLE, OPENJSON.
    /// </summary>
    public static class JsonFunctions
    {
        public sealed record JsonTableColumn(
            string Name,
            string? TypeName,
            string? Path,
            bool ForOrdinality,
            bool Exists,
            object? DefaultOnEmpty,
            object? DefaultOnError);

        /// <summary>Registers all JSON functions into the global function registry.</summary>
        public static void Register(IFunctionRegistry registry)
        {
            registry.RegisterWithHelp("JSON_VALUE", JsonValue, "JSON_VALUE(json, path): Extracts a scalar value from a JSON string at the given path.");
            registry.RegisterWithHelp("JSON_QUERY", JsonQuery, "JSON_QUERY(json, path): Extracts an object or array fragment from a JSON string.");
            registry.RegisterWithHelp("JSON_MODIFY", JsonModify, "JSON_MODIFY(json, path, val): Updates or inserts a value in a JSON string.");
            registry.RegisterWithHelp("ISJSON", IsJson, "ISJSON(str): Returns 1 if the string is valid JSON, 0 otherwise.");
            registry.RegisterWithHelp("JSON_EXISTS", JsonExists, "JSON_EXISTS(json, path): Returns 1 if the path exists in the JSON string.");
            registry.RegisterWithHelp("JSON_OBJECT", JsonObject, "JSON_OBJECT(k1, v1, k2, v2, ...): Constructs a JSON object from key/value pairs.");
            registry.RegisterWithHelp("JSON_ARRAY", JsonArray, "JSON_ARRAY(v1, v2, ...): Constructs a JSON array from the provided values.");
            registry.RegisterWithHelp("JSON_TABLE", JsonTable, "JSON_TABLE(json, path): Expands a JSON array or object into a table.");
            registry.RegisterWithHelp("JSON_EXTRACT", JsonValue, "JSON_EXTRACT(json, path): Alias for JSON_VALUE.");
            registry.RegisterWithHelp("OPENJSON", OpenJson, "OPENJSON(json[, path]): Expands JSON into a table (SQL Server style).");
            registry.RegisterWithHelp("JSON_GET", JsonGet, "JSON_GET(json, key_or_index): One JSON access step — object field by string key or array element by integer index (negative counts from the end) — returned as JSON. The -> operator compiles to this; chain for deep access.");
            registry.RegisterWithHelp("JSON_GET_TEXT", JsonGetText, "JSON_GET_TEXT(json, key_or_index): Like JSON_GET but returns the value as text (strings unquoted). The ->> operator compiles to this.");
        }

        // ── Scalar helpers ────────────────────────────────────────────────────

        /// <summary>
        /// One JSON access step (the -> operator, PostgreSQL semantics): an object field by string
        /// key, or an array element by integer index (negative indexes count from the end). Returns
        /// the selected value serialised as JSON (strings keep their quotes), so results chain into
        /// further -> / ->> steps. Null-propagating: a missing key, out-of-range index, kind
        /// mismatch, or invalid JSON yields NULL, never an error.
        /// </summary>
        private static object? JsonGet(List<object?> args, IExecutionContext ctx)
        {
            var element = NavigateOneStep(args);
            return element?.GetRawText();
        }

        /// <summary>
        /// One JSON access step returning text (the ->> operator, PostgreSQL semantics): strings are
        /// returned unquoted, numbers/booleans as their literal text, JSON null as NULL, and
        /// objects/arrays as their raw JSON text.
        /// </summary>
        private static object? JsonGetText(List<object?> args, IExecutionContext ctx)
        {
            var element = NavigateOneStep(args);
            if (element == null) return null;
            return element.Value.ValueKind switch
            {
                JsonValueKind.String => element.Value.GetString(),
                JsonValueKind.Null => null,
                _ => element.Value.GetRawText()
            };
        }

        /// <summary>Shared -> / ->> navigation: selects one field or element from the JSON in args[0].</summary>
        private static JsonElement? NavigateOneStep(List<object?> args)
        {
            if (args.Count < 2) return null;
            string? json = args[0]?.ToString();
            object? selector = args[1];
            if (string.IsNullOrEmpty(json) || selector == null) return null;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Integer selector → array element (negative counts from the end, as in PostgreSQL).
                if (IsIntegral(selector, out var index))
                {
                    if (root.ValueKind != JsonValueKind.Array) return null;
                    var length = root.GetArrayLength();
                    if (index < 0) index += length;
                    if (index < 0 || index >= length) return null;
                    return root[index].Clone();
                }

                // String selector → object field.
                if (root.ValueKind != JsonValueKind.Object) return null;
                return root.TryGetProperty(selector.ToString()!, out var prop) ? prop.Clone() : null;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                return null;
            }
        }

        private static bool IsIntegral(object selector, out int index)
        {
            switch (selector)
            {
                case int i: index = i; return true;
                case long l when l >= int.MinValue && l <= int.MaxValue: index = (int)l; return true;
                case decimal d when d == decimal.Truncate(d) && d >= int.MinValue && d <= int.MaxValue:
                    index = (int)d; return true;
                case double db when db == Math.Truncate(db) && db >= int.MinValue && db <= int.MaxValue:
                    index = (int)db; return true;
                default: index = 0; return false;
            }
        }

        /// <summary>
        /// Extracts a scalar value from a JSON string at the given JSONPath.
        /// Returns null if the path does not exist or resolves to an object/array.
        /// Example: JSON_VALUE('{"name":"Alice"}', '$.name') → 'Alice'
        /// </summary>
        private static object? JsonValue(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 2) return null;
            string? json = args[0]?.ToString();
            string? path = args[1]?.ToString();
            if (string.IsNullOrEmpty(json)) return null;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var element = NavigatePath(doc.RootElement, path ?? "$");
                if (element == null) return null;
                return element.Value.ValueKind switch
                {
                    JsonValueKind.String => element.Value.GetString(),
                    JsonValueKind.Number => element.Value.TryGetDecimal(out var d) ? (object?)d : element.Value.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => null,
                    // Object/Array → return null (use JSON_QUERY for those)
                    _ => null
                };
            }
            catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException) { return null; }
        }

        /// <summary>
        /// Extracts an object or array fragment from a JSON string at the given JSONPath.
        /// Returns the fragment serialised as a JSON string, or null if not found.
        /// Example: JSON_QUERY('{"a":{"b":1}}', '$.a') → '{"b":1}'
        /// </summary>
        private static object? JsonQuery(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 2) return null;
            string? json = args[0]?.ToString();
            string? path = args[1]?.ToString();
            if (string.IsNullOrEmpty(json)) return null;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var element = NavigatePath(doc.RootElement, path ?? "$");
                if (element == null) return null;
                return element.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array
                    ? element.Value.GetRawText()
                    : null;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException) { return null; }
        }

        /// <summary>
        /// Updates or inserts a value at the given JSONPath in a JSON string.
        /// Returns the modified JSON string.
        /// Example: JSON_MODIFY('{"a":1}', '$.a', 2) → '{"a":2}'
        /// </summary>
        private static object? JsonModify(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 3) return args.FirstOrDefault();
            string? json = args[0]?.ToString();
            string? path = args[1]?.ToString();
            object? newValue = args[2];
            if (string.IsNullOrEmpty(json)) return json;

            try
            {
                var node = JNode.Parse(json);
                if (node == null) return json;
                SetPath(node, path ?? "$", newValue);
                return node.ToJsonString();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException) { return json; }
        }

        /// <summary>
        /// Returns 1 if the string is valid JSON, 0 otherwise.
        /// Example: ISJSON('{"a":1}') → 1
        /// </summary>
        private static object? IsJson(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 1 || args[0] == null) return null;
            try
            {
                JsonDocument.Parse(args[0]!.ToString()!);
                return 1m;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException) { return 0m; }
        }

        /// <summary>
        /// Returns 1 if the path exists in the JSON string, 0 otherwise.
        /// Example: JSON_EXISTS('{"a":1}', '$.a') → 1
        /// </summary>
        private static object? JsonExists(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 2 || args[0] == null || args[1] == null) return null;
            string? json = args[0]?.ToString();
            string? path = args[1]?.ToString();
            if (string.IsNullOrEmpty(json)) return null;

            try
            {
                using var doc = JsonDocument.Parse(json);
                return NavigatePath(doc.RootElement, path ?? "$") != null ? 1m : 0m;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException) { return 0m; }
        }

        /// <summary>
        /// Constructs a JSON object from alternating key/value arguments.
        /// Example: JSON_OBJECT('name', 'Alice', 'age', 30) → '{"name":"Alice","age":30}'
        /// </summary>
        private static object? JsonObject(List<object?> args, IExecutionContext ctx)
        {
            var dict = new Dictionary<string, object?>();
            for (int i = 0; i + 1 < args.Count; i += 2)
                dict[args[i]?.ToString() ?? $"key{i}"] = args[i + 1];
            return SerializeToJson(dict);
        }

        /// <summary>
        /// Constructs a JSON array from the provided arguments.
        /// Example: JSON_ARRAY(1, 'two', 3) → '[1,"two",3]'
        /// </summary>
        private static object? JsonArray(List<object?> args, IExecutionContext ctx)
        {
            return SerializeToJson(args);
        }

        // ── Table-valued functions ────────────────────────────────────────────

        /// <summary>
        /// Expands a JSON array at the given path into a table with one VALUE column.
        /// Each element becomes a row. Object elements are serialised as JSON strings.
        /// Example: SELECT * FROM JSON_TABLE('{"items":[1,2,3]}', '$.items')
        /// </summary>
        private static async Task<object?> JsonTable(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 1) return new DataTable();
            string? json = args[0]?.ToString();
            string? path = args.Count >= 2 ? args[1]?.ToString() : "$";
            if (string.IsNullOrEmpty(json)) return new DataTable();

            try
            {
                using var doc = JsonDocument.Parse(json);
                var element = NavigatePath(doc.RootElement, path ?? "$");
                if (element == null) return new DataTable();

                if (element.Value.ValueKind == JsonValueKind.Array)
                {
                    return await BuildTableFromArray(element.Value);
                }
                else if (element.Value.ValueKind == JsonValueKind.Object)
                {
                    return await BuildTableFromObject(element.Value);
                }

                // Scalar — wrap single value
                var dt = new DataTable();
                dt.SetColumns(new[] { "VALUE" });
                await dt.AddRowAsync(new Row { ["VALUE"] = ScalarFromElement(element.Value) });
                return dt;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException) { return new DataTable(); }
        }

        public static async Task<DataTable> BuildJsonTableWithColumns(string? json, string? rowPath, IReadOnlyList<JsonTableColumn> columns)
        {
            var result = new DataTable();
            result.SetColumns(columns.Select(c => c.Name));

            if (string.IsNullOrEmpty(json) || columns.Count == 0) return result;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var rowElements = ResolveRowElements(doc.RootElement, rowPath ?? "$");
                int ordinal = 1;
                foreach (var rowElement in rowElements)
                {
                    var row = new Row(result.Schema);
                    foreach (var column in columns)
                    {
                        row[column.Name] = ResolveJsonTableColumnValue(rowElement, column, ordinal);
                    }
                    await result.AddRowAsync(row);
                    ordinal++;
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                return new DataTable();
            }

            return result;
        }

        /// <summary>
        /// SQL Server-compatible OPENJSON — expands a JSON string into key/value/type rows,
        /// or (when the JSON is an array of objects) into a multi-column table.
        /// </summary>
        private static async Task<object?> OpenJson(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 1) return new DataTable();
            string? json = args[0]?.ToString();
            string? path = args.Count >= 2 ? args[1]?.ToString() : "$";
            if (string.IsNullOrEmpty(json)) return new DataTable();

            try
            {
                using var doc = JsonDocument.Parse(json);
                var element = NavigatePath(doc.RootElement, path ?? "$");
                if (element == null) return new DataTable();

                if (element.Value.ValueKind == JsonValueKind.Array)
                {
                    // If it's an array of objects, build column-per-key table
                    var firstObj = element.Value.EnumerateArray()
                        .FirstOrDefault(e => e.ValueKind == JsonValueKind.Object);
                    if (firstObj.ValueKind == JsonValueKind.Object)
                        return await BuildTableFromArray(element.Value);

                    // Array of scalars → key/value/type
                    return await BuildKeyValueTypeTable(element.Value);
                }
                else if (element.Value.ValueKind == JsonValueKind.Object)
                {
                    // Object → key/value/type rows
                    return await BuildKeyValueTypeTable(element.Value);
                }

                return new DataTable();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException) { return new DataTable(); }
        }

        // ── Table-building helpers ────────────────────────────────────────────

        private static object? ResolveJsonTableColumnValue(JsonElement rowElement, JsonTableColumn column, int ordinal)
        {
            if (column.ForOrdinality) return (decimal)ordinal;

            try
            {
                var target = NavigatePath(rowElement, column.Path ?? "$");
                if (column.Exists) return target != null ? 1m : 0m;
                if (target == null) return column.DefaultOnEmpty;

                object? value = ScalarFromElement(target.Value);
                if (!string.IsNullOrWhiteSpace(column.TypeName))
                {
                    value = EvaluationUtils.CastToType(value, column.TypeName);
                }
                return value;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                return column.DefaultOnError;
            }
        }

        private static List<JsonElement> ResolveRowElements(JsonElement root, string path)
        {
            if (path.EndsWith("[*]", StringComparison.Ordinal))
            {
                var array = NavigatePath(root, path[..^3]);
                if (array?.ValueKind == JsonValueKind.Array) return array.Value.EnumerateArray().ToList();
                return new List<JsonElement>();
            }

            var element = NavigatePath(root, path);
            if (element == null) return new List<JsonElement>();
            if (element.Value.ValueKind == JsonValueKind.Array) return element.Value.EnumerateArray().ToList();
            return new List<JsonElement> { element.Value };
        }

        private static async Task<DataTable> BuildTableFromArray(JsonElement array)
        {
            var rows = new List<JsonElement>(array.GetArrayLength());
            foreach (var item in array.EnumerateArray()) rows.Add(item);

            if (rows.Count == 0) return new DataTable();

            if (rows[0].ValueKind == JsonValueKind.Object)
            {
                // Discover columns from all rows (union of keys)
                var cols = new List<string>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in rows)
                    foreach (var prop in row.EnumerateObject())
                        if (seen.Add(prop.Name)) cols.Add(prop.Name);

                var dt = new DataTable();
                dt.SetColumns(cols);
                foreach (var row in rows)
                {
                    var r = new Row();
                    foreach (var prop in row.EnumerateObject())
                        r[prop.Name] = ScalarFromElement(prop.Value);
                    await dt.AddRowAsync(r);
                }
                return dt;
            }
            else
            {
                // Array of scalars → single VALUE column
                var dt = new DataTable();
                dt.SetColumns(new[] { "VALUE" });
                foreach (var item in rows)
                    await dt.AddRowAsync(new Row { ["VALUE"] = ScalarFromElement(item) });
                return dt;
            }
        }

        private static async Task<DataTable> BuildTableFromObject(JsonElement obj)
        {
            var dt = new DataTable();
            dt.SetColumns(new[] { "KEY", "VALUE" });
            foreach (var prop in obj.EnumerateObject())
                await dt.AddRowAsync(new Row { ["KEY"] = prop.Name, ["VALUE"] = ScalarFromElement(prop.Value) });
            return dt;
        }

        private static async Task<DataTable> BuildKeyValueTypeTable(JsonElement element)
        {
            var dt = new DataTable();
            dt.SetColumns(new[] { "KEY", "VALUE", "TYPE" });

            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in element.EnumerateObject())
                    await dt.AddRowAsync(new Row { ["KEY"] = prop.Name, ["VALUE"] = prop.Value.GetRawText().Trim('"'), ["TYPE"] = (decimal)JsonTypeId(prop.Value) });
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                int idx = 0;
                foreach (var item in element.EnumerateArray())
                    await dt.AddRowAsync(new Row { ["KEY"] = idx++.ToString(), ["VALUE"] = item.GetRawText().Trim('"'), ["TYPE"] = (decimal)JsonTypeId(item) });
            }
            return dt;
        }

        // ── JSONPath traversal ────────────────────────────────────────────────

        /// <summary>
        /// Navigates a JSONPath expression ($ prefix, dot/bracket notation).
        /// Supports: $, $.key, $.a.b, $.arr[n], $[n], $.arr[*] (returns first element).
        /// </summary>
        private static JsonElement? NavigatePath(JsonElement root, string path)
        {
            if (string.IsNullOrEmpty(path) || path == "$") return root;

            // Strip leading '$'
            string p = path.StartsWith("$") ? path.Substring(1) : path;
            JsonElement current = root;

            int i = 0;
            while (i < p.Length)
            {
                if (p[i] == '.')
                {
                    i++;
                    // Read property name up to next . or [
                    int start = i;
                    while (i < p.Length && p[i] != '.' && p[i] != '[') i++;
                    string key = p.Substring(start, i - start);
                    if (string.IsNullOrEmpty(key)) continue;
                    if (current.ValueKind != JsonValueKind.Object) return null;
                    if (!current.TryGetProperty(key, out current)) return null;
                }
                else if (p[i] == '[')
                {
                    i++;
                    int start = i;
                    while (i < p.Length && p[i] != ']') i++;
                    string idx = p.Substring(start, i - start);
                    if (i < p.Length) i++; // skip ']'

                    if (idx == "*")
                    {
                        if (current.ValueKind != JsonValueKind.Array || current.GetArrayLength() == 0) return null;
                        current = current[0];
                    }
                    else if (int.TryParse(idx, out int n))
                    {
                        if (current.ValueKind != JsonValueKind.Array || n < 0 || n >= current.GetArrayLength()) return null;
                        current = current[n];
                    }
                    else return null;
                }
                else i++;
            }
            return current;
        }

        // ── JSON_MODIFY path writing ──────────────────────────────────────────

        private static void SetPath(JNode root, string path, object? value)
        {
            if (path == "$") return; // Can't replace root this way

            string p = path.StartsWith("$") ? path.Substring(1) : path;
            var segments = ParsePathSegments(p);
            if (segments.Count == 0) return;

            JNode? current = root;
            for (int i = 0; i < segments.Count - 1; i++)
            {
                var seg = segments[i];
                if (seg.IsIndex)
                    current = (current as JArray)?[seg.Index];
                else
                    current = (current as JObject)?[seg.Key];
                if (current == null) return;
            }

            var last = segments[segments.Count - 1];
            var jsonValue = ValueToJsonNode(value);

            if (last.IsIndex)
            {
                if (current is JArray arr && last.Index >= 0 && last.Index < arr.Count)
                    arr[last.Index] = jsonValue;
            }
            else
            {
                if (current is JObject obj)
                    obj[last.Key] = jsonValue;
            }
        }

        private static JNode? ValueToJsonNode(object? value)
        {
            if (value == null) return null;
            if (value is bool b) return JValue.Create(b);
            if (value is decimal d) return JValue.Create(d);
            if (value is double dbl) return JValue.Create(dbl);
            if (value is int n) return JValue.Create(n);
            if (value is long l) return JValue.Create(l);
            try { return JNode.Parse(value.ToString()!); } catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException) { }
            return JValue.Create(value.ToString());
        }

        // ── Misc helpers ──────────────────────────────────────────────────────

        private static object? ScalarFromElement(JsonElement el) => el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.TryGetDecimal(out var d) ? (object?)d : el.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => el.GetRawText() // Object or Array → return raw JSON string
        };

        private static int JsonTypeId(JsonElement el) => el.ValueKind switch
        {
            JsonValueKind.Null => 0,
            JsonValueKind.String => 1,
            JsonValueKind.Number => 2,
            JsonValueKind.True => 3,
            JsonValueKind.False => 3,
            JsonValueKind.Array => 4,
            JsonValueKind.Object => 5,
            _ => 0
        };

        private static string SerializeToJson(object? value)
        {
            return JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = false });
        }

        private record PathSegment(bool IsIndex, string Key = "", int Index = 0);

        private static List<PathSegment> ParsePathSegments(string p)
        {
            var segments = new List<PathSegment>();
            int i = 0;
            while (i < p.Length)
            {
                if (p[i] == '.')
                {
                    i++;
                    int start = i;
                    while (i < p.Length && p[i] != '.' && p[i] != '[') i++;
                    string key = p.Substring(start, i - start);
                    if (!string.IsNullOrEmpty(key)) segments.Add(new PathSegment(false, Key: key));
                }
                else if (p[i] == '[')
                {
                    i++;
                    int start = i;
                    while (i < p.Length && p[i] != ']') i++;
                    string idx = p.Substring(start, i - start);
                    if (i < p.Length) i++;
                    if (int.TryParse(idx, out int n)) segments.Add(new PathSegment(true, Index: n));
                }
                else i++;
            }
            return segments;
        }
    }
}
