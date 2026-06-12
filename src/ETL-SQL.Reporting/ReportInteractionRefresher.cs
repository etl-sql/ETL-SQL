using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;

namespace ETL_SQL.Reporting
{
    /// <summary>
    /// Shared logic for updating report parameters and refreshing affected visuals.
    /// </summary>
    public static class ReportInteractionRefresher
    {
        public static async Task<int> RefreshAffectedVisualsAsync(
            IExecutionContext context,
            ReportManifest manifest,
            IEnumerable<(string Name, string Value)> updates,
            bool isInteraction = false)
        {
            var logger = context.Logger;
            var affectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var interactionValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (name, value) in updates)
            {
                var varName = name.StartsWith('@') ? name : '@' + name;
                affectedNames.Add(name.TrimStart('@'));

                if (isInteraction)
                {
                    interactionValues[varName] = value;
                }
                else
                {
                    // Global Update: Apply permanently to Evaluator and Manifest
                    context.VarContext.DeclareVariable(varName, value, new VariableMetadata { IsInput = true });
                    manifest.Parameters[varName] = value;
                }
            }

            logger.Debug($"[ReportInteractionRefresher] Refreshing visuals affected by: {string.Join(", ", affectedNames)} (Interaction: {isInteraction})");

            var builder = new ManifestBuilder(context);
            int refreshCount = 0;

            foreach (var visualDef in context.ReportContext.VisualDefinitions.Values)
            {
                // Visual is affected if it directly uses the variable.
                // For interactions (Highlight), we refresh all visuals that have an interaction mode enabled
                // to ensure cross-filtering/ghosting is applied correctly across the page.
                var action = visualDef.Interactions.FirstOrDefault(o => string.Equals(o.Key, "ON_SELECT", StringComparison.OrdinalIgnoreCase))?.Value;
                bool hasInteraction = action != null && !string.Equals(action, "NONE", StringComparison.OrdinalIgnoreCase);

                bool isAffected = (isInteraction && hasInteraction) || affectedNames.Any(n => DependsOnVariable(visualDef, n));

                if (isAffected)
                {
                    var existingVm = manifest.Visuals.FirstOrDefault(v => v.Name == visualDef.Name);
                    if (existingVm != null)
                    {
                        logger.Debug($"[ReportInteractionRefresher] Refreshing visual: {visualDef.Name}");

                        // If it's an interaction, we need to pass the selection values down to the visual builder
                        // for the double-fetch logic.
                        if (isInteraction)
                        {
                            await RefreshVisualWithInteractionAsync(builder, visualDef, existingVm, interactionValues);
                        }
                        else
                        {
                            await builder.RefreshVisualAsync(visualDef, existingVm);
                        }
                        refreshCount++;
                    }
                    else
                    {
                        logger.Warning($"[ReportInteractionRefresher] Found dependency but visual manifest entry missing: {visualDef.Name}");
                    }
                }
            }

            // After a non-interaction refresh (slicer / param change), clear any stale HighlightRows
            // left from a prior cross-filter click on visuals that weren't in the affected set.
            // This prevents ghost overlays persisting after the user changes a slicer.
            if (!isInteraction)
            {
                foreach (var vm in manifest.Visuals)
                {
                    if (vm.HighlightRows != null)
                    {
                        builder.ClearHighlightRows(vm);
                        refreshCount++;
                        logger.Debug($"[ReportInteractionRefresher] Cleared stale HighlightRows on: {vm.Name}");
                    }
                }
            }

            logger.Debug($"[ReportInteractionRefresher] Refresh complete. {refreshCount} visuals updated.");
            return refreshCount;
        }

        private static async Task RefreshVisualWithInteractionAsync(ManifestBuilder builder, CreateVisualStatement visualDef, VisualManifest vm, Dictionary<string, string> interactionValues)
        {
            await builder.RefreshVisualAsync(visualDef, vm, interactionValues);
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
