using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ETL_SQL.Core;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.ReportHosting
{
    /// <summary>
    /// Reads a reports.json manifest and vends one <see cref="DashboardService"/>
    /// per report, lazily constructed on first request.
    /// Thread-safe; concurrent first-access races resolve to a single instance via
    /// <see cref="ConcurrentDictionary{TKey,TValue}.GetOrAdd"/>.
    /// </summary>
    public class DashboardServiceFactory
    {
        private readonly string _manifestDir;
        private readonly IReadOnlyList<ReportEntry> _reports;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly TimeSpan _executionTimeout;
        private readonly ConcurrentDictionary<string, DashboardService> _services
            = new(StringComparer.OrdinalIgnoreCase);

        public DashboardServiceFactory(string manifestPath, IServiceScopeFactory scopeFactory, TimeSpan? executionTimeout = null)
        {
            _manifestDir = Path.GetDirectoryName(Path.GetFullPath(manifestPath))
                ?? Environment.CurrentDirectory;
            var json = File.ReadAllText(manifestPath);
            var manifest = JsonSerializer.Deserialize<ReportsManifest>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
            _reports = manifest.Reports
                .Where(r => SafePath.TryResolveWithinRoot(_manifestDir, r.Path, out _))
                .ToList();
            _scopeFactory = scopeFactory;
            _executionTimeout = executionTimeout ?? TimeSpan.FromSeconds(30);
        }

        public string ManifestDirectory => _manifestDir;

        public IReadOnlyList<ReportEntry> Reports => _reports;

        /// <summary>
        /// Returns the <see cref="DashboardService"/> for the named report,
        /// or null if no such report is listed in the manifest.
        /// </summary>
        public DashboardService? GetService(string name)
        {
            var entry = _reports.FirstOrDefault(r =>
                string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
            if (entry == null) return null;

            return _services.GetOrAdd(entry.Name, _ =>
            {
                if (!SafePath.TryResolveWithinRoot(_manifestDir, entry.Path, out var fullPath))
                    throw new InvalidOperationException("Report path is outside the manifest directory.");
                return new DashboardService(fullPath, _scopeFactory, _executionTimeout);
            });
        }
    }
}
