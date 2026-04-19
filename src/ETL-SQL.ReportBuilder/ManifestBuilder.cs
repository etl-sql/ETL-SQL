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
        private readonly EChartsRenderer _renderer = new();

        public ManifestBuilder(IExecutionContext ctx) => _ctx = ctx;

        /// <summary>
        /// Builds the manifest by querying each visual's data source.
        /// Must be called after the script has been fully evaluated.
        /// </summary>
        public async Task<ReportManifest> BuildAsync(string scriptSource)
        {
            var manifest = new ReportManifest
            {
                Source      = scriptSource,
                BuiltAt     = DateTime.UtcNow,
                Title       = _ctx.ReportTitle,
                Description = _ctx.ReportDescription
            };

            // ── Visuals ──────────────────────────────────────────────────────
            foreach (var (name, vStmt) in _ctx.VisualDefinitions)
            {
                var vm = new VisualManifest
                {
                    Name         = name,
                    VisualType   = vStmt.VisualType.ToString(),
                    DefaultValue = vStmt.DefaultValue,
                    Tooltip      = BuildTooltipManifest(vStmt.Tooltip)
                };

                // Copy flat options
                foreach (var opt in vStmt.Options)
                    vm.Options[opt.Key] = opt.Value;

                // Styles — named style is the base; inline styles override
                var resolvedStyles = ResolveStyles(vStmt.StyleName, vStmt.Styles);
                if (resolvedStyles.Count > 0)
                    vm.Styles = resolvedStyles;

                // Typed series (COMBO)
                if (vStmt.TypedSeries.Count > 0)
                    vm.SeriesDefs = vStmt.TypedSeries.Select(ts => new SeriesDefManifest { SeriesType = ts.SeriesType, Column = ts.Column }).ToList();

                // Overlay definitions
                if (vStmt.Overlays.Count > 0)
                    vm.Overlays = vStmt.Overlays.Select(o => new OverlayManifest
                    {
                        OverlayType = o.OverlayType.ToString(),
                        Parameter   = o.Parameter,
                        LineStyle   = o.LineStyle.ToString().ToLowerInvariant(),
                        Color       = o.Color,
                        Label       = o.Label
                    }).ToList();

                // Conditional formatting rules (TABLE)
                if (vStmt.FormattingRules.Count > 0)
                    vm.FormattingRules = vStmt.FormattingRules.Select(r => new FormattingRuleManifest
                    {
                        Column    = r.Column,
                        Operator  = r.Operator,
                        Threshold = r.Threshold,
                        Color     = r.Color
                    }).ToList();

                // Copy axis options with axis:{x|y}:{key} prefix for the renderer
                foreach (var axis in vStmt.AxisOptions)
                {
                    var prefix = "axis:" + axis.Axis.ToLowerInvariant() + ":";
                    foreach (var opt in axis.Options)
                        vm.Options[prefix + opt.Key.ToLowerInvariant()] = opt.Value;
                }

                // Store mapping role→column hints so the renderer can find columns
                foreach (var mapping in vStmt.Mappings)
                    vm.Options["mapping:" + mapping.Role.ToLowerInvariant()] = mapping.Column;

                // Copy action bindings
                foreach (var action in vStmt.Actions)
                {
                    vm.Actions.Add(action switch
                    {
                        DrillDownAction dd => new VisualActionManifest
                        {
                            Type         = "DRILL_DOWN",
                            Trigger      = dd.Trigger,
                            TargetVisual = dd.TargetVisual,
                            KeyColumn    = dd.KeyColumn
                        },
                        SetParameterAction sp => new VisualActionManifest
                        {
                            Type            = "SET_PARAMETER",
                            Trigger         = sp.Trigger,
                            ParameterName   = sp.ParameterName,
                            ValueExpression = sp.ValueExpression
                        },
                        _ => new VisualActionManifest { Type = "UNKNOWN", Trigger = action.Trigger }
                    });
                }

                // Materialise data rows
                try
                {
                    await FetchVisualDataAsync(vStmt, vm);
                    ApplyFormatting(vm);
                }
                catch (Exception ex)
                {
                    vm.Error = ex.Message;
                }

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
                {
                    pm.Parameters[param.Name] = param.DefaultValue;
                    if (param.DataType != null)
                    {
                        pm.ParameterTypes ??= new Dictionary<string, string>();
                        pm.ParameterTypes[param.Name] = param.DataType;
                    }
                }

                var resolvedPageStyles = ResolveStyles(pStmt.StyleName, pStmt.Styles);
                if (resolvedPageStyles.Count > 0)
                    pm.Styles = resolvedPageStyles;

                manifest.Pages.Add(pm);
            }

            // ── Containers ───────────────────────────────────────────────────
            if (_ctx.ContainerDefinitions.Count > 0)
            {
                manifest.Containers = new();
                foreach (var (name, cStmt) in _ctx.ContainerDefinitions)
                {
                    var resolvedContainerStyles = ResolveStyles(cStmt.StyleName, cStmt.Styles);
                    manifest.Containers.Add(new ContainerManifest
                    {
                        Name          = name,
                        ContainerType = cStmt.ContainerType,
                        Visuals       = new List<string>(cStmt.Visuals),
                        Styles        = resolvedContainerStyles.Count > 0 ? resolvedContainerStyles : null
                    });
                }
            }

            // ── Navigations ──────────────────────────────────────────────────
            if (_ctx.NavigationDefinitions.Count > 0)
            {
                manifest.Navigations = new();
                foreach (var (name, nStmt) in _ctx.NavigationDefinitions)
                {
                    manifest.Navigations.Add(new NavigationManifest
                    {
                        Name        = name,
                        NavType     = nStmt.NavType.ToString().ToUpperInvariant(),
                        Orientation = nStmt.Orientation.ToString().ToUpperInvariant(),
                        DefaultPage = nStmt.DefaultPage,
                        Pages       = new List<string>(nStmt.Pages)
                    });
                }
            }

            // ── Buttons ──────────────────────────────────────────────────────
            if (_ctx.ButtonDefinitions.Count > 0)
            {
                manifest.Buttons = new();
                foreach (var (name, bStmt) in _ctx.ButtonDefinitions)
                {
                    var resolvedButtonStyles = ResolveStyles(bStmt.StyleName, bStmt.Styles);
                    var bm = new ButtonManifest
                    {
                        Name       = name,
                        ButtonType = bStmt.ButtonType,
                        Title      = bStmt.Title,
                        Tooltip    = BuildTooltipManifest(bStmt.Tooltip),
                        Styles     = resolvedButtonStyles.Count > 0 ? resolvedButtonStyles : null
                    };

                    foreach (var opt in bStmt.Options)
                        bm.Options[opt.Key] = opt.Value;

                    foreach (var action in bStmt.Actions)
                    {
                        bm.Actions.Add(action switch
                        {
                            DrillDownAction dd => new VisualActionManifest
                            {
                                Type         = "DRILL_DOWN",
                                Trigger      = dd.Trigger,
                                TargetVisual = dd.TargetVisual,
                                KeyColumn    = dd.KeyColumn
                            },
                            SetParameterAction sp => new VisualActionManifest
                            {
                                Type            = "SET_PARAMETER",
                                Trigger         = sp.Trigger,
                                ParameterName   = sp.ParameterName,
                                ValueExpression = sp.ValueExpression
                            },
                            _ => new VisualActionManifest { Type = "UNKNOWN", Trigger = action.Trigger }
                        });
                    }
                    manifest.Buttons.Add(bm);
                }
            }

            // ── Datasets ─────────────────────────────────────────────────────
            foreach (var (tableName, dStmt) in _ctx.DatasetDefinitions)
            {
                var rowCount = 0L;
                if (_ctx.Connections.TryGetValue(tableName, out var src))
                {
                    try
                    {
                        await foreach (var batch in src.ReadBatches())
                            rowCount += batch.Rows.Count;
                    }
                    catch { /* source may not support ReadBatches */ }
                }

                manifest.Datasets.Add(new DatasetManifest
                {
                    TempTableName   = tableName,
                    RefreshInterval = dStmt.RefreshInterval,
                    Ttl             = dStmt.Ttl,
                    LastRefresh     = DateTime.UtcNow,
                    RowCount        = rowCount
                });
            }

            return manifest;
        }

        /// <summary>
        /// Re-queries the data for a specific visual and updates its Row/Column collections.
        /// Also regenerates the ChartConfig.
        /// </summary>
        public async Task RefreshVisualAsync(CreateVisualStatement vStmt, VisualManifest vm)
        {
            vm.Rows.Clear();
            vm.Error = null;
            try
            {
                await FetchVisualDataAsync(vStmt, vm);
            }
            catch (Exception ex)
            {
                vm.Error = ex.Message;
            }
            vm.ChartConfig = _renderer.Render(vm);
        }

        /// <summary>
        /// Applies FORMAT option to the mapped "value" column of each row.
        /// Format string should be a standard .NET numeric format specifier (N0, C2, P1, etc.).
        /// </summary>
        private static void ApplyFormatting(VisualManifest vm)
        {
            if (!vm.Options.TryGetValue("FORMAT", out var fmt) || string.IsNullOrWhiteSpace(fmt))
                return;

            var valueColName = vm.Options.TryGetValue("mapping:value", out var mc) ? mc :
                               vm.Columns.Count > 0 ? vm.Columns[0] : null;
            if (valueColName == null) return;

            int colIdx = vm.Columns.IndexOf(valueColName);
            if (colIdx < 0) return;

            for (int i = 0; i < vm.Rows.Count; i++)
            {
                var raw = colIdx < vm.Rows[i].Count ? vm.Rows[i][colIdx] : null;
                if (raw != null && double.TryParse(raw, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var d))
                {
                    vm.Rows[i][colIdx] = d.ToString(fmt, System.Globalization.CultureInfo.CurrentCulture);
                }
            }
        }

        /// <summary>
        /// Merges a named style (base) with inline styles (override). Named style values are
        /// applied first; any key also present in inlineStyles takes the inline value.
        /// </summary>
        private static TooltipManifest? BuildTooltipManifest(TooltipDefinition? tooltip)
        {
            if (tooltip == null) return null;
            if (tooltip.ContainerRef != null)
                return new TooltipManifest { Type = "container", ContainerRef = tooltip.ContainerRef };
            if (tooltip.InlineVisuals != null)
                return new TooltipManifest { Type = "inline", Markdown = tooltip.InlineMarkdown, Visuals = tooltip.InlineVisuals };
            return new TooltipManifest { Type = "text", Text = tooltip.PlainText };
        }

        private Dictionary<string, string> ResolveStyles(string? styleName, Dictionary<string, string> inlineStyles)
        {
            if (string.IsNullOrEmpty(styleName) && inlineStyles.Count == 0)
                return new Dictionary<string, string>();

            var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrEmpty(styleName) &&
                _ctx is IReportContext rc &&
                rc.StyleDefinitions.TryGetValue(styleName, out var namedStyle))
            {
                foreach (var kv in namedStyle.Styles)
                    merged[kv.Key] = kv.Value;
            }

            foreach (var kv in inlineStyles)
                merged[kv.Key] = kv.Value;

            return merged;
        }

        public async Task FetchVisualDataAsync(CreateVisualStatement vStmt, VisualManifest vm)
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
