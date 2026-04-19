using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core;

namespace ETL_SQL.ReportBuilder.Builders
{
    public class PageBuilder(IExecutionContext ctx, StyleBuilder styleBuilder)
    {
        public PageManifest Build(string name, CreatePageStatement pStmt)
        {
            var pm = new PageManifest
            {
                Name      = name,
                Structure = pStmt.Structure,
                SlotMap   = pStmt.SlotMap.ToDictionary(kv => kv.Key, kv => kv.Value)
            };

            // Map parameters and their types
            foreach (var param in pStmt.Parameters)
            {
                var val = ctx.GetVariable(param.Name)?.ToString() ?? param.DefaultValue;
                pm.Parameters[param.Name.TrimStart('@')] = val;
                
                if (param.DataType != null)
                {
                    pm.ParameterTypes ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    pm.ParameterTypes[param.Name.TrimStart('@')] = param.DataType;
                }
            }

            // Styles
            var resolvedStyles = styleBuilder.ResolveStyles(pStmt.StyleName, pStmt.Styles);
            if (resolvedStyles.Count > 0)
                pm.Styles = resolvedStyles;

            return pm;
        }
    }
}
