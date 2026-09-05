using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Security;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Reporting;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.ReportHosting
{
    /// <summary>
    /// Singleton service that owns the script path, parameter state, and
    /// the most-recently-built <see cref="ReportManifest"/>.
    ///
    /// On first access (or after <see cref="RebuildAsync"/>) the service:
    ///   1. Evaluates the .rptsql script in a fresh engine context
    ///   2. Calls <see cref="ManifestBuilder.BuildAsync"/> to snapshot all visual data
    ///   3. Caches the manifest so subsequent HTTP requests can return it cheaply
    ///
    /// Parameter changes from slicer interactions re-evaluate affected visuals
    /// and fall back to <see cref="RebuildAsync"/> when selective refresh cannot apply.
    /// </summary>
    public class DashboardService : IAsyncDisposable
    {
        private readonly string _scriptPath;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly TimeSpan _executionTimeout;
        private readonly string? _datasetCallerContext;
        private readonly int? _datasetOwningReportId;
        private readonly string? _datasetAtRestKey;
        private readonly string? _keyScope;
        private readonly string _storageRunId = $"report-{Guid.NewGuid():N}";
        private readonly SemaphoreSlim _lock = new(1, 1);
        private readonly NativeChartLayoutProfile _layoutProfile;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<NativeChartLayoutCacheKey, NativeChartLayoutCacheEntry> _layoutCache = new();

        private IServiceScope? _currentScope;
        private ReportManifest? _manifest;
        private Evaluator? _evaluator;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _parameters = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _runPages = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, VisualDrillState> _drillStates = new(StringComparer.OrdinalIgnoreCase);

        public string ScriptDirectory => Path.GetDirectoryName(_scriptPath) ?? Directory.GetCurrentDirectory();

        public DashboardService(string scriptPath, IServiceScopeFactory scopeFactory, TimeSpan? executionTimeout = null, string? datasetCallerContext = null, int? datasetOwningReportId = null, string? datasetAtRestKey = null, ETL_SQL.Core.Governance.ExecutionIdentity? executionIdentity = null, string? keyScope = null, NativeChartLayoutProfile? layoutProfile = null)
        {
            _scriptPath = scriptPath ?? throw new ArgumentNullException(nameof(scriptPath));
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _executionTimeout = executionTimeout ?? TimeSpan.FromSeconds(30);
            _datasetCallerContext = datasetCallerContext;
            _datasetOwningReportId = datasetOwningReportId;
            _datasetAtRestKey = datasetAtRestKey;
            _executionIdentity = executionIdentity;
            _layoutProfile = layoutProfile ?? NativeChartLayoutProfile.Default;
            _keyScope = string.IsNullOrWhiteSpace(keyScope)
                ? null
                : ETL_SQL.Core.Multitenancy.TenantId.FromTrustedSource(keyScope).Value;
        }

        private readonly ETL_SQL.Core.Governance.ExecutionIdentity? _executionIdentity;

        public async ValueTask DisposeAsync()
        {
            if (_currentScope is IAsyncDisposable asyncScope)
                await asyncScope.DisposeAsync();
            else
                _currentScope?.Dispose();

            _currentScope = null;

            if (_evaluator != null)
            {
                await _evaluator.DisposeAsync();
                _evaluator = null;
            }

            _lock.Dispose();
        }

        public void Dispose()
        {
            // Fallback for sync disposal (though we prefer DisposeAsync)
            _currentScope?.Dispose();
            _lock.Dispose();
        }

        /// <summary>Returns the cached manifest, building it on first call.</summary>
        public async Task<ReportManifest> GetManifestAsync()
        {
            if (_manifest != null)
            {
                StampDefaultLayouts(_manifest);
                return _manifest;
            }
            return await RebuildAsync();
        }

        /// <summary>
        /// Re-resolves one native visual for a bounded layout tier without querying its data source.
        /// The returned manifest is a delivery clone; the session's canonical manifest stays at its
        /// standard layout, and its parameter, selection, highlight, and drill state are preserved.
        /// </summary>
        public async Task<ReportManifest?> ResolveVisualLayoutAsync(string visualName, NativeChartLayoutTier tier)
        {
            if (string.IsNullOrWhiteSpace(visualName)) return null;
            if (_manifest == null) await GetManifestAsync();
            if (_manifest == null) return null;

            await _lock.WaitAsync();
            try
            {
                var source = _manifest.Visuals.FirstOrDefault(visual =>
                    visual.Name.Equals(visualName.Trim(), StringComparison.OrdinalIgnoreCase));
                if (source == null || !NativeChartLayoutResolver.Supports(source)) return null;

                var key = new NativeChartLayoutCacheKey(_scriptPath, source.Name, tier, _manifest.BuiltAt.Ticks);
                if (!_layoutCache.TryGetValue(key, out var entry))
                {
                    var working = CloneManifest(_manifest);
                    var visual = working.Visuals.First(item => item.Name.Equals(source.Name, StringComparison.OrdinalIgnoreCase));
                    visual.ResolvedMapFile = source.ResolvedMapFile;
                    NativeChartLayoutResolver.Resolve(visual, tier, _layoutProfile);
                    entry = new NativeChartLayoutCacheEntry(
                        visual.NativeSvg,
                        visual.PlotPlan,
                        visual.Interaction,
                        visual.Layout!);
                    _layoutCache[key] = entry;
                    TrimLayoutCache(_manifest.Visuals.Count);
                }

                var result = CloneManifest(_manifest);
                var target = result.Visuals.First(item => item.Name.Equals(source.Name, StringComparison.OrdinalIgnoreCase));
                target.NativeSvg = entry.NativeSvg;
                target.PlotPlan = entry.PlotPlan;
                target.Interaction = entry.Interaction;
                target.Layout = entry.Layout;
                return result;
            }
            finally { _lock.Release(); }
        }

        /// <summary>Current parameter values (set by slicer interactions).</summary>
        public IReadOnlyDictionary<string, string> Parameters => _parameters;

        public ILineageTracker? CurrentLineageTracker => _evaluator?.LineageTracker;

        internal int NativeLayoutCacheEntryCount => _layoutCache.Count;

        private bool IsPaginatedPage(string pageName) =>
            _manifest?.Pages.Any(p =>
                p.Name.Equals(pageName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.Mode, "PAGINATED", StringComparison.OrdinalIgnoreCase)) == true;

        private bool HasPaginatedPages() =>
            _manifest?.Pages.Any(p => string.Equals(p.Mode, "PAGINATED", StringComparison.OrdinalIgnoreCase)) == true;

        private void MarkAllPaginatedPagesRun()
        {
            if (_manifest?.Pages == null) return;
            foreach (var page in _manifest.Pages.Where(p => string.Equals(p.Mode, "PAGINATED", StringComparison.OrdinalIgnoreCase)))
                _runPages.Add(page.Name);
        }

        /// <summary>
        /// Updates multiple parameters atomically and re-evaluates only the affected visuals.
        /// </summary>
        public async Task<ReportManifest> SetParametersAsync(IEnumerable<(string Name, string Value)> updates, bool isInteraction = false, string? pageName = null, string? sourceVisual = null)
        {
            var updateList = updates.ToList();
            // Warm the session on first access so interaction calls have a live evaluator to work with.
            if (_manifest == null || _evaluator == null)
                await GetManifestAsync();

            // Only update global context if NOT an interaction
            if (!isInteraction)
            {
                if (!string.IsNullOrWhiteSpace(pageName) && IsPaginatedPage(pageName))
                {
                    foreach (var (name, value) in updateList) _parameters[name] = value;
                    _runPages.Add(pageName);
                    return await RebuildAsync();
                }

                if (string.IsNullOrWhiteSpace(pageName) && HasPaginatedPages())
                {
                    foreach (var (name, value) in updateList) _parameters[name] = value;
                    MarkAllPaginatedPagesRun();
                    return await RebuildAsync();
                }
            }

            if (_evaluator != null && _manifest != null)
            {
                await _lock.WaitAsync();
                try
                {
                    if (!isInteraction && updateList.Count == 0 && _manifest.IsInteraction)
                    {
                        var resetBuilder = new ManifestBuilder(_evaluator);
                        foreach (var visualDef in _evaluator.ReportContext.VisualDefinitions.Values)
                        {
                            var visual = _manifest.Visuals.FirstOrDefault(item =>
                                item.Name.Equals(visualDef.Name, StringComparison.OrdinalIgnoreCase));
                            if (visual == null) continue;
                            _drillStates.TryGetValue(visual.Name, out var drillState);
                            await resetBuilder.RefreshVisualAsync(visualDef, visual, drillState: drillState);
                        }
                        _manifest.BuiltAt = DateTime.UtcNow;
                        _manifest.IsInteraction = false;
                        return _manifest;
                    }

                    var refreshManifest = isInteraction ? _manifest : CloneManifest(_manifest);
                    int refreshCount = await ReportInteractionRefresher.RefreshAffectedVisualsAsync(_evaluator, refreshManifest, updateList, isInteraction, sourceVisual);
                    if (!isInteraction)
                    {
                        // One reference swap publishes the complete parameter/visual state.
                        _manifest = refreshManifest;
                        var committedNames = _manifest.CascadeTransaction?.ChangedParameters
                            ?? updateList.Select(update => update.Name).ToList();
                        foreach (var (name, _) in updateList)
                        {
                            var normalized = name.StartsWith('@') ? name : '@' + name;
                            if (_manifest.Parameters.TryGetValue(normalized, out var committedValue))
                                _parameters[name] = committedValue;
                        }
                        foreach (var name in committedNames.Where(committed =>
                                     !updateList.Any(update => string.Equals(
                                         update.Name.TrimStart('@'), committed.TrimStart('@'),
                                         StringComparison.OrdinalIgnoreCase))))
                        {
                            var normalized = name.StartsWith('@') ? name : '@' + name;
                            if (_manifest.Parameters.TryGetValue(normalized, out var committedValue))
                                _parameters[name.TrimStart('@')] = committedValue;
                        }
                        foreach (var name in committedNames)
                        {
                            var varName = name.StartsWith('@') ? name : '@' + name;
                            if (_manifest.Parameters.TryGetValue(varName, out var value))
                                _evaluator.ReportContext.BaselineParameters[varName] = value;
                        }
                        _manifest.BuiltAt = DateTime.UtcNow;
                        _manifest.IsInteraction = false;
                        return _manifest;
                    }
                    if (refreshCount > 0)
                    {
                        _manifest.BuiltAt = DateTime.UtcNow;
                        _manifest.IsInteraction = true;
                        return _manifest;
                    }
                }
                finally { _lock.Release(); }
            }

            var result = await RebuildAsync();
            result.IsInteraction = isInteraction;
            return result;
        }

        /// <summary>
        /// Updates one parameter and re-evaluates only the affected visuals.
        /// </summary>
        public async Task<ReportManifest> SetParameterAsync(string name, string value, bool isInteraction = false, string? pageName = null)
            => await SetParametersAsync(new[] { (name, value) }, isInteraction, pageName);

        /// <summary>
        /// Applies an author bookmark by name as one server-side transaction: resolve the envelope,
        /// validate/reconcile every reference and typed value against the current manifest, stage the
        /// parameter reconciliation and affected visual refreshes through the cascading-parameter engine,
        /// then publish one manifest carrying the resolved <see cref="ReportManifest.AppliedState"/>. On any
        /// failure nothing is applied (the live manifest is untouched) and the returned manifest reports
        /// the error. The active page and presentation state are carried on the published manifest for the
        /// client to apply as one deterministic swap — never as a second request.
        /// </summary>
        public Task<ReportManifest> ApplyBookmarkAsync(string bookmarkName)
            => ApplyResolvedStateAsync(bookmarkName, null, null);

        /// <summary>
        /// Applies a resolved-state envelope (a Portal saved view) as one server-side transaction, with the
        /// same atomic contract as <see cref="ApplyBookmarkAsync"/>. <paramref name="currentScriptHash"/>
        /// enables report-revision drift warnings.
        /// </summary>
        public Task<ReportManifest> ApplySavedViewAsync(ETL_SQL.Core.Reporting.ResolvedReportState state, string? currentScriptHash = null)
            => ApplyResolvedStateAsync(null, state, currentScriptHash);

        private async Task<ReportManifest> ApplyResolvedStateAsync(
            string? bookmarkName,
            ETL_SQL.Core.Reporting.ResolvedReportState? providedState,
            string? currentScriptHash)
        {
            if (_manifest == null || _evaluator == null)
                await GetManifestAsync();
            if (_manifest == null || _evaluator == null)
                throw new InvalidOperationException("Report manifest is not available.");

            await _lock.WaitAsync();
            try
            {
                // Resolve the requested envelope.
                ETL_SQL.Core.Reporting.ResolvedReportState? requested = providedState;
                if (requested == null && bookmarkName != null)
                {
                    requested = BookmarkApplicationService.ResolveAuthorBookmark(_manifest, bookmarkName);
                    if (requested == null)
                    {
                        var notFound = CloneManifest(_manifest);
                        notFound.AppliedState = null;
                        notFound.StateWarnings = new List<string> { $"Bookmark '{bookmarkName}' was not found." };
                        notFound.Error = $"Bookmark '{bookmarkName}' was not found.";
                        return notFound;
                    }
                }
                requested ??= new ETL_SQL.Core.Reporting.ResolvedReportState();

                // Validate/reconcile against the current manifest (unknown refs dropped with warnings).
                var reconciliation = BookmarkApplicationService.Reconcile(_manifest, requested, currentScriptHash);
                // Author bookmarks are source-controlled declarations and must be wholly valid. Saved
                // views are reader-owned persisted state and deliberately tolerate revision drift.
                if (providedState == null && reconciliation.HasWarnings)
                {
                    var invalid = CloneManifest(_manifest);
                    invalid.AppliedState = null;
                    invalid.StateWarnings = reconciliation.Warnings;
                    invalid.Error = $"Bookmark '{bookmarkName}' is invalid and was not applied.";
                    return invalid;
                }
                var resolved = reconciliation.State;

                // Stage on a clone so a failure leaves the live manifest untouched (no partial apply).
                var staged = CloneManifest(_manifest);
                var updates = resolved.Parameters
                    .Select(p => (p.Key, p.Value.ToCanonicalString()))
                    .ToList();

                try
                {
                    if (updates.Count > 0)
                        await ReportInteractionRefresher.RefreshAffectedVisualsAsync(_evaluator, staged, updates, isInteraction: false);
                }
                catch (Exception ex)
                {
                    // The cascade refresh threw — roll back (variables restored by the refresher) and
                    // apply nothing. Report the failure on an unpublished copy of the live manifest.
                    _evaluator.Logger.Warning($"[Bookmark] Application failed and was rolled back: {ex.Message}");
                    var failed = CloneManifest(_manifest);
                    failed.AppliedState = null;
                    failed.StateWarnings = new List<string> { "The bookmark could not be applied and no changes were made." };
                    failed.Error = ex.Message;
                    return failed;
                }

                // Success: attach the resolved presentation state and publish with one reference swap.
                staged.AppliedState = resolved;
                staged.StateWarnings = reconciliation.Warnings.Count > 0 ? reconciliation.Warnings : null;
                staged.BuiltAt = DateTime.UtcNow;
                staged.IsInteraction = false;
                _manifest = staged;

                // Mirror committed parameter values into the host's parameter cache and baseline.
                foreach (var (name, _) in updates)
                {
                    var normalized = name.StartsWith('@') ? name : '@' + name;
                    if (_manifest.Parameters.TryGetValue(normalized, out var committed))
                    {
                        _parameters[name.TrimStart('@')] = committed;
                        _evaluator.ReportContext.BaselineParameters[normalized] = committed;
                    }
                }

                return _manifest;
            }
            finally { _lock.Release(); }
        }

        private static ReportManifest CloneManifest(ReportManifest manifest) =>
            JsonSerializer.Deserialize<ReportManifest>(JsonSerializer.Serialize(manifest))
            ?? throw new InvalidOperationException("Unable to stage the report manifest.");

        public async Task<(string Message, bool Refresh)> RunScriptAsync(string scriptPath, Dictionary<string, string> parameters)
        {
            await _lock.WaitAsync();
            try
            {
                if (!SafePath.TryResolveWithinRoot(ScriptDirectory, scriptPath, out var fullPath))
                    return ($"Script path is outside the report directory: {scriptPath}", false);

                if (!File.Exists(fullPath))
                    return ($"Script file not found: {scriptPath}", false);

                var source = await File.ReadAllTextAsync(fullPath);

                // We use the existing evaluator if it exists to share context (temp tables, etc.)
                bool ownsEvaluator = false;
                var evaluator = _evaluator;
                IServiceScope? tempScope = null;

                if (evaluator == null)
                {
                    ownsEvaluator = true;
                    tempScope = _scopeFactory.CreateScope();
                    evaluator = tempScope.ServiceProvider.GetRequiredService<Evaluator>();
                    evaluator.ExecutionIdentity = _executionIdentity;
                }

                try
                {
                    // Set action-specific parameters
                    foreach (var (pName, pValue) in parameters)
                    {
                        var varName = pName.StartsWith('@') ? pName : '@' + pName;
                        if (!evaluator.ContainsVariable(varName))
                            evaluator.DeclareVariable(varName, pValue, new VariableMetadata { IsInput = true });
                        else
                            evaluator.SetVariable(varName, pValue);
                    }

                    var lexer = new Lexer(source);
                    var tokens = lexer.Tokenize();
                    var parser = new Parser(tokens, source);
                    var script = parser.Parse();

                    using var timeoutCts = new CancellationTokenSource(_executionTimeout);
                    await evaluator.Evaluate(script, timeoutCts.Token);

                    // Heuristic: refresh if script looks like it modified data
                    bool needsRefresh = source.Contains("INSERT", StringComparison.OrdinalIgnoreCase) ||
                                       source.Contains("UPDATE", StringComparison.OrdinalIgnoreCase) ||
                                       source.Contains("DELETE", StringComparison.OrdinalIgnoreCase) ||
                                       source.Contains("MERGE", StringComparison.OrdinalIgnoreCase);

                    return ("Script executed successfully.", needsRefresh);
                }
                catch (Exception ex)
                {
                    return ($"Script execution failed: {ex.Message}", false);
                }
                finally
                {
                    if (ownsEvaluator)
                    {
                        await evaluator.DisposeAsync();
                        tempScope?.Dispose();
                    }
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<ReportManifest?> RefreshVisualsAsync(IEnumerable<string> visualNames)
        {
            if (_manifest == null || _evaluator == null)
                await GetManifestAsync();
            if (_manifest == null || _evaluator == null) return null;

            var targets = visualNames
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (targets.Count == 0) return null;

            await _lock.WaitAsync();
            try
            {
                var builder = new ManifestBuilder(_evaluator);
                foreach (var target in targets)
                {
                    var vm = _manifest.Visuals.FirstOrDefault(v => v.Name.Equals(target, StringComparison.OrdinalIgnoreCase));
                    if (vm == null) return null;
                    if (!_evaluator.ReportContext.VisualDefinitions.TryGetValue(vm.Name, out var vStmt))
                        return null;

                    _drillStates.TryGetValue(vm.Name, out var drillState);
                    await builder.RefreshVisualAsync(vStmt, vm, null, drillState);
                }

                _manifest.BuiltAt = DateTime.UtcNow;
                _manifest.IsInteraction = true;
                return _manifest;
            }
            finally { _lock.Release(); }
        }

        /// <summary>Full rebuild: re-evaluate the script and re-snapshot all visuals.</summary>
        public async Task<ReportManifest> RebuildAsync()
        {
            await _lock.WaitAsync();
            try
            {
                _drillStates.Clear();
                var source = await File.ReadAllTextAsync(_scriptPath);

                var lexer = new Lexer(source);
                var tokens = lexer.Tokenize();
                var parser = new Parser(tokens, source);
                var script = parser.Parse();

                if (_currentScope != null)
                {
                    if (_currentScope is IAsyncDisposable ad)
                        await ad.DisposeAsync();
                    else
                        _currentScope.Dispose();

                    _currentScope = null;
                }

                if (_evaluator != null)
                {
                    await _evaluator.DisposeAsync();
                    _evaluator = null;
                }

                _currentScope = _scopeFactory.CreateScope();
                var evaluator = _currentScope.ServiceProvider.GetRequiredService<Evaluator>();
                evaluator.ExecutionIdentity = _executionIdentity;
                var portalConfig = _currentScope.ServiceProvider.GetService<ETL_SQL.Portal.PortalConfig>();
                var persistedTenant = portalConfig?.SharedTenancy.Enabled == true
                    && !string.IsNullOrWhiteSpace(_keyScope)
                        ? ETL_SQL.Core.Multitenancy.TenantContext.FromVerifiedCredential(_keyScope)
                        : null;
                var storageAuthority = _currentScope.ServiceProvider
                    .GetService<ETL_SQL.Core.Multitenancy.ITenantStorageHostAuthorityProvider>()
                    ?.GetAuthority(persistedTenant);
                if (storageAuthority is not null)
                {
                    evaluator.StorageCapability = storageAuthority.CreateRunCapability(_storageRunId);
                    evaluator.SessionRoot = storageAuthority.CheckpointRoot;
                }
                var registry = _currentScope.ServiceProvider.GetService<IDatasetRegistry>();
                if (registry != null)
                {
                    evaluator.DatasetRegistry = registry;
                    evaluator.DatasetCallerContext = _datasetCallerContext;
                    evaluator.DatasetOwningReportId = _datasetOwningReportId;
                    evaluator.DatasetAtRestKey = _datasetAtRestKey;
                    evaluator.DatasetKeyMaterialProvider =
                        _currentScope.ServiceProvider.GetService<IKeyMaterialProvider>();
                    evaluator.DatasetKeyScope = _keyScope
                        ?? (string.IsNullOrWhiteSpace(portalConfig?.TenantId)
                            ? "portal-host"
                            : portalConfig.TenantId);
                }

                evaluator.CheckpointKeyScope = _keyScope;

                evaluator.RedirectOutput = true;

                // Security Hardening (CR-S1): Inject current parameter values directly into the scope
                // instead of concatenating source text. This prevents script injection.
                foreach (var (name, value) in _parameters)
                {
                    var varName = name.StartsWith('@') ? name : '@' + name;
                    evaluator.DeclareVariable(varName, value, new VariableMetadata { IsInput = true });
                    evaluator.ReportContext.BaselineParameters[varName] = value;
                }

                using var cts = new CancellationTokenSource(_executionTimeout);

                try
                {
                    await evaluator.Evaluate(script, cts.Token);
                }
                catch (Exception ex)
                {
                    // Build a "failure" manifest that still contains the logs and execution tree
                    var failBuilder = new ManifestBuilder(evaluator);
                    _evaluator = evaluator;
                    _manifest = await failBuilder.BuildAsync(_scriptPath);
                    _manifest.Error = ex.Message;
                    StampDefaultLayouts(_manifest);
                    return _manifest;
                }

                var builder = new ManifestBuilder(evaluator);
                _evaluator = evaluator;
                _manifest = await builder.BuildAsync(_scriptPath, runPages: _runPages);
                StampDefaultLayouts(_manifest);

                return _manifest;
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Drills into the next hierarchy level for a DRILL_IN visual.
        /// Filters rows to those matching <paramref name="clickedValue"/> at the current level,
        /// then re-renders the visual grouped by the next level column.
        /// </summary>
        public async Task<ReportManifest?> DrillInAsync(string visualName, string clickedValue)
        {
            if (_manifest == null || _evaluator == null)
                await GetManifestAsync();
            if (_manifest == null || _evaluator == null) return null;

            var vm = _manifest.Visuals.FirstOrDefault(v => v.Name.Equals(visualName, StringComparison.OrdinalIgnoreCase));
            var action = vm?.Actions?.FirstOrDefault(a => a.Type == "DRILL_IN");
            if (vm == null || action?.Hierarchy == null) return null;

            var hierarchy = action.Hierarchy;
            _drillStates.TryGetValue(visualName, out var existing);
            var path = existing?.Path.ToList() ?? new List<(string, string)>();
            var curLevel = hierarchy[path.Count];
            path.Add((curLevel, clickedValue));

            if (path.Count >= hierarchy.Length) return _manifest; // already at leaf

            _drillStates[visualName] = new VisualDrillState(hierarchy, path);

            await _lock.WaitAsync();
            try
            {
                if (!_evaluator.ReportContext.VisualDefinitions.TryGetValue(visualName, out var vStmt))
                    return _manifest;
                var builder = new ManifestBuilder(_evaluator);
                await builder.RefreshVisualAsync(vStmt, vm, null, _drillStates[visualName]);
                _manifest.BuiltAt = DateTime.UtcNow;
            }
            finally { _lock.Release(); }

            return _manifest;
        }

        /// <summary>
        /// Drills up to <paramref name="targetDepth"/> (0 = root) for a DRILL_IN visual.
        /// </summary>
        public async Task<ReportManifest?> DrillUpAsync(string visualName, int targetDepth)
        {
            if (_manifest == null || _evaluator == null)
                await GetManifestAsync();
            if (_manifest == null || _evaluator == null) return null;

            var vm = _manifest.Visuals.FirstOrDefault(v => v.Name.Equals(visualName, StringComparison.OrdinalIgnoreCase));
            if (vm == null) return null;

            if (targetDepth <= 0)
                _drillStates.Remove(visualName);
            else if (_drillStates.TryGetValue(visualName, out var existing))
                _drillStates[visualName] = existing with { Path = existing.Path.Take(targetDepth).ToList() };

            await _lock.WaitAsync();
            try
            {
                if (!_evaluator.ReportContext.VisualDefinitions.TryGetValue(visualName, out var vStmt))
                    return _manifest;
                _drillStates.TryGetValue(visualName, out var newState);
                var builder = new ManifestBuilder(_evaluator);
                await builder.RefreshVisualAsync(vStmt, vm, null, newState);
                _manifest.BuiltAt = DateTime.UtcNow;
            }
            finally { _lock.Release(); }

            return _manifest;
        }

        /// <summary>
        /// Checks whether the cached manifest is stale (script file has changed or TTL expired).
        /// </summary>
        public bool IsStale(TimeSpan? ttl = null)
        {
            if (_manifest == null) return true;
            return new SnapshotStore().IsStale(_manifest, _scriptPath, ttl);
        }

        private void StampDefaultLayouts(ReportManifest manifest)
        {
            foreach (var visual in manifest.Visuals.Where(item => item.Layout is null))
                NativeChartLayoutResolver.StampDefault(visual, _layoutProfile);
        }

        private void TrimLayoutCache(int visualCount)
        {
            var limit = Math.Max(3, visualCount * 6);
            if (_layoutCache.Count <= limit) return;
            var currentTicks = _manifest?.BuiltAt.Ticks;
            foreach (var key in _layoutCache.Keys.Where(item => item.ManifestTicks != currentTicks))
                _layoutCache.TryRemove(key, out _);
        }

        private readonly record struct NativeChartLayoutCacheKey(
            string Report,
            string Visual,
            NativeChartLayoutTier Tier,
            long ManifestTicks);

        private sealed record NativeChartLayoutCacheEntry(
            string? NativeSvg,
            ETL_SQL.Reporting.Semantics.PlotPlan? PlotPlan,
            InteractionManifest? Interaction,
            NativeChartLayoutManifest Layout);

    }
}
