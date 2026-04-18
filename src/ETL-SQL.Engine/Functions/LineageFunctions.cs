using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Functions;
using ETL_SQL.Data;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Engine.Functions
{
    /// <summary>
    /// Provides built-in functions for interacting with data lineage and metadata tags.
    /// Includes GET_TAGS and GET_TAG_VALUE.
    /// </summary>
    public static class LineageFunctions
    {
        /// <summary>Registers lineage-related functions into the global function registry.</summary>
        public static void Register(IFunctionRegistry registry)
        {
            registry.RegisterWithHelp("GET_TAGS", GetTags, "GET_TAGS(target): Returns a list of tags associated with a table or column.");
            registry.RegisterWithHelp("GET_TAG_VALUE", GetTagValue, "GET_TAG_VALUE(target, tag_name): Returns the value of a specific tag.");
        }

        /// <summary>
        /// Retrieves a list of all tag names (metadata keys) for a given table or column.
        /// Usage: GET_TAGS(table_name [, column_name])
        /// Returns: LIST(varchar)
        /// </summary>
        private static Task<object?> GetTags(List<object?> args, IExecutionContext context)
        {
            if (args.Count < 1 || args[0] == null) return Task.FromResult<object?>(null);
            
            string table = args[0]?.ToString() ?? "";
            string? column = args.Count > 1 ? args[1]?.ToString() : null;

            var metadata = column != null 
                ? context.LineageTracker.GetColumnMetadata(table, column)
                : context.LineageTracker.GetLineage(table).FirstOrDefault()?.Metadata;

            if (metadata == null) return Task.FromResult<object?>(new List<string>());

            return Task.FromResult<object?>(metadata.Keys.ToList());
        }

        /// <summary>
        /// Retrieves the value of a specific metadata tag for a given table or column.
        /// Usage: GET_TAG_VALUE(table_name, column_name, tag_name)
        /// Returns: varchar
        /// </summary>
        private static Task<object?> GetTagValue(List<object?> args, IExecutionContext context)
        {
            if (args.Count < 3 || args[0] == null || args[1] == null || args[2] == null) return Task.FromResult<object?>(null);
            
            string table = args[0]?.ToString() ?? "";
            string column = args[1]?.ToString() ?? "";
            string tag = args[2]?.ToString() ?? "";

            var metadata = context.LineageTracker.GetColumnMetadata(table, column);
            
            if (metadata != null && metadata.TryGetValue(tag, out var val))
            {
                return Task.FromResult<object?>(val);
            }

            return Task.FromResult<object?>(null);
        }
    }
}
