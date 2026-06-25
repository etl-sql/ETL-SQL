using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using ETL_SQL.Core.Common;
using ETL_SQL.Data;
using Spectre.Console;

namespace ETL_SQL.Engine;
/// <summary>
/// Handles formatting and printing query results in various formats (Console, JSON, XML).
/// </summary>
public class ResultFormatter
{
    public interface IResultOutputSink
    {
        void Write(Table table);
        void WriteLine(string text);
        void MarkupLine(string markup);
        ConsoleKeyInfo ReadKey(bool intercept);
    }

    private sealed class SpectreResultOutputSink : IResultOutputSink
    {
        public void Write(Table table) => AnsiConsole.Write(table);
        public void WriteLine(string text) => AnsiConsole.Console.WriteLine(text);
        public void MarkupLine(string markup) => AnsiConsole.MarkupLine(markup);
        public ConsoleKeyInfo ReadKey(bool intercept) => Console.ReadKey(intercept);
    }

    public static bool IsJsonMode { get; set; } = false;
    public static bool EnablePaging { get; set; } = false;
    public static bool SuppressOutput { get; set; } = false;
    public static IResultOutputSink OutputSink { get; set; } = new SpectreResultOutputSink();
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
            OutputSink.MarkupLine("\n[yellow]Press any key to see the next result set...[/]");
            OutputSink.ReadKey(true);
        }
        if (isFirst) _resultSetCount++;

        var table = new Table();
        foreach (var col in batch.ColumnNames)
        {
            table.AddColumn(new TableColumn($"[blue]{Markup.Escape(col)}[/]").Centered());
        }

        foreach (var row in batch.Rows)
        {
            var values = batch.ColumnNames.Select(c =>
            {
                var val = SecretRedactor.RedactValue(c, row[c]);
                return Markup.Escape(val?.ToString() ?? "NULL");
            }).ToArray();
            table.AddRow(values);
        }

        if (isFirst)
        {
            OutputSink.Write(table);
        }
        else
        {
            // For streaming batches, we might just append rows if possible, 
            // but Spectre Table doesn't support easy appending to an existing rendered table.
            // For now, we print a new table for each batch (less than ideal but better than nothing).
            // Usually we'd use a Live display, but that's harder to orchestrate across batches.
            OutputSink.Write(table);
        }
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

    /// <summary>
    /// Formats a collection of rows into a JSON string using specific mode (AUTO/PATH).
    /// </summary>
    public static string FormatJson(IEnumerable<Row> rows, ForMode mode, string? root, bool includeNulls = false, bool withoutArrayWrapper = false)
    {
        var list = new List<Dictionary<string, object?>>();
        foreach (var row in rows)
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
            list.Add(dict);
        }

        object output = list;
        if (!string.IsNullOrEmpty(root)) output = new Dictionary<string, object> { [root] = list };

        var json = JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true });

        if (withoutArrayWrapper && !string.IsNullOrEmpty(json))
        {
            json = json.Trim();
            if (json.StartsWith("[") && json.EndsWith("]"))
            {
                json = json.Substring(1, json.Length - 2).Trim();
            }
        }

        return json;
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
        {
            XElement rowEl;
            if (mode == ForMode.EXPLICIT)
            {
                // EXPLICIT mode is complex, but we'll do a basic implementation
                // Expecting 'Tag' and 'Parent' columns
                rowEl = new XElement("row");
            }
            else
            {
                rowEl = new XElement("row");
            }

            foreach (var kv in row.Columns)
            {
                string safeKey = kv.Key.Trim('[', ']');
                object? val = SecretRedactor.RedactValue(safeKey, kv.Value);

                if (val == null)
                {
                    if (!includeNulls) continue;

                    // XSINIL support
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
                    {
                        rowEl.Add(new XElement(tagName, val));
                    }
                    else
                    {
                        rowEl.Add(new XAttribute(tagName, val));
                    }
                }
            }
            rootEl.Add(rowEl);
        }
        return rootEl.ToString();
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


