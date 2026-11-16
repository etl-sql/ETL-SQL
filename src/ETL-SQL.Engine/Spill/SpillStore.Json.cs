using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Engine.Spill;

public partial class SpillStore
{
    // COMPAT_BREAK: 0.22 — untyped JSON spills cannot distinguish text from dates or numbers.
    // Saved sessions containing those chunks must be recreated; never guess their scalar types.
    private static class JsonRowCodec
    {
        public const string Header = "ETLSQL-JSON-1";
        private static readonly JsonSerializerOptions Options = new()
        {
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };

        private sealed record Cell(string Type, JsonElement Value);

        public static string Encode(Dictionary<string, object?> columns)
        {
            var cells = new Dictionary<string, Cell>(columns.Count, columns.Comparer);
            foreach (var (name, value) in columns)
            {
                var type = value switch
                {
                    null => "null",
                    string => "string",
                    bool => "bool",
                    decimal => "decimal",
                    double => "double",
                    float => "float",
                    byte => "byte",
                    sbyte => "sbyte",
                    short => "short",
                    ushort => "ushort",
                    int => "int",
                    uint => "uint",
                    long => "long",
                    ulong => "ulong",
                    char => "char",
                    DateTime => "datetime",
                    DateTimeOffset => "datetimeoffset",
                    TimeSpan => "timespan",
                    DateOnly => "dateonly",
                    TimeOnly => "timeonly",
                    Guid => "guid",
                    byte[] => "binary",
                    _ => "json"
                };
                // DateTime's binary representation retains its kind and ambiguous local-time bit.
                var payload = value is DateTime date ? date.ToBinary() : value;
                cells.Add(name, new Cell(type, JsonSerializer.SerializeToElement(payload, Options)));
            }
            return JsonSerializer.Serialize(cells, Options);
        }

        public static Dictionary<string, object?> Decode(string line)
        {
            try
            {
                var cells = JsonSerializer.Deserialize<Dictionary<string, Cell>>(line, Options);
                if (cells == null)
                    throw new JsonException();
                var columns = new Dictionary<string, object?>(cells.Count);
                foreach (var (name, cell) in cells)
                {
                    if (cell == null) throw new JsonException();
                    columns.Add(name, cell.Type switch
                    {
                        "null" when cell.Value.ValueKind == JsonValueKind.Null => null,
                        "string" => cell.Value.GetString(),
                        "bool" => cell.Value.GetBoolean(),
                        "decimal" => cell.Value.GetDecimal(),
                        "double" => cell.Value.Deserialize<double>(Options),
                        "float" => cell.Value.Deserialize<float>(Options),
                        "byte" => cell.Value.GetByte(),
                        "sbyte" => cell.Value.GetSByte(),
                        "short" => cell.Value.GetInt16(),
                        "ushort" => cell.Value.GetUInt16(),
                        "int" => cell.Value.GetInt32(),
                        "uint" => cell.Value.GetUInt32(),
                        "long" => cell.Value.GetInt64(),
                        "ulong" => cell.Value.GetUInt64(),
                        "char" => cell.Value.Deserialize<char>(Options),
                        "datetime" => DateTime.FromBinary(cell.Value.GetInt64()),
                        "datetimeoffset" => cell.Value.GetDateTimeOffset(),
                        "timespan" => cell.Value.Deserialize<TimeSpan>(Options),
                        "dateonly" => cell.Value.Deserialize<DateOnly>(Options),
                        "timeonly" => cell.Value.Deserialize<TimeOnly>(Options),
                        "guid" => cell.Value.GetGuid(),
                        "binary" => cell.Value.GetBytesFromBase64(),
                        "json" => cell.Value,
                        _ => throw new JsonException()
                    });
                }
                return columns;
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or ArgumentException or OverflowException)
            {
                // Do not include file contents or provider exception text in diagnostics.
                throw new ExecutionException("Unsupported or invalid JSON spill data. Rerun the statement or recreate the saved session.");
            }
        }
    }
}
