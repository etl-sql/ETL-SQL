using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Core.Parser;


using ETL_SQL.Engine;
using ETL_SQL.ReportBuilder;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.ReportPlayer
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
    /// Parameter changes from slicer interactions call <see cref="RebuildAsync"/>
    /// to re-evaluate only the affected visuals (Phase 9D simplified: full rebuild).
    /// </summary>
    public class DashboardService
    {
        private readonly string _scriptPath;
        private readonly SemaphoreSlim _lock = new(1, 1);

        private ReportManifest? _manifest;
        private Evaluator? _evaluator;
        private Dictionary<string, string> _parameters = new(StringComparer.OrdinalIgnoreCase);

        // Background auto-refresh state
        private CancellationTokenSource? _refreshCts;
        private static readonly Regex _intervalPattern = new(@"^(\d+)([smhd])$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public DashboardService(string scriptPath)
        {
            _scriptPath = scriptPath ?? throw new ArgumentNullException(nameof(scriptPath));
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
        /// More efficient than calling <see cref="SetParameterAsync"/> in sequence when
        /// several filter controls (e.g. date range start + end) change together.
        /// </summary>
        public async Task<ReportManifest> SetParametersAsync(IEnumerable<(string Name, string Value)> updates)
        {
            foreach (var (name, value) in updates)
                _parameters[name] = value;

            if (_evaluator != null && _manifest != null)
            {
                await _lock.WaitAsync();
                try
                {
                    int refreshCount = await DashboardSharedLogic.RefreshAffectedVisualsAsync(_evaluator, _manifest, updates);
                    if (refreshCount > 0)
                    {
                        _manifest.BuiltAt = DateTime.UtcNow;
                        return _manifest;
                    }
                }
                finally { _lock.Release(); }
            }

            return await RebuildAsync();
        }

        /// <summary>
        /// Updates one parameter and re-evaluates only the affected visuals
        /// rather than doing a full script rebuild (Tier 1 Optimization).
        /// </summary>
        public async Task<ReportManifest> SetParameterAsync(string name, string value)
        {
            _parameters[name] = value;
            
            // If we have an active evaluator and manifest from a previous run, try selective refresh
            if (_evaluator != null && _manifest != null)
            {
                await _lock.WaitAsync();
                try 
                {
                    int refreshCount = await DashboardSharedLogic.RefreshAffectedVisualsAsync(_evaluator, _manifest, new[] { (name, value) });
                    if (refreshCount > 0)
                    {
                        _manifest.BuiltAt = DateTime.UtcNow;
                        return _manifest;
                    }
                }
                finally { _lock.Release(); }
            }

            return await RebuildAsync();
        }

        private bool DependsOnVariable(CreateVisualStatement visual, string variableName)
        {
            return DashboardSharedLogic.DependsOnVariable(visual, variableName);
        }

        /// <summary>Full rebuild: re-evaluate the script and re-snapshot all visuals.</summary>
        public async Task<ReportManifest> RebuildAsync()
        {
            await _lock.WaitAsync();
            try
            {
                var source = await File.ReadAllTextAsync(_scriptPath);

                var lexer    = new Lexer(source);
                var tokens   = lexer.Tokenize();
                var parser   = new Parser(tokens, source);
                var script   = parser.Parse();

                var provider  = DependencyInjectionSetup.BuildServiceProvider();
                var evaluator = provider.GetRequiredService<Evaluator>();
                evaluator.RedirectOutput = true;

                // Security Hardening (CR-S1): Inject current parameter values directly into the scope 
                // instead of concatenating source text. This prevents script injection.
                foreach (var (name, value) in _parameters)
                {
                    var varName = name.StartsWith('@') ? name : '@' + name;
                    evaluator.DeclareVariable(varName, value, new VariableMetadata { IsInput = true });
                }

                await evaluator.Evaluate(script);

                var builder   = new ManifestBuilder(evaluator);
                _evaluator    = evaluator;
                _manifest     = await builder.BuildAsync(_scriptPath);

                ScheduleRefresh(_manifest);
                return _manifest;
            }
            finally
            {
                _lock.Release();
            }
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
