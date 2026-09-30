using System;
using System.Collections.Generic;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;

namespace ETL_SQL.Reporting
{
    /// <summary>
    /// Turns a value a reader posted (always text) into what the variable it lands in holds.
    /// </summary>
    public static class ReportParameterBinding
    {
        /// <summary>
        /// A LIST variable receives a list, so <c>IN @var</c> matches element by element; the text is
        /// a JSON array or comma-separated items. Every other variable keeps the text as posted.
        /// </summary>
        public static object? Value(IExecutionContext context, string name, string posted) =>
            context.VarContext.VariableMetadata.TryGetValue(name, out var metadata)
            && string.Equals(metadata.DataType, "LIST", StringComparison.OrdinalIgnoreCase)
                ? TypeConverter.Cast(posted, "LIST")
                : posted;

        /// <summary>
        /// A page selection (click or Ctrl+click) arrives as a JSON array of the selected values, so a
        /// value containing a comma stays whole. A LIST receives every value; any other variable
        /// receives the value itself when one is selected, and cannot match several.
        /// </summary>
        public static object? Selection(IExecutionContext context, string name, string posted)
        {
            var value = Value(context, name, posted);
            if (value is not string text || !text.StartsWith('[')) return value;
            var items = (List<object?>)TypeConverter.Cast(text, "LIST")!;
            return items.Count == 1 && items[0] is string single ? single : text;
        }
    }
}
