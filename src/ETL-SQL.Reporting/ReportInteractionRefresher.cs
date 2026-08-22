using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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
            if (!isInteraction)
                return await RefreshAtomicallyAsync(context, manifest, updates);

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

        private static async Task<int> RefreshAtomicallyAsync(
            IExecutionContext context,
            ReportManifest manifest,
            IEnumerable<(string Name, string Value)> updates)
        {
            var definitions = context.ReportContext.VisualDefinitions.Values.ToList();
            var graph = CascadingFilterGraphCompiler.Compile(definitions);
            var parameters = manifest.Parameters.ToDictionary(
                pair => CascadingFilterGraphCompiler.Normalize(pair.Key), pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);
            var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, value) in updates)
            {
                var normalized = CascadingFilterGraphCompiler.Normalize(name);
                parameters[normalized] = value;
                changed.Add(normalized);
            }

            var variableSnapshot = context.VarContext.Variables.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
            var stagedVisuals = new Dictionary<string, VisualManifest>(StringComparer.OrdinalIgnoreCase);
            var refreshed = new List<string>();
            var builder = new ManifestBuilder(context);

            try
            {
                ApplyParameters(context, parameters, changed);

                foreach (var node in graph.OrderedNodes)
                {
                    if (!changed.Contains(node.ProducedParameter) && !node.ParentParameters.Any(changed.Contains)) continue;
                    var existing = manifest.Visuals.FirstOrDefault(v => v.Name.Equals(node.Visual.Name, StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidOperationException($"Cascade visual manifest entry '{node.Visual.Name}' is missing.");

                    VisualManifest staged;
                    if (node.Visual.Cascade!.Mode == CascadeMode.Local)
                    {
                        staged = Clone(existing);
                        if (staged.Cascade?.SourceRows == null)
                            throw new InvalidOperationException($"LOCAL cascading visual '{node.Visual.Name}' has no retained option vector.");
                        staged.Rows = CascadingFilterState.FilterRows(staged.Cascade, parameters);
                        staged.ChartConfig = null;
                        staged.NativeSvg = new SvgChartRenderer().Render(staged);
                    }
                    else
                    {
                        staged = await builder.BuildVisualSnapshotAsync(node.Visual);
                        if (staged.Error != null) throw new InvalidOperationException(staged.Error);
                    }

                    var current = parameters.TryGetValue(node.ProducedParameter, out var selection) ? selection : null;
                    var reconciled = CascadingFilterState.Reconcile(staged.Cascade!, staged, current);
                    if (!string.Equals(current ?? string.Empty, reconciled, StringComparison.Ordinal))
                    {
                        parameters[node.ProducedParameter] = reconciled;
                        changed.Add(node.ProducedParameter);
                        ApplyParameter(context, node.ProducedParameter, reconciled);
                    }
                    stagedVisuals[node.Visual.Name] = staged;
                    refreshed.Add(node.Visual.Name);
                }

                foreach (var definition in definitions)
                {
                    if (stagedVisuals.ContainsKey(definition.Name)) continue;
                    if (!changed.Any(parameter => DependsOnVariable(definition, parameter))) continue;
                    var staged = await builder.BuildVisualSnapshotAsync(definition);
                    if (staged.Error != null) throw new InvalidOperationException(staged.Error);
                    stagedVisuals[definition.Name] = staged;
                    refreshed.Add(definition.Name);
                }

                foreach (var pair in stagedVisuals)
                {
                    var index = manifest.Visuals.FindIndex(v => v.Name.Equals(pair.Key, StringComparison.OrdinalIgnoreCase));
                    if (index >= 0) manifest.Visuals[index] = pair.Value;
                }
                foreach (var parameter in parameters) manifest.Parameters[parameter.Key] = parameter.Value;
                manifest.CascadeGraph = graph.OrderedNodes.Count > 0 ? graph.ToManifest() : null;
                manifest.CascadeTransaction = new CascadeTransactionManifest
                {
                    CommittedAt = DateTime.UtcNow,
                    ChangedParameters = changed.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList(),
                    RefreshedVisuals = refreshed
                };
                return refreshed.Count;
            }
            catch
            {
                RestoreVariables(context, variableSnapshot);
                throw;
            }
        }

        private static void ApplyParameters(
            IExecutionContext context,
            IReadOnlyDictionary<string, string> parameters,
            IEnumerable<string> changed)
        {
            foreach (var parameter in changed)
                ApplyParameter(context, parameter, parameters[parameter]);
        }

        private static void ApplyParameter(IExecutionContext context, string name, string value)
        {
            if (context.VarContext.ContainsVariable(name)) context.VarContext.SetVariable(name, value);
            else context.VarContext.DeclareVariable(name, value, new VariableMetadata { IsInput = true });
        }

        private static void RestoreVariables(IExecutionContext context, IReadOnlyDictionary<string, object?> snapshot)
        {
            foreach (var name in context.VarContext.Variables.Keys.Where(name => !snapshot.ContainsKey(name)).ToList())
            {
                context.VarContext.Variables.Remove(name);
                context.VarContext.VariableMetadata.Remove(name);
            }
            foreach (var pair in snapshot)
            {
                if (context.VarContext.ContainsVariable(pair.Key)) context.VarContext.SetVariable(pair.Key, pair.Value);
                else context.VarContext.DeclareVariable(pair.Key, pair.Value);
            }
        }

        private static VisualManifest Clone(VisualManifest visual) =>
            JsonSerializer.Deserialize<VisualManifest>(JsonSerializer.Serialize(visual))
            ?? throw new InvalidOperationException($"Unable to stage visual '{visual.Name}'.");

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
