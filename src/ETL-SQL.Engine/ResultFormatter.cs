using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using ETL_SQL.Core.Common;
using ETL_SQL.Data;

namespace ETL_SQL.Engine;
/// <summary>
/// Handles formatting and printing query results in various formats (Console, JSON, XML).
/// </summary>
public class ResultFormatter
{
    public interface IResultOutputSink
    {
        void WriteLine(string text);
        ConsoleKeyInfo ReadKey(bool intercept);
    }

    private sealed class ConsoleResultOutputSink : IResultOutputSink
    {
        public void WriteLine(string text) => Console.WriteLine(text);
        public ConsoleKeyInfo ReadKey(bool intercept) => Console.ReadKey(intercept);
    }

    public static bool IsJsonMode { get; set; } = false;
    public static bool EnablePaging { get; set; } = false;
    public static bool SuppressOutput { get; set; } = false;
    public static IResultOutputSink OutputSink { get; set; } = new ConsoleResultOutputSink();
    private static int _resultSetCount = 0;

    /// <summary>Resets the internal result set count for paging.</summary>
    public static void ResetPaging() => _resultSetCount = 0;

    /// <summary>
    /// Prints a batch of rows to the console or as JSON depending on the current mode.
    /// Handles paging if enabled.
    /// </summary>
    public static void PrintBatch(DataTable batch, bool isFirst)
    {
        if (SuppressOutput) return;
        if (IsJsonMode) { PrintJson(batch, isFirst); return; }
        if (batch.Rows.Count == 0) return;

        if (EnablePaging && isFirst && _resultSetCount > 0)
        {
            OutputSink.WriteLine("");
            OutputSink.WriteLine("Press any key to see the next result set...");
            OutputSink.ReadKey(true);
        }
        if (isFirst) _resultSetCount++;

        OutputSink.WriteLine(FormatPlainTable(batch));
    }

    /// <summary>Prints an entire DataTable to the console.</summary>
    public static void PrintTable(DataTable dt)
    {
        PrintBatch(dt, true);
    }

    /// <summary>Prints a batch of rows as a JSON object to standard output.</summary>
    public static void PrintJson(DataTable batch, bool isFirst)
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var data = new
        {
            type = "results",
            isFirst = isFirst,
            columns = batch.ColumnNames,
            rows = batch.Rows.Select(r =>
            {
                var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var col in batch.ColumnNames) dict[col] = SecretRedactor.RedactValue(col, r[col]);
                return dict;
            }).ToList()
        };
        OutputSink.WriteLine(JsonSerializer.Serialize(data, options));
    }

    private static string FormatPlainTable(DataTable batch)
    {
        var columns = batch.ColumnNames.ToList();
        if (columns.Count == 0)
            return string.Empty;

        var rows = batch.Rows
            .Select(row => columns
                .Select(column => SecretRedactor.RedactValue(column, row[column])?.ToString() ?? "NULL")
                .ToList())
            .ToList();

        var widths = columns
            .Select((column, index) => Math.Max(column.Length, rows.Count == 0 ? 0 : rows.Max(row => row[index].Length)))
            .ToArray();

        var builder = new StringBuilder();
        builder.AppendLine(RenderPlainRow(columns, widths));
        builder.AppendLine(RenderPlainSeparator(widths));
        foreach (var row in rows)
            builder.AppendLine(RenderPlainRow(row, widths));
        return builder.ToString().TrimEnd();
    }

    private static string RenderPlainRow(IReadOnlyList<string> values, IReadOnlyList<int> widths) =>
        "| " + string.Join(" | ", values.Select((value, index) => value.PadRight(widths[index]))) + " |";

    private static string RenderPlainSeparator(IReadOnlyList<int> widths) =>
        "|-" + string.Join("-|-", widths.Select(width => new string('-', width))) + "-|";

    /// <summary>
    /// Formats a collection of rows into a JSON string using specific mode (AUTO/PATH).
    /// </summary>
    public static string FormatJson(IEnumerable<Row> rows, ForMode mode, string? root, bool includeNulls = false, bool withoutArrayWrapper = false)
    {
        using var stream = new MemoryStream();
        using (var writer = CreateJsonWriter(stream))
            WriteJsonRows(writer, rows, mode, root, includeNulls);

        var json = Encoding.UTF8.GetString(stream.ToArray());
        return ApplyWithoutArrayWrapper(json, withoutArrayWrapper);
    }

    public static async Task<string> FormatJsonAsync(
        IAsyncEnumerable<DataTable> batches,
        ForMode mode,
        string? root,
        bool includeNulls = false,
        bool withoutArrayWrapper = false,
        CancellationToken ct = default)
    {
        using var stream = new MemoryStream();
        using (var writer = CreateJsonWriter(stream))
        {
            WriteJsonStart(writer, root);
            await foreach (var batch in batches.WithCancellation(ct))
            {
                foreach (var row in batch.Rows)
                    WriteJsonRow(writer, row, mode, includeNulls);
            }
            WriteJsonEnd(writer, root);
        }

        var json = Encoding.UTF8.GetString(stream.ToArray());
        return ApplyWithoutArrayWrapper(json, withoutArrayWrapper);
    }

    /// <summary>Recursively adds a value to a nested dictionary based on a dotted path key.</summary>
    private static void AddToNestedDict(Dictionary<string, object?> dict, string key, object? value)
    {
        var parts = key.Split('.');
        object currentObj = dict;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            var currentDict = (Dictionary<string, object?>)currentObj;
            var part = parts[i];
            if (!currentDict.ContainsKey(part)) currentDict[part] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            currentObj = currentDict[part]!;
        }
        ((Dictionary<string, object?>)currentObj)[parts[parts.Length - 1]] = value;
    }

    /// <summary>
    /// Formats a collection of rows into an XML string using specific mode (AUTO/PATH/EXPLICIT).
    /// </summary>
    public static string FormatXml(IEnumerable<Row> rows, ForMode mode, string? root, bool includeNulls = false, bool useElements = false)
    {
        XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
        var rootEl = new XElement(root?.Trim('[', ']') ?? "root");
        if (includeNulls) rootEl.Add(new XAttribute(XNamespace.Xmlns + "xsi", xsi.NamespaceName));

        foreach (var row in rows)
            rootEl.Add(BuildXmlRow(row, mode, includeNulls, useElements, xsi));

        return rootEl.ToString();
    }

    public static async Task<string> FormatXmlAsync(
        IAsyncEnumerable<DataTable> batches,
        ForMode mode,
        string? root,
        bool includeNulls = false,
        bool useElements = false,
        CancellationToken ct = default)
    {
        XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
        using var stringWriter = new StringWriter();
        using (var writer = XmlWriter.Create(stringWriter, new XmlWriterSettings { OmitXmlDeclaration = true, Indent = true }))
        {
            writer.WriteStartElement(root?.Trim('[', ']') ?? "root");
            if (includeNulls)
                writer.WriteAttributeString("xmlns", "xsi", null, xsi.NamespaceName);

            await foreach (var batch in batches.WithCancellation(ct))
            {
                foreach (var row in batch.Rows)
                    BuildXmlRow(row, mode, includeNulls, useElements, xsi).WriteTo(writer);
            }

            writer.WriteEndElement();
        }

        return stringWriter.ToString();
    }

    private static Utf8JsonWriter CreateJsonWriter(Stream stream) =>
        new(stream, new JsonWriterOptions { Indented = true });

    private static void WriteJsonRows(Utf8JsonWriter writer, IEnumerable<Row> rows, ForMode mode, string? root, bool includeNulls)
    {
        WriteJsonStart(writer, root);
        foreach (var row in rows)
            WriteJsonRow(writer, row, mode, includeNulls);
        WriteJsonEnd(writer, root);
    }

    private static void WriteJsonStart(Utf8JsonWriter writer, string? root)
    {
        if (!string.IsNullOrEmpty(root))
        {
            writer.WriteStartObject();
            writer.WritePropertyName(root);
        }
        writer.WriteStartArray();
    }

    private static void WriteJsonEnd(Utf8JsonWriter writer, string? root)
    {
        writer.WriteEndArray();
        if (!string.IsNullOrEmpty(root))
            writer.WriteEndObject();
    }

    private static void WriteJsonRow(Utf8JsonWriter writer, Row row, ForMode mode, bool includeNulls) =>
        JsonSerializer.Serialize(writer, BuildJsonObject(row, mode, includeNulls));

    private static Dictionary<string, object?> BuildJsonObject(Row row, ForMode mode, bool includeNulls)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in row.Columns)
        {
            if (!includeNulls && kv.Value == null) continue;
            string safeKey = kv.Key.Trim('[', ']');
            var safeValue = SecretRedactor.RedactValue(safeKey, kv.Value);
            if (mode == ForMode.PATH && safeKey.Contains(".")) AddToNestedDict(dict, safeKey, safeValue);
            else dict[safeKey] = safeValue;
        }
        return dict;
    }

    private static string ApplyWithoutArrayWrapper(string json, bool withoutArrayWrapper)
    {
        if (!withoutArrayWrapper || string.IsNullOrEmpty(json)) return json;

        json = json.Trim();
        return json.StartsWith("[") && json.EndsWith("]")
            ? json.Substring(1, json.Length - 2).Trim()
            : json;
    }

    private static XElement BuildXmlRow(Row row, ForMode mode, bool includeNulls, bool useElements, XNamespace xsi)
    {
        var rowEl = new XElement("row");

        foreach (var kv in row.Columns)
        {
            string safeKey = kv.Key.Trim('[', ']');
            object? val = SecretRedactor.RedactValue(safeKey, kv.Value);

            if (val == null)
            {
                if (!includeNulls) continue;

                var nilEl = new XElement(safeKey.Replace(".", "_").Replace(" ", "_"), new XAttribute(xsi + "nil", "true"));
                rowEl.Add(nilEl);
                continue;
            }

            if (mode == ForMode.PATH && safeKey.Contains("."))
            {
                AddNestedXmlElement(rowEl, safeKey, val, useElements, mode);
            }
            else
            {
                var tagName = safeKey.Replace(".", "_").Replace(" ", "_");
                if (useElements || mode == ForMode.PATH)
                    rowEl.Add(new XElement(tagName, val));
                else
                    rowEl.Add(new XAttribute(tagName, val));
            }
        }

        return rowEl;
    }

    /// <summary>Recursively adds an XML element or attribute based on a dotted path key.</summary>
    private static void AddNestedXmlElement(XElement parent, string key, object? value, bool useElements, ForMode mode)
    {
        var parts = key.Split('.').Select(p => p.Trim('[', ']')).ToArray();
        var current = parent;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            var tag = parts[i].Replace(" ", "_");
            var el = current.Elements(tag).FirstOrDefault();
            if (el == null) { el = new XElement(tag); current.Add(el); }
            current = el;
        }

        var finalPart = parts[parts.Length - 1];
        bool isAttribute = finalPart.StartsWith("@");
        var finalTag = (isAttribute ? finalPart.Substring(1) : finalPart).Replace(" ", "_");

        if (useElements || (mode == ForMode.PATH && !isAttribute))
        {
            current.Add(new XElement(finalTag, value));
        }
        else
        {
            current.Add(new XAttribute(finalTag, value == null ? "" : value));
        }
    }
}


