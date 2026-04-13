using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
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
        private Evaluator? _evaluator; // Cache evaluator to allow partial re-materialization
        private Dictionary<string, string> _parameters = new(StringComparer.OrdinalIgnoreCase);

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
                    var varName = name.StartsWith('@') ? name : "@" + name;
                    _evaluator.DeclareVariable(varName, value, new VariableMetadata { IsInput = true });

                    var builder = new ManifestBuilder(_evaluator);
                    var affectedCount = 0;

                    foreach (var visualDef in _evaluator.VisualDefinitions.Values)
                    {
                        if (DependsOnVariable(visualDef, name))
                        {
                            var existingVm = _manifest.Visuals.FirstOrDefault(v => v.Name == visualDef.Name);
                            if (existingVm != null)
                            {
                                await builder.RefreshVisualAsync(visualDef, existingVm);
                                affectedCount++;
                            }
                        }
                    }

                    if (affectedCount > 0)
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
            if (!variableName.StartsWith("@")) variableName = "@" + variableName;
            
            string? sql = visual.Source.IsInlineSelect 
                ? visual.Source.InlineSelect?.ToSql() 
                : visual.Source.TempTableName;
                
            return sql?.Contains(variableName, StringComparison.OrdinalIgnoreCase) ?? false;
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
                _evaluator    = evaluator; // Hold onto evaluator for partial refreshes
                _manifest     = await builder.BuildAsync(_scriptPath);
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
    }
}
