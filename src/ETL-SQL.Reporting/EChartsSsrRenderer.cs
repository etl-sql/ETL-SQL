#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using Microsoft.ClearScript.V8;

namespace ETL_SQL.Reporting
{
    /// <summary>
    /// Renders a chart's pre-built ECharts option (<see cref="VisualManifest.ChartConfig"/>)
    /// to an SVG string using the real ECharts library running server-side in
    /// ClearScript/V8 (SSR). This gives exports the same charts as the on-screen report,
    /// for every chart type, instead of the hand-rolled <see cref="SvgChartRenderer"/>.
    ///
    /// Thread-safe via a lock around one reused V8 engine (echarts is loaded once).
    /// Returns <c>null</c> when there is no chart option or rendering fails, so callers
    /// fall back to the static renderer and exports never crash.
    /// </summary>
    public sealed class EChartsSsrRenderer : IDisposable
    {
        private static readonly Lazy<EChartsSsrRenderer> _shared = new(() => new EChartsSsrRenderer());
        public static EChartsSsrRenderer Shared => _shared.Value;

        /// <summary>
        /// Optional sink for SSR failures (engine init or per-chart render). The host's composition
        /// root wires this to its logger; null by default so library/test usage stays silent. Without
        /// it, SSR failures are invisible — a chart silently downgrades to the static renderer, or a
        /// missing V8 runtime turns the whole high-fidelity path off with no diagnostic.
        /// </summary>
        public static Action<string, Exception>? OnError { get; set; }

        // Bare V8 has no host timer APIs; echarts schedules animation with them, which
        // SSR doesn't need — no-op shims let it load and render.
        private const string Shims = @"
            globalThis.setTimeout    = function(){ return 0; };
            globalThis.clearTimeout  = function(){};
            globalThis.setInterval   = function(){ return 0; };
            globalThis.clearInterval = function(){};";

        // Mirrors the JS-only finalizers from report-runtime.js renderChart() that affect
        // the *rendered* output (symbol sizing, custom renderItem, value formatters), then
        // forces a static render. Interactive concerns (tooltip/brush/cross-filter) are skipped.
        private const string RenderFn = @"
            globalThis.__renderChartSvg = function(configJson, w, h) {
                var option = JSON.parse(configJson);
                var matchBy = (option.__matchBy || 'NAME').toUpperCase();
                delete option.__mapKey; delete option.__matchBy; delete option.__mapFile;

                if (option.__bubbleSymbolSize) {
                    delete option.__bubbleSymbolSize;
                    (option.series||[]).forEach(function(s){ if (s.type==='scatter') s.symbolSize = function(v){ return v[2]; }; });
                }
                (option.series||[]).forEach(function(s){
                    if (s.__pointsSymbolSize) { delete s.__pointsSymbolSize; if (s.type==='scatter') s.symbolSize = function(v){ return v[2]; }; }
                });

                if (option.__ganttRenderItem) {
                    delete option.__ganttRenderItem;
                    (option.series||[]).forEach(function(s){
                        if (s.type==='custom') {
                            s.renderItem = function(params, api){
                                var ci = api.value(0);
                                var start = api.coord([api.value(1), ci]);
                                var end = api.coord([api.value(2), ci]);
                                var height = api.size([0,1])[1]*0.6;
                                return { type:'rect', shape:{ x:start[0], y:start[1]-height/2, width:Math.max(end[0]-start[0],2), height:height }, style: api.style({ fill: api.value(4) || '#5470c6' }) };
                            };
                        }
                    });
                }

                if (matchBy==='FIPS') (option.series||[]).forEach(function(s){ if (s.type==='map') s.nameProperty='fips'; });

                (option.series||[]).forEach(function(s){
                    if (s.type==='gauge' && s.detail && (s.detail.formatter==='{value}' || s.detail.formatter==='{value:.1f}'))
                        s.detail.formatter = function(v){ return (typeof v==='number') ? v.toFixed(1) : v; };
                    if (s.label && s.label.show && !s.label.formatter)
                        s.label.formatter = function(p){ var v=p.value; if (Array.isArray(v)) v=v[v.length-1]; return (typeof v==='number' && !Number.isInteger(v)) ? v.toFixed(2) : v; };
                });

                // Static export: no animation, no interactive chrome.
                option.animation = false;
                option.toolbox = { show: false };
                if (option.tooltip) option.tooltip.show = false;

                var chart = echarts.init(null, null, { renderer:'svg', ssr:true, width:w, height:h });
                chart.setOption(option);
                var svg = chart.renderToSVGString();
                chart.dispose();
                return svg;
            };";

        private readonly ConcurrentQueue<PooledEngine> _pool = new();
        private readonly SemaphoreSlim _poolSemaphore = new(Environment.ProcessorCount);
        private readonly object _initLock = new();
        private bool _initFailed;
        private string? _cachedEchartsJs;

        private sealed class PooledEngine : IDisposable
        {
            public V8ScriptEngine Engine { get; }
            public HashSet<string> RegisteredMaps { get; } = new(StringComparer.OrdinalIgnoreCase);

            public PooledEngine(V8ScriptEngine engine)
            {
                Engine = engine;
            }

            public void Dispose()
            {
                Engine.Dispose();
            }
        }

        /// <summary>
        /// Render the visual's ECharts option to an SVG string, or null if it has no
        /// chart option (CARD/TABLE/TEXT/etc.) or rendering is unavailable.
        /// </summary>
        public string? RenderSvg(VisualManifest visual, int width = 600, int height = 350)
        {
            if (string.IsNullOrWhiteSpace(visual.ChartConfig)) return null;
            if (_initFailed) return null;

            _poolSemaphore.Wait();
            PooledEngine? pooled = null;
            try
            {
                if (_initFailed) return null;

                if (!_pool.TryDequeue(out pooled))
                {
                    lock (_initLock)
                    {
                        if (_initFailed) return null;
                        try
                        {
                            pooled = CreateEngine();
                        }
                        catch (Exception ex)
                        {
                            _initFailed = true;
                            OnError?.Invoke("ECharts SSR engine failed to initialize (V8/echarts unavailable); " +
                                            "all chart exports will use the static renderer", ex);
                            return null;
                        }
                    }
                }

                EnsureMapRegistered(pooled, visual.ChartConfig!);
                return pooled.Engine.Script.__renderChartSvg(visual.ChartConfig, width, height) as string;
            }
            catch (Exception ex)
            {
                OnError?.Invoke("ECharts SSR render failed; falling back to the static chart renderer", ex);
                return null; // caller falls back to the static renderer
            }
            finally
            {
                if (pooled != null)
                {
                    if (_initFailed)
                    {
                        pooled.Dispose();
                    }
                    else
                    {
                        _pool.Enqueue(pooled);
                    }
                }
                _poolSemaphore.Release();
            }
        }

        private PooledEngine CreateEngine()
        {
            var engine = new V8ScriptEngine();
            engine.Execute(Shims);
            if (_cachedEchartsJs == null)
            {
                _cachedEchartsJs = LoadEcharts();
            }
            engine.Execute(_cachedEchartsJs);
            engine.Execute(RenderFn);
            engine.Execute("globalThis.__registerMap = function(name, geojson){ echarts.registerMap(name, JSON.parse(geojson)); };");
            return new PooledEngine(engine);
        }

        private static string LoadEcharts()
        {
            var asm  = typeof(EChartsSsrRenderer).Assembly;
            var name = Array.Find(asm.GetManifestResourceNames(),
                           n => n.EndsWith("echarts.min.js", StringComparison.OrdinalIgnoreCase))
                       ?? throw new InvalidOperationException("echarts.min.js embedded resource not found");
            using var s = asm.GetManifestResourceStream(name)!;
            using var r = new StreamReader(s);
            return r.ReadToEnd();
        }

        // MAP charts reference a registered map by name (series.map). Register the
        // matching bundled GeoJSON into ECharts once, on demand.
        private void EnsureMapRegistered(PooledEngine pooled, string chartConfig)
        {
            string? mapKey = null;
            try
            {
                using var doc = JsonDocument.Parse(chartConfig);
                if (doc.RootElement.TryGetProperty("__mapKey", out var mk) && mk.ValueKind == JsonValueKind.String)
                    mapKey = mk.GetString();
            }
            catch { return; }

            if (string.IsNullOrEmpty(mapKey) || pooled.RegisteredMaps.Contains(mapKey)) return;

            var geojson = LoadGeojson(mapKey);
            if (geojson == null) return; // unknown map → render without it rather than fail

            pooled.Engine.Script.__registerMap(mapKey, geojson);
            pooled.RegisteredMaps.Add(mapKey);
        }

        private static string? LoadGeojson(string mapKey)
        {
            var asm  = typeof(EChartsSsrRenderer).Assembly;
            var name = Array.Find(asm.GetManifestResourceNames(),
                n => n.EndsWith("." + mapKey + ".geojson", StringComparison.OrdinalIgnoreCase));
            if (name == null) return null;
            using var s = asm.GetManifestResourceStream(name)!;
            using var r = new StreamReader(s);
            return r.ReadToEnd();
        }

        public void Dispose()
        {
            while (_pool.TryDequeue(out var pooled))
            {
                pooled.Dispose();
            }
            _poolSemaphore.Dispose();
        }
    }
}
