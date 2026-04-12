using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Data;

namespace ETL_SQL.ReportBuilder
{
    /// <summary>
    /// Walks the post-execution context to collect visual/page/dataset definitions
    /// and materialise their data into a <see cref="ReportManifest"/>.
    /// </summary>
    public class ManifestBuilder
    {
        private readonly IExecutionContext _ctx;
        private readonly ChartJsRenderer _renderer = new();

        public ManifestBuilder(IExecutionContext ctx) => _ctx = ctx;

        /// <summary>
        /// Builds the manifest by querying each visual's data source.
        /// Must be called after the script has been fully evaluated.
        /// </summary>
        public async Task<ReportManifest> BuildAsync(string scriptSource)
        {
            var manifest = new ReportManifest { Source = scriptSource, BuiltAt = DateTime.UtcNow };

            // ── Visuals ──────────────────────────────────────────────────────
            foreach (var (name, vStmt) in _ctx.VisualDefinitions)
            {
                var vm = new VisualManifest
                {
                    Name       = name,
                    VisualType = vStmt.VisualType.ToString()
                };

                // Copy flat options
                foreach (var opt in vStmt.Options)
                    vm.Options[opt.Key] = opt.Value;

                // Store mapping role→column hints so ChartJsRenderer can find columns
                foreach (var mapping in vStmt.Mappings)
                    vm.Options["mapping:" + mapping.Role.ToLowerInvariant()] = mapping.Column;

                // Materialise data rows
                await FetchVisualDataAsync(vStmt, vm);

                vm.ChartConfig = _renderer.Render(vm);
                manifest.Visuals.Add(vm);
            }

            // ── Pages ────────────────────────────────────────────────────────
            foreach (var (name, pStmt) in _ctx.PageDefinitions)
            {
                var pm = new PageManifest
                {
                    Name      = name,
                    Structure = pStmt.Structure,
                    SlotMap   = new Dictionary<string, string>(pStmt.SlotMap)
                };
                foreach (var param in pStmt.Parameters)
                    pm.Parameters[param.Name] = param.DefaultValue;

                manifest.Pages.Add(pm);
            }

            // ── Datasets ─────────────────────────────────────────────────────
            // Infer from known temp tables that were produced by CREATE DATASET
            // (we track the AST definitions in VisualDefinitions; datasets themselves
            //  are just regular temp tables in the context after execution).
            // For Phase 9B, we enumerate all registered temp sources and report them.
            foreach (var (tableName, source) in _ctx.Connections)
            {
                if (!tableName.StartsWith('#')) continue;
                var rowCount = 0L;
                try
                {
                    await foreach (var batch in source.ReadBatches())
                        rowCount += batch.Rows.Count;
                }
                catch { /* source may not support ReadBatches */ }

                manifest.Datasets.Add(new DatasetManifest
                {
                    TempTableName = tableName,
                    LastRefresh   = DateTime.UtcNow,
                    RowCount      = rowCount
                });
            }

            return manifest;
        }

        private async Task FetchVisualDataAsync(CreateVisualStatement vStmt, VisualManifest vm)
        {
            Statement queryStmt;

            if (vStmt.Source.IsInlineSelect && vStmt.Source.InlineSelect != null)
            {
                queryStmt = vStmt.Source.InlineSelect;
            }
            else if (vStmt.Source.TempTableName != null)
            {
                // Build a simple SELECT * FROM #tableName
                var tableRef = new TableReference(vStmt.Source.TempTableName);
                queryStmt = new SelectStatement(
                    new List<SelectColumn> { new SelectColumn(new IdentifierExpression("*")) },
                    null, tableRef, new List<JoinClause>(), null);
            }
            else return;

            bool firstBatch = true;
            await foreach (var batch in _ctx.ExecuteQuery(queryStmt))
            {
                if (firstBatch)
                {
                    vm.Columns = batch.ColumnNames.ToList();
                    firstBatch = false;
                }
                foreach (var row in batch.Rows)
                {
                    vm.Rows.Add(vm.Columns.Select(c => row[c]?.ToString()).ToList());
                }
            }
        }
    }
}
