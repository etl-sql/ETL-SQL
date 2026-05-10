using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Data;

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
        private readonly SemaphoreSlim _lock = new(1, 1);

        private IServiceScope? _currentScope;
        private ReportManifest? _manifest;
        private Evaluator? _evaluator;
        private Dictionary<string, string> _parameters = new(StringComparer.OrdinalIgnoreCase);
        private bool _hasAppliedParameters = false;
        private readonly Dictionary<string, VisualDrillState> _drillStates = new(StringComparer.OrdinalIgnoreCase);

        // Background auto-refresh state
        private CancellationTokenSource? _refreshCts;
        private static readonly Regex _intervalPattern = new(@"^(\d+)([smhd])$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public string ScriptDirectory => Path.GetDirectoryName(_scriptPath) ?? Directory.GetCurrentDirectory();

        public DashboardService(string scriptPath, IServiceScopeFactory scopeFactory, TimeSpan? executionTimeout = null)
        {
            _scriptPath = scriptPath ?? throw new ArgumentNullException(nameof(scriptPath));
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _executionTimeout = executionTimeout ?? TimeSpan.FromSeconds(30);
        }

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

            _refreshCts?.Cancel();
            _refreshCts?.Dispose();
            _lock.Dispose();
        }

        public void Dispose()
        {
            // Fallback for sync disposal (though we prefer DisposeAsync)
            _currentScope?.Dispose();
            _refreshCts?.Cancel();
            _refreshCts?.Dispose();
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

        /// <summary>
        /// Updates multiple parameters atomically and re-evaluates only the affected visuals.
        /// </summary>
        public async Task<ReportManifest> SetParametersAsync(IEnumerable<(string Name, string Value)> updates, bool isInteraction = false)
        {
            // First RUN: do a full rebuild so all deferred (VISIBLE=OFF) visuals get data in one pass
            bool firstRun = !_hasAppliedParameters && !isInteraction;
            if (!isInteraction) _hasAppliedParameters = true;

            if (firstRun)
            {
                foreach (var (name, value) in updates)
                    _parameters[name] = value;
                return await RebuildAsync(); // _hasAppliedParameters=true → skipDeferredVisuals=false
            }

            // Only update global context if NOT an interaction
            if (!isInteraction)
            {
                foreach (var (name, value) in updates)
                    _parameters[name] = value;
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
        public async Task<ReportManifest> SetParameterAsync(string name, string value, bool isInteraction = false)
        {
            if (!isInteraction)
            {
                _hasAppliedParameters = true;
                _parameters[name] = value;
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

                    var lexer  = new Lexer(source);
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

        /// <summary>Full rebuild: re-evaluate the script and re-snapshot all visuals.</summary>
        public async Task<ReportManifest> RebuildAsync()
        {
            await _lock.WaitAsync();
            try
            {
                _drillStates.Clear();
                var source = await File.ReadAllTextAsync(_scriptPath);

                var lexer    = new Lexer(source);
                var tokens   = lexer.Tokenize();
                var parser   = new Parser(tokens, source);
                var script   = parser.Parse();

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
                var registry = _currentScope.ServiceProvider.GetService<IDatasetRegistry>();
                if (registry != null) evaluator.DatasetRegistry = registry;

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
                    _evaluator       = evaluator;
                    _manifest        = await failBuilder.BuildAsync(_scriptPath);
                    _manifest.Error  = ex.Message;
                    return _manifest;
                }

                var builder   = new ManifestBuilder(evaluator);
                _evaluator    = evaluator;
                _manifest     = await builder.BuildAsync(_scriptPath, skipDeferredVisuals: !_hasAppliedParameters);

                ScheduleRefresh(_manifest);
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
            if (_manifest == null || _evaluator == null) return null;

            var vm     = _manifest.Visuals.FirstOrDefault(v => v.Name.Equals(visualName, StringComparison.OrdinalIgnoreCase));
            var action = vm?.Actions?.FirstOrDefault(a => a.Type == "DRILL_IN");
            if (vm == null || action?.Hierarchy == null) return null;

            var hierarchy = action.Hierarchy;
            _drillStates.TryGetValue(visualName, out var existing);
            var path     = existing?.Path.ToList() ?? new List<(string, string)>();
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

        /// <summary>
        /// Starts (or restarts) a background task that rebuilds the manifest at the shortest
        /// REFRESH EVERY interval declared across all CREATE DATASET statements.
        /// Cancels any previously scheduled refresh before starting a new one.
        /// </summary>
        private void ScheduleRefresh(ReportManifest manifest)
        {
            // Cancel previous timer if any
            _refreshCts?.Cancel();
            _refreshCts?.Dispose();
            _refreshCts = null;

            // Find the minimum non-null refresh interval across all datasets
            TimeSpan? minInterval = null;
            foreach (var ds in manifest.Datasets)
            {
                if (string.IsNullOrWhiteSpace(ds.RefreshInterval)) continue;
                var parsed = ParseInterval(ds.RefreshInterval);
                if (parsed.HasValue && (!minInterval.HasValue || parsed.Value < minInterval.Value))
                    minInterval = parsed;
            }

            if (!minInterval.HasValue) return;

            var cts = new CancellationTokenSource();
            _refreshCts = cts;

            // Fire-and-forget background loop; exceptions are swallowed so the server stays up.
            _ = Task.Run(async () =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    try { await Task.Delay(minInterval.Value, cts.Token); }
                    catch (OperationCanceledException) { break; }

                    if (cts.Token.IsCancellationRequested) break;

                    try { await RebuildAsync(); }
                    catch { /* keep the timer running even if a rebuild fails */ }
                }
            }, CancellationToken.None);
        }

        private static TimeSpan? ParseInterval(string interval)
        {
            var m = _intervalPattern.Match(interval.Trim());
            if (!m.Success) return null;

            var amount = int.Parse(m.Groups[1].Value);
            return m.Groups[2].Value.ToLowerInvariant() switch
            {
                "s" => TimeSpan.FromSeconds(amount),
                "m" => TimeSpan.FromMinutes(amount),
                "h" => TimeSpan.FromHours(amount),
                "d" => TimeSpan.FromDays(amount),
                _   => null
            };
        }
    }
}
