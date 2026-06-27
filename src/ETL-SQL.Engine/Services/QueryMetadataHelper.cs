using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Services;
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
            if (col.Expression is StarExpression star)
            {
                var excl = new HashSet<string>(star.Exclude, StringComparer.OrdinalIgnoreCase);
                var replaceMap = star.Replace.ToDictionary(r => r.Column, r => r.Value, StringComparer.OrdinalIgnoreCase);
                var renameMap = star.Rename.ToDictionary(r => r.Column, r => r.NewName, StringComparer.OrdinalIgnoreCase);
                foreach (var sc in sourceColumns)
                {
                    var baseName = sc.Contains('.') ? sc.Split('.').Last() : sc;
                    if (excl.Contains(sc) || excl.Contains(baseName)) continue;
                    Expression colExpr = replaceMap.TryGetValue(baseName, out var rv) ? rv
                                       : replaceMap.TryGetValue(sc, out var rv2) ? rv2
                                       : new IdentifierExpression(sc);
                    string alias = renameMap.TryGetValue(baseName, out var nn) ? nn
                                 : renameMap.TryGetValue(sc, out var nn2) ? nn2
                                 : sc;
                    final.Add(new SelectColumn(colExpr, alias));
                }
            }
            else if (col.Expression is IdentifierExpression id && (id.Name == "*" || id.Name.EndsWith(".*")))
            {
                foreach (var sc in sourceColumns) final.Add(new SelectColumn(new IdentifierExpression(sc), sc));
            }
            else final.Add(col);
        }


        var names = new List<string>();
        for (int i = 0; i < final.Count; i++)
        {
            var col = final[i];
            names.Add(col.Alias ?? (col.Expression is IdentifierExpression id ? id.Name.Split('.').Last() : $"column{i + 1}"));
        }
        return (final, names);
    }
}
