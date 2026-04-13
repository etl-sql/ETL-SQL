using System;
using System.Collections.Generic;
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
        /// Updates one parameter and re-evaluates the script so affected visuals
        /// are refreshed with the new value.
        /// </summary>
        public async Task<ReportManifest> SetParameterAsync(string name, string value)
        {
            _parameters[name] = value;
            return await RebuildAsync();
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
