using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Core.Parser;
using ETL_SQL.ReportBuilder;
using ETL_SQL.Engine;

namespace ETL_SQL.ReportPlayer
{
    /// <summary>
    /// Shared logic for updating report parameters and refreshing affected visuals.
    /// This eliminates code duplication between SetParameter and SetParameters.
    /// </summary>
    public static class DashboardSharedLogic
    {
        public static async Task<int> RefreshAffectedVisualsAsync(
            Evaluator evaluator, 
            ReportManifest manifest, 
            IEnumerable<(string Name, string Value)> updates)
        {
            var logger = evaluator.Logger;
            var affectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, value) in updates)
            {
                var varName = name.StartsWith('@') ? name : '@' + name;
                evaluator.DeclareVariable(varName, value, new VariableMetadata { IsInput = true });
                affectedNames.Add(name.TrimStart('@'));
                manifest.Parameters[varName] = value;
            }

            logger.Debug($"[DashboardSharedLogic] Refreshing visuals affected by: {string.Join(", ", affectedNames)}");

            var builder = new ManifestBuilder(evaluator);
            int refreshCount = 0;

            foreach (var visualDef in evaluator.ReportContext.VisualDefinitions.Values)
            {
                if (affectedNames.Any(n => DependsOnVariable(visualDef, n)))
                {
                    var existingVm = manifest.Visuals.FirstOrDefault(v => v.Name == visualDef.Name);
                    if (existingVm != null)
                    {
                        logger.Debug($"[DashboardSharedLogic] Refreshing visual: {visualDef.Name}");
                        await builder.RefreshVisualAsync(visualDef, existingVm);
                        refreshCount++;
                    }
                    else
                    {
                        logger.Warning($"[DashboardSharedLogic] Found dependency but visual manifest entry missing: {visualDef.Name}");
                    }
                }
            }

            logger.Debug($"[DashboardSharedLogic] Refresh complete. {refreshCount} visuals updated.");
            return refreshCount;
        }

        public static bool DependsOnVariable(CreateVisualStatement visual, string variableName)
        {
            if (!variableName.StartsWith("@")) variableName = "@" + variableName;

            if (visual.Source.IsInlineSelect && visual.Source.InlineSelect != null)
            {
                var usedParams = ParameterScanner.Scan(visual.Source.InlineSelect);
                return usedParams.Contains(variableName);
            }

            return visual.Source.TempTableName?.Contains(variableName, StringComparison.OrdinalIgnoreCase) ?? false;
        }
    }
}
