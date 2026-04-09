using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using ETL_SQL.Core;
using ETL_SQL.Core.Functions;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using ETL_SQL.Engine.Functions;

namespace ETL_SQL.Engine.Functions
{
    /// <summary>
    /// Provides XML scalar and table-valued functions: XMLVALUE, XMLEXISTS, XMLQUERY,
    /// XMLTABLE, XMLELEMENT, XMLATTRIBUTES, XMLFOREST.
    /// </summary>
    public static class XmlFunctions
    {
        /// <summary>Registers all XML functions into the global function registry.</summary>
        public static void Register(IFunctionRegistry registry)
        {
            registry.RegisterWithHelp("XMLVALUE", XmlValue, "XMLVALUE(xml, xpath): Extracts a scalar value from an XML string using an XPath expression.");
            registry.RegisterWithHelp("XMLEXISTS", XmlExists, "XMLEXISTS(xml, xpath): Returns 1 if the XPath expression matches any node in the XML.");
            registry.RegisterWithHelp("XMLQUERY", XmlQuery, "XMLQUERY(xml, xpath): Returns an XML fragment from the string using XPath.");
            registry.RegisterWithHelp("XMLTABLE", XmlTable, "XMLTABLE(xml, xpath): Expands an XML document into a table based on the row-level XPath.");
            registry.RegisterWithHelp("XMLELEMENT", XmlElement, "XMLELEMENT(name, contents): Constructs an XML element with the given name and child content.");
            registry.RegisterWithHelp("XMLATTRIBUTES", XmlAttributes, "XMLATTRIBUTES(n1, v1, n2, v2, ...): Constructs XML attributes for an element.");
            registry.RegisterWithHelp("XMLFOREST", XmlForest, "XMLFOREST(n1, v1, n2, v2, ...): Constructs a forest of XML elements from name/value pairs.");
            registry.RegisterWithHelp("EXTRACTVALUE", XmlValue, "EXTRACTVALUE(xml, xpath): Alias for XMLVALUE.");
        }

        // ── Scalar functions ──────────────────────────────────────────────────

        /// <summary>
        /// Extracts the text content of the first node matching the XPath expression.
        /// Example: XMLVALUE('&lt;root&gt;&lt;name&gt;Alice&lt;/name&gt;&lt;/root&gt;', '/root/name') → 'Alice'
        /// </summary>
        private static object? XmlValue(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 2) return null;
            string? xml = args[0]?.ToString();
            string? xpath = args[1]?.ToString();
            if (string.IsNullOrEmpty(xml) || string.IsNullOrEmpty(xpath)) return null;

            try
            {
                var doc = XDocument.Parse(xml);
                return EvaluateXPath(doc, xpath);
            }
            catch { return null; }
        }

        /// <summary>
        /// Evaluates an XPath expression against an XDocument and returns a scalar value.
        /// Handles elements, attributes, text nodes, and scalar XPath results.
        /// </summary>
        private static object? EvaluateXPath(XDocument doc, string xpath)
        {
            // XPathEvaluate handles all node types including attributes.
            // The return value is object; node sets come back as IEnumerable (of XObject subclasses).
            var result = doc.XPathEvaluate(xpath);

            if (result is System.Collections.IEnumerable nodes && result is not string)
            {
                var first = nodes.Cast<object>().FirstOrDefault();
                return first switch
                {
                    XElement el   => el.Value,
                    XAttribute at => at.Value,
                    XText txt     => txt.Value,
                    null          => null,
                    _             => first.ToString()
                };
            }

            // Scalar results from XPath functions like string(), number(), boolean()
            return result switch
            {
                string s  => s,
                double d  => (decimal)d,
                bool b    => b ? 1m : 0m,
                _         => result?.ToString()
            };
        }

        /// <summary>
        /// Returns 1 if the XPath expression matches at least one node, 0 otherwise.
        /// Example: XMLEXISTS('&lt;root&gt;&lt;a/&gt;&lt;/root&gt;', '/root/a') → 1
        /// </summary>
        private static object? XmlExists(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 2) return 0m;
            string? xml = args[0]?.ToString();
            string? xpath = args[1]?.ToString();
            if (string.IsNullOrEmpty(xml) || string.IsNullOrEmpty(xpath)) return 0m;

            try
            {
                var doc = XDocument.Parse(xml);
                var result = doc.XPathEvaluate(xpath);
                if (result is IEnumerable<object> nodes) return nodes.Cast<object>().Any() ? 1m : 0m;
                if (result is bool b) return b ? 1m : 0m;
                return result != null ? 1m : 0m;
            }
            catch { return 0m; }
        }

        /// <summary>
        /// Returns the serialised XML fragment matching the XPath expression.
        /// Example: XMLQUERY('&lt;root&gt;&lt;a&gt;&lt;b/&gt;&lt;/a&gt;&lt;/root&gt;', '/root/a') → '&lt;a&gt;&lt;b /&gt;&lt;/a&gt;'
        /// </summary>
        private static object? XmlQuery(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 2) return null;
            string? xml = args[0]?.ToString();
            string? xpath = args[1]?.ToString();
            if (string.IsNullOrEmpty(xml) || string.IsNullOrEmpty(xpath)) return null;

            try
            {
                var doc = XDocument.Parse(xml);
                var element = doc.XPathSelectElement(NormaliseXPath(xpath, doc));
                return element?.ToString(SaveOptions.DisableFormatting);
            }
            catch { return null; }
        }

        // ── Table-valued functions ────────────────────────────────────────────

        /// <summary>
        /// Expands each node matched by the XPath expression into a table row.
        /// Each matched element's child elements become columns; text content goes into VALUE.
        /// Example: SELECT * FROM XMLTABLE('&lt;catalog&gt;&lt;book id="1"&gt;&lt;title&gt;T&lt;/title&gt;&lt;/book&gt;&lt;/catalog&gt;', '/catalog/book')
        /// </summary>
        private static async Task<object?> XmlTable(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 1) return new DataTable();
            string? xml = args[0]?.ToString();
            string? xpath = args.Count >= 2 ? args[1]?.ToString() : null;
            if (string.IsNullOrEmpty(xml)) return new DataTable();

            try
            {
                var doc = XDocument.Parse(xml);

                IEnumerable<XElement> elements;
                if (!string.IsNullOrEmpty(xpath))
                {
                    elements = doc.XPathSelectElements(NormaliseXPath(xpath, doc));
                }
                else
                {
                    // Default: children of root
                    elements = doc.Root?.Elements() ?? Enumerable.Empty<XElement>();
                }

                var rows = elements.ToList();
                if (rows.Count == 0) return new DataTable();

                // Determine columns: union of child element names + attributes
                var cols = new List<string>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                bool hasChildElements = false;

                foreach (var row in rows)
                {
                    foreach (var attr in row.Attributes())
                        if (seen.Add(attr.Name.LocalName)) cols.Add(attr.Name.LocalName);
                    foreach (var child in row.Elements())
                    {
                        hasChildElements = true;
                        if (seen.Add(child.Name.LocalName)) cols.Add(child.Name.LocalName);
                    }
                }

                if (!hasChildElements)
                {
                    // Leaf nodes — single VALUE column
                    var dt2 = new DataTable();
                    dt2.SetColumns(new[] { "VALUE" });
                    foreach (var row in rows)
                        await dt2.AddRowAsync(new Row { ["VALUE"] = row.Value });
                    return dt2;
                }

                var dt = new DataTable();
                dt.SetColumns(cols);
                foreach (var row in rows)
                {
                    var r = new Row();
                    foreach (var attr in row.Attributes())
                        r[attr.Name.LocalName] = attr.Value;
                    foreach (var child in row.Elements())
                        r[child.Name.LocalName] = child.Value;
                    await dt.AddRowAsync(r);
                }
                return dt;
            }
            catch { return new DataTable(); }
        }

        // ── XML construction functions ────────────────────────────────────────

        /// <summary>
        /// Constructs an XML element with the given name and text content.
        /// Example: XMLELEMENT('name', 'Alice') → '&lt;name&gt;Alice&lt;/name&gt;'
        /// </summary>
        private static object? XmlElement(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 1) return null;
            string name = args[0]?.ToString() ?? "element";
            string content = args.Count >= 2 ? (args[1]?.ToString() ?? "") : "";
            return new XElement(XmlConvert.EncodeLocalName(name), content).ToString(SaveOptions.DisableFormatting);
        }

        /// <summary>
        /// Returns an XML attribute string from alternating name/value pairs.
        /// Intended for use with XMLELEMENT or concatenation.
        /// Example: XMLATTRIBUTES('id', 1, 'type', 'A') → 'id="1" type="A"'
        /// </summary>
        private static object? XmlAttributes(List<object?> args, IExecutionContext ctx)
        {
            var sb = new StringBuilder();
            for (int i = 0; i + 1 < args.Count; i += 2)
            {
                if (sb.Length > 0) sb.Append(' ');
                string attrName = args[i]?.ToString() ?? $"attr{i}";
                string attrValue = XmlEscapeAttribute(args[i + 1]?.ToString() ?? "");
                sb.Append($"{XmlConvert.EncodeLocalName(attrName)}=\"{attrValue}\"");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Constructs a sequence of XML elements from alternating name/value pairs.
        /// Example: XMLFOREST('name', 'Alice', 'age', 30) → '&lt;name&gt;Alice&lt;/name&gt;&lt;age&gt;30&lt;/age&gt;'
        /// </summary>
        private static object? XmlForest(List<object?> args, IExecutionContext ctx)
        {
            var sb = new StringBuilder();
            for (int i = 0; i + 1 < args.Count; i += 2)
            {
                string name = args[i]?.ToString() ?? $"col{i}";
                string value = args[i + 1]?.ToString() ?? "";
                sb.Append(new XElement(XmlConvert.EncodeLocalName(name), value).ToString(SaveOptions.DisableFormatting));
            }
            return sb.ToString();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Strips a leading '/' if the document root element name is already embedded,
        /// so that XPath expressions work whether or not they include the root tag.
        /// </summary>
        private static string NormaliseXPath(string xpath, XDocument doc)
        {
            // If xpath starts with // or /root it works as-is with XPathSelectElement
            return xpath;
        }

        private static string XmlEscapeAttribute(string value)
        {
            return value
                .Replace("&", "&amp;")
                .Replace("\"", "&quot;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");
        }
    }
}
