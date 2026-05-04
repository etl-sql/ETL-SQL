using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.ReportPlayer

{
    /// <summary>
    /// Reads a reports.json manifest and vends one <see cref="DashboardService"/>
    /// per report, lazily constructed on first request.
    /// Thread-safe; concurrent first-access races resolve to a single instance via
    /// <see cref="ConcurrentDictionary{TKey,TValue}.GetOrAdd"/>.
    /// </summary>
    public class DashboardServiceFactory
    {
        private readonly ReportsManifest _manifest;
        private readonly string _manifestDir;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ConcurrentDictionary<string, DashboardService> _services
            = new(StringComparer.OrdinalIgnoreCase);
 
        public DashboardServiceFactory(string manifestPath, IServiceScopeFactory scopeFactory)
        {
            var json = File.ReadAllText(manifestPath);
            _manifest = JsonSerializer.Deserialize<ReportsManifest>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
            _manifestDir = Path.GetDirectoryName(Path.GetFullPath(manifestPath))
                ?? Environment.CurrentDirectory;
            _scopeFactory = scopeFactory;
        }
 
        public IReadOnlyList<ReportEntry> Reports => _manifest.Reports;
 
        /// <summary>
        /// Returns the <see cref="DashboardService"/> for the named report,
        /// or null if no such report is listed in the manifest.
        /// </summary>
        public DashboardService? GetService(string name)
        {
            var entry = _manifest.Reports.FirstOrDefault(r =>
                string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
            if (entry == null) return null;
 
            return _services.GetOrAdd(entry.Name, _ =>
            {
                var fullPath = System.IO.Path.IsPathRooted(entry.Path)
                    ? entry.Path
                    : System.IO.Path.GetFullPath(
                        System.IO.Path.Combine(_manifestDir, entry.Path));
                return new DashboardService(fullPath, _scopeFactory);
            });
        }
    }
}
