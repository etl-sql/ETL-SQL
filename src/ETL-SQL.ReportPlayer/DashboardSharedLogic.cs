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
            IEnumerable<(string Name, string Value)> updates,
            bool isInteraction = false)
        {
            var logger = evaluator.Logger;
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
                    evaluator.DeclareVariable(varName, value, new VariableMetadata { IsInput = true });
                    manifest.Parameters[varName] = value;
                }
            }

            logger.Debug($"[DashboardSharedLogic] Refreshing visuals affected by: {string.Join(", ", affectedNames)} (Interaction: {isInteraction})");

            var builder = new ManifestBuilder(evaluator);
            int refreshCount = 0;

            foreach (var visualDef in evaluator.ReportContext.VisualDefinitions.Values)
            {
                // Visual is affected if it directly uses the variable.
                // For interactions (Highlight), we refresh all visuals that have an interaction mode enabled
                // to ensure cross-filtering/ghosting is applied correctly across the page.
                var action = visualDef.Options.FirstOrDefault(o => string.Equals(o.Key, "CROSS_VISUAL_ACTION", StringComparison.OrdinalIgnoreCase))?.Value;
                bool hasInteraction = action != null && !string.Equals(action, "NONE", StringComparison.OrdinalIgnoreCase);
                
                bool isAffected = (isInteraction && hasInteraction) || affectedNames.Any(n => DependsOnVariable(visualDef, n));
                
                if (isAffected)
                {
                    var existingVm = manifest.Visuals.FirstOrDefault(v => v.Name == visualDef.Name);
                    if (existingVm != null)
                    {
                        logger.Debug($"[DashboardSharedLogic] Refreshing visual: {visualDef.Name}");
                        
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
                        logger.Warning($"[DashboardSharedLogic] Found dependency but visual manifest entry missing: {visualDef.Name}");
                    }
                }
            }

            logger.Debug($"[DashboardSharedLogic] Refresh complete. {refreshCount} visuals updated.");
            return refreshCount;
        }

        private static async Task RefreshVisualWithInteractionAsync(ManifestBuilder builder, CreateVisualStatement visualDef, VisualManifest vm, Dictionary<string, string> interactionValues)
        {
            // We reuse the existing builder but we need it to be interaction-aware.
            // Since ManifestBuilder is a facade, we can use a temporary side-channel or 
            // a dedicated method if we added one. 
            // Let's add an overload to RefreshVisualAsync.
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
