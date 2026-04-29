using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;
using ETL_SQL.Common;

namespace ETL_SQL.Engine.Services
{
    /// <summary>
    /// Helper service for preparing query metadata, such as expanding SELECT * into explicit columns.
    /// </summary>
    public class QueryMetadataHelper(ILogger logger)
    {
        private readonly ILogger _logger = logger;

        public async Task<(List<SelectColumn> Columns, List<string> Names)> ExpandColumns(SelectStatement stmt, List<string> sourceColumns)
        {
            var final = new List<SelectColumn>();
            foreach (var col in stmt.Columns)
            {
                if (col.Expression is IdentifierExpression id && (id.Name == "*" || id.Name.EndsWith(".*")))
                {
                    foreach (var sc in sourceColumns) final.Add(new SelectColumn(new IdentifierExpression(sc), sc));
                }
                else final.Add(col);
            }
            var names = new List<string>();
            for (int i = 0; i < final.Count; i++)
            {
                var col = final[i];
                names.Add(col.Alias ?? (col.Expression is IdentifierExpression id ? id.Name.Split('.').Last() : $"Expr{i}"));
            }
            return (final, names);
        }
    }
}
