using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Reporting;
using ETL_SQL.Core.Security;
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
        private readonly SemaphoreSlim _lock = new(1, 1);

        private IServiceScope? _currentScope;
        private ReportManifest? _manifest;
        private Evaluator? _evaluator;
        private Dictionary<string, string> _parameters = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _runPages = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, VisualDrillState> _drillStates = new(StringComparer.OrdinalIgnoreCase);

        public string ScriptDirectory => Path.GetDirectoryName(_scriptPath) ?? Directory.GetCurrentDirectory();

        public DashboardService(string scriptPath, IServiceScopeFactory scopeFactory, TimeSpan? executionTimeout = null, string? datasetCallerContext = null, int? datasetOwningReportId = null, string? datasetAtRestKey = null, ETL_SQL.Core.Governance.ExecutionIdentity? executionIdentity = null, string? keyScope = null)
        {
            _scriptPath = scriptPath ?? throw new ArgumentNullException(nameof(scriptPath));
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _executionTimeout = executionTimeout ?? TimeSpan.FromSeconds(30);
            _datasetCallerContext = datasetCallerContext;
            _datasetOwningReportId = datasetOwningReportId;
            _datasetAtRestKey = datasetAtRestKey;
            _executionIdentity = executionIdentity;
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
            if (_manifest != null) return _manifest;
            return await RebuildAsync();
        }

        /// <summary>Current parameter values (set by slicer interactions).</summary>
        public IReadOnlyDictionary<string, string> Parameters => _parameters;

        public ILineageTracker? CurrentLineageTracker => _evaluator?.LineageTracker;

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
        public async Task<ReportManifest> SetParametersAsync(IEnumerable<(string Name, string Value)> updates, bool isInteraction = false, string? pageName = null)
        {
            // Warm the session on first access so interaction calls have a live evaluator to work with.
            if (_manifest == null || _evaluator == null)
                await GetManifestAsync();

            // Only update global context if NOT an interaction
            if (!isInteraction)
            {
                foreach (var (name, value) in updates)
                    _parameters[name] = value;

                if (!string.IsNullOrWhiteSpace(pageName) && IsPaginatedPage(pageName))
                {
                    _runPages.Add(pageName);
                    return await RebuildAsync();
                }

                if (string.IsNullOrWhiteSpace(pageName) && HasPaginatedPages())
                {
                    MarkAllPaginatedPagesRun();
                    return await RebuildAsync();
                }
            }

            if (_evaluator != null && _manifest != null)
            {
                await _lock.WaitAsync();
                try
                {
                    if (!isInteraction)
                    {
                        foreach (var (name, value) in updates)
                        {
                            var varName = name.StartsWith('@') ? name : '@' + name;
                            _evaluator.ReportContext.BaselineParameters[varName] = value;
                        }
                    }

                    int refreshCount = await ReportInteractionRefresher.RefreshAffectedVisualsAsync(_evaluator, _manifest, updates, isInteraction);
                    if (refreshCount > 0)
                    {
                        _manifest.BuiltAt = DateTime.UtcNow;
                        _manifest.IsInteraction = isInteraction;
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
        {
            if (!isInteraction)
            {
                _parameters[name] = value;

                if (!string.IsNullOrWhiteSpace(pageName) && IsPaginatedPage(pageName))
                {
                    _runPages.Add(pageName);
                    return await RebuildAsync();
                }

                if (string.IsNullOrWhiteSpace(pageName) && HasPaginatedPages())
                {
                    MarkAllPaginatedPagesRun();
                    return await RebuildAsync();
                }
            }

            // If we have an active evaluator and manifest from a previous run, try selective refresh
            if (_evaluator != null && _manifest != null)
            {
                await _lock.WaitAsync();
                try
                {
                    if (!isInteraction)
                    {
                        var varName = name.StartsWith('@') ? name : '@' + name;
                        _evaluator.ReportContext.BaselineParameters[varName] = value;
                    }

                    int refreshCount = await ReportInteractionRefresher.RefreshAffectedVisualsAsync(_evaluator, _manifest, new[] { (name, value) }, isInteraction);
                    if (refreshCount > 0)
                    {
                        _manifest.BuiltAt = DateTime.UtcNow;
                        _manifest.IsInteraction = isInteraction;
                        return _manifest;
                    }
                }
                finally { _lock.Release(); }
            }

            var result = await RebuildAsync();
            result.IsInteraction = isInteraction;
            return result;
        }

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

                    await evaluator.Evaluate(script);

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
                var registry = _currentScope.ServiceProvider.GetService<IDatasetRegistry>();
                if (registry != null)
                {
                    evaluator.DatasetRegistry = registry;
                    evaluator.DatasetCallerContext = _datasetCallerContext;
                    evaluator.DatasetOwningReportId = _datasetOwningReportId;
                    evaluator.DatasetAtRestKey = _datasetAtRestKey;
                    evaluator.DatasetKeyMaterialProvider =
                        _currentScope.ServiceProvider.GetService<IKeyMaterialProvider>();
                    var portalConfig = _currentScope.ServiceProvider.GetService<ETL_SQL.Portal.PortalConfig>();
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
                    return _manifest;
                }

                var builder = new ManifestBuilder(evaluator);
                _evaluator = evaluator;
                _manifest = await builder.BuildAsync(_scriptPath, runPages: _runPages);

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

    }
}
