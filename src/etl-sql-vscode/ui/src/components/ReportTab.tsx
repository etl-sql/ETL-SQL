import React, { useEffect, useRef, useState, useMemo } from 'react';
import * as echarts from 'echarts';
import { RefreshCw, AlertCircle, Calendar, FileText, File, Download, CheckSquare } from 'lucide-react';
import type { ReportManifest, VisualManifest, ContainerManifest, PageManifest } from '../types';
import { clsx } from 'clsx';

interface ReportTabProps {
    manifest: ReportManifest;
    onRefresh: (parameters?: Record<string, string | null>) => void;
    onExport?: (format: 'markdown' | 'pdf' | 'text') => void;
}

export const ReportTab: React.FC<ReportTabProps> = ({ manifest, onRefresh, onExport }) => {
    const [activePageName, setActivePageName] = useState<string | null>(
        manifest.pages?.[0]?.name || null
    );

    const [crossFilterSource, setCrossFilterSource] = useState<string | null>(null);
    const [baselineManifest, setBaselineManifest] = useState<ReportManifest | null>(null);

    // Local parameter state initialized from manifest defaults
    const [parameters, setParameters] = useState<Record<string, string | null>>(manifest.parameters || {});
    const [isRefreshing, setIsRefreshing] = useState(false);
    const [contextMenu, setContextMenu] = useState<{ x: number, y: number, visual: VisualManifest, rowData?: any[] } | null>(null);
    const debounceTimer = useRef<any>(null);

    const activePage = useMemo(() => 
        manifest.pages.find(p => p.name === activePageName) || manifest.pages[0],
    [manifest, activePageName]);

    // Reset isRefreshing when manifest changes (indicating refresh completed)
    useEffect(() => {
        setIsRefreshing(false);
    }, [manifest]);

    // Initialize baseline manifest on first load
    useEffect(() => {
        if (!baselineManifest) {
            setBaselineManifest(manifest);
        }
    }, [manifest]);

    // Initialize parameters when manifest changes
    useEffect(() => {
        if (manifest.parameters) {
            setParameters(manifest.parameters);
        }
    }, [manifest]);

    // Handle parameter changes from slicers/inputs
    const handleParameterUpdate = (name: string, value: string, sourceVisual?: string) => {
        // Toggle: if same value clicked on same visual, clear it
        const isToggleOff = sourceVisual && crossFilterSource === sourceVisual && parameters[name] === value;
        const finalValue = isToggleOff ? '' : value;
        const finalSource = isToggleOff ? null : (sourceVisual || crossFilterSource);

        setParameters(prev => ({ ...prev, [name]: finalValue }));
        setCrossFilterSource(finalSource);
        
        // Debounced auto-refresh
        if (debounceTimer.current) clearTimeout(debounceTimer.current);
        debounceTimer.current = setTimeout(() => {
            setIsRefreshing(true);
            onRefresh({ ...parameters, [name]: finalValue });
        }, 500);
    };

    const handleClearFilters = () => {
        setParameters({});
        setCrossFilterSource(null);
        setIsRefreshing(true);
        onRefresh({});
    };

    return (
        <div className="flex-1 flex flex-col min-h-0 bg-[var(--bg-dark)] text-[var(--text)] overflow-hidden font-display">
            {/* Header */}
            <header className="px-6 py-4 border-b border-[var(--border)] bg-[var(--bg-darker)]/40 backdrop-blur-xl flex items-center justify-between shrink-0">
                <div className="flex flex-col">
                    <h1 className="text-xl font-bold tracking-tight text-[var(--text)] flex items-center gap-2">
                        {manifest.title || 'Untitled Report'}
                    </h1>
                    {manifest.description && (
                        <p className="text-xs text-[var(--muted)] line-clamp-1 mt-0.5">{manifest.description}</p>
                    )}
                </div>

                <div className="flex items-center gap-4">
                    <div className={clsx(
                        "flex items-center gap-2 px-3 py-1.5 rounded-full border text-[10px] font-bold uppercase tracking-widest transition-all duration-500",
                        isRefreshing 
                            ? "bg-blue-500/10 border-blue-500/30 text-blue-400 animate-pulse" 
                            : "bg-indigo-500/10 border-indigo-500/20 text-indigo-400"
                    )}>
                        {isRefreshing ? <RefreshCw size={12} className="animate-spin" /> : <Calendar size={12} />}
                        {isRefreshing ? 'Refreshing Report...' : `Built: ${new Date(manifest.builtAt).toLocaleString()}`}
                    </div>
                    
                    <div className="flex items-center gap-1 bg-current/5 p-1 rounded-xl border border-[var(--border)]">
                        <button 
                            onClick={handleClearFilters}
                            className="flex items-center gap-2 px-3 py-1.5 rounded-lg hover:bg-red-500/10 text-[var(--text)] opacity-50 hover:text-red-400 transition-all text-[10px] font-bold uppercase tracking-wider"
                            title="Clear All Filters"
                        >
                            <RefreshCw size={12} className={clsx(isRefreshing && "animate-spin")} />
                            <span>Clear</span>
                        </button>
                        <div className="w-px h-4 bg-[var(--border)] mx-1" />
                        <button 
                            onClick={() => (window as any).vscode?.postMessage({ type: 'serve' })}
                            className="flex items-center gap-2 px-3 py-1.5 rounded-lg hover:bg-current/10 text-[var(--text)] opacity-70 hover:text-green-400 transition-all text-xs font-medium"
                            title="Serve Live"
                        >
                            <RefreshCw size={14} className={isRefreshing ? "animate-spin" : ""} />
                            <span>Serve</span>
                        </button>
                        <div className="w-px h-4 bg-[var(--border)] mx-1" />
                        <button 
                            onClick={() => onExport?.('markdown')}
                            className="p-1.5 rounded-lg hover:bg-current/10 text-[var(--text)] opacity-70 hover:text-indigo-400 transition-all"
                            title="Export to Markdown"
                        >
                            <FileText size={16} />
                        </button>
                        <button 
                            onClick={() => onExport?.('pdf')}
                            className="p-1.5 rounded-lg hover:bg-current/10 text-[var(--text)] opacity-70 hover:text-indigo-400 transition-all"
                            title="Export to PDF"
                        >
                            <File size={16} />
                        </button>
                        <button 
                            onClick={() => onExport?.('text')}
                            className="p-1.5 rounded-lg hover:bg-current/10 text-[var(--text)] opacity-70 hover:text-indigo-400 transition-all"
                            title="Export to Text"
                        >
                            <Download size={16} />
                        </button>
                    </div>

                    <button 
                        onClick={() => {
                            setIsRefreshing(true);
                            onRefresh(parameters);
                        }}
                        disabled={isRefreshing}
                        className={clsx(
                            "p-2 rounded-xl border transition-all duration-300 group shadow-lg",
                            isRefreshing 
                                ? "bg-current/5 text-[var(--muted)] border-[var(--border)] opacity-50 cursor-not-allowed"
                                : "bg-current/5 hover:bg-indigo-500/20 text-[var(--muted)] hover:text-indigo-400 border-[var(--border)] hover:border-indigo-500/30"
                        )}
                        title="Refresh Report"
                    >
                        <RefreshCw size={18} className={clsx("transition-transform duration-700", !isRefreshing && "group-hover:rotate-180")} />
                    </button>
                </div>
            </header>

            {/* Navigation Tabs (if defined or multiple pages) */}
            {(manifest.navigations?.length || manifest.pages.length > 1) && (
                <div className={clsx(
                    "px-6 shrink-0 flex gap-2 overflow-x-auto no-scrollbar py-2",
                    "border-b border-[var(--border)] bg-[var(--bg-darker)]/20 shadow-inner"
                )}>
                    {/* Render Explicit Navigations if any */}
                    {manifest.navigations?.map(nav => (
                        <div key={nav.name} className={clsx("flex gap-2", nav.orientation === 'VERTICAL' ? "flex-col" : "flex-row")}>
                            {nav.pages.map(pageName => (
                                <button
                                    key={pageName}
                                    onClick={() => setActivePageName(pageName)}
                                    className={clsx(
                                        "px-4 py-1.5 rounded-lg text-xs font-bold transition-all duration-300 whitespace-nowrap border",
                                        activePageName === pageName 
                                            ? "bg-indigo-500 text-[var(--text)] border-indigo-400/50 shadow-[0_0_15px_rgba(99,102,241,0.4)]" 
                                            : "bg-current/5 text-[var(--muted)] border-transparent hover:bg-current/10 hover:text-[var(--text)]"
                                    )}
                                >
                                    {pageName}
                                </button>
                            ))}
                        </div>
                    ))}
                    {/* Fallback to simple page list if no explicit navigation defined */}
                    {(!manifest.navigations || manifest.navigations.length === 0) && manifest.pages.map(page => (
                        <button
                            key={page.name}
                            onClick={() => setActivePageName(page.name)}
                            className={clsx(
                                "px-4 py-1.5 rounded-lg text-xs font-bold transition-all duration-300 whitespace-nowrap border",
                                activePageName === page.name 
                                    ? "bg-indigo-500 text-white border-indigo-400/50 shadow-[0_0_15px_rgba(99,102,241,0.4)]" 
                                    : "bg-white/5 text-[var(--muted)] border-transparent hover:bg-white/10"
                            )}
                        >
                            {page.name}
                        </button>
                    ))}
                </div>
            )}

            {/* Content Area */}
            <main className="flex-1 overflow-auto p-6 custom-scrollbar relative">
                {activePage && (
                    <RenderPage 
                        page={activePage} 
                        manifest={manifest} 
                        baselineManifest={baselineManifest}
                        parameters={parameters} 
                        onParameterChange={handleParameterUpdate}
                        crossFilterSource={crossFilterSource}
                        onShowContextMenu={setContextMenu}
                    />
                )}

                {/* Context Menu Portal */}
                {contextMenu && (
                    <ContextMenu 
                        {...contextMenu} 
                        onClose={() => setContextMenu(null)} 
                        onAction={(action) => {
                            if (action.type === 'DRILL_DOWN') {
                                const target = action.targetVisual || action.targetPage;
                                if (target) {
                                    const targetPage = manifest.pages.find(p => p.name === target);
                                    if (targetPage) {
                                        setActivePageName(targetPage.name);
                                    } else {
                                        const el = document.querySelector(`[data-visual-name="${target}"]`);
                                        el?.scrollIntoView({ behavior: 'smooth' });
                                    }
                                }
                                if (action.parameterName) {
                                    handleParameterUpdate(action.parameterName, String(contextMenu.rowData?.[0] || ''));
                                }
                            }
                        }}
                    />
                )}
            </main>
        </div>
    );
};

const RenderPage: React.FC<{ 
    page: PageManifest, 
    manifest: ReportManifest,
    baselineManifest: ReportManifest | null,
    parameters: Record<string, string | null>,
    onParameterChange: (name: string, value: string, source?: string) => void,
    crossFilterSource: string | null,
    onShowContextMenu: (val: any) => void
}> = ({ page, manifest, baselineManifest, parameters, onParameterChange, crossFilterSource, onShowContextMenu }) => {
    return (
        <GenericLayout 
            structure={page.structure} 
            slotMap={page.slotMap} 
            manifest={manifest} 
            baselineManifest={baselineManifest}
            parameters={parameters} 
            onParameterChange={onParameterChange}
            crossFilterSource={crossFilterSource}
            onShowContextMenu={onShowContextMenu}
        />
    );
};

const GenericLayout: React.FC<{
    structure: string,
    slotMap: Record<string, string>,
    manifest: ReportManifest,
    baselineManifest: ReportManifest | null,
    parameters: Record<string, string | null>,
    onParameterChange: (name: string, value: string, source?: string) => void,
    crossFilterSource: string | null,
    onShowContextMenu: (val: any) => void
}> = ({ structure, slotMap, manifest, baselineManifest, parameters, onParameterChange, crossFilterSource, onShowContextMenu }) => {
    const rows = structure.split('/').map(r => r.trim());
    const rowCount = rows.length;
    const colCount = rows[0].split(/\s+/).length;
    const gridStyle = {
        gridTemplateAreas: rows.map(r => `'${r}'`).join(' '),
        gridTemplateColumns: `repeat(${colCount}, 1fr)`,
        gridTemplateRows: `repeat(${rowCount}, minmax(40px, auto))`, 
    };

    return (
        <div className="grid gap-6 animate-in fade-in slide-in-from-bottom-4 duration-700" style={gridStyle}>
            {Object.keys(slotMap).map(slot => {
                const objectName = slotMap[slot];
                return (
                    <div key={slot} style={{ gridArea: slot }} className="flex" data-visual-name={objectName}>
                        <RenderObject 
                            name={objectName} 
                            manifest={manifest} 
                            baselineManifest={baselineManifest}
                            parameters={parameters}
                            onParameterChange={onParameterChange}
                            crossFilterSource={crossFilterSource}
                            onShowContextMenu={onShowContextMenu}
                        />
                    </div>
                );
            })}
        </div>
    );
};

const RenderObject: React.FC<{ 
    name: string, 
    manifest: ReportManifest,
    baselineManifest: ReportManifest | null,
    parameters: Record<string, string | null>,
    onParameterChange: (name: string, value: string, source?: string) => void,
    crossFilterSource: string | null,
    onShowContextMenu: (val: any) => void
}> = ({ name, manifest, baselineManifest, parameters, onParameterChange, crossFilterSource, onShowContextMenu }) => {
    const visual = manifest.visuals.find(v => v.name.toLowerCase() === name.toLowerCase());
    if (visual) return (
        <VisualCard 
            visual={visual} 
            manifest={manifest}
            baselineManifest={baselineManifest}
            parameters={parameters}
            onParameterChange={onParameterChange}
            crossFilterSource={crossFilterSource}
            onShowContextMenu={onShowContextMenu}
        />
    );

    const container = manifest.containers?.find(c => c.name.toLowerCase() === name.toLowerCase());
    if (container) return (
        <ContainerView 
            container={container} 
            manifest={manifest} 
            baselineManifest={baselineManifest}
            parameters={parameters} 
            onParameterChange={onParameterChange}
            crossFilterSource={crossFilterSource}
            onShowContextMenu={onShowContextMenu}
        />
    );

    return (
        <div className="w-full p-8 rounded-3xl bg-red-500/10 border border-red-500/20 flex items-center gap-4 text-red-400">
            <AlertCircle size={24} />
            <div className="flex flex-col">
                <span className="font-bold uppercase tracking-widest text-[10px]">Reference Error</span>
                <span className="text-sm">Object "{name}" not found in manifest.</span>
            </div>
        </div>
    );
};

const ContainerView: React.FC<{ 
    container: ContainerManifest, 
    manifest: ReportManifest,
    baselineManifest: ReportManifest | null,
    parameters: Record<string, string | null>,
    onParameterChange: (name: string, value: string, source?: string) => void,
    crossFilterSource: string | null,
    onShowContextMenu: (val: any) => void
}> = ({ container, manifest, baselineManifest, parameters, onParameterChange, crossFilterSource, onShowContextMenu }) => {
    if (container.structure && container.slotMap) {
        return (
            <div className="w-full flex flex-col gap-4">
                {(container.title || container.subtitle) && (
                    <div className="flex flex-col gap-1 px-1">
                        {container.title && <h2 className="text-sm font-bold text-white/80">{container.title}</h2>}
                        {container.subtitle && <p className="text-xs text-[var(--muted)]">{container.subtitle}</p>}
                    </div>
                )}
                <GenericLayout 
                    structure={container.structure} 
                    slotMap={container.slotMap} 
                    manifest={manifest} 
                    baselineManifest={baselineManifest}
                    parameters={parameters} 
                    onParameterChange={onParameterChange} 
                    crossFilterSource={crossFilterSource}
                    onShowContextMenu={onShowContextMenu}
                />
            </div>
        );
    }

    const isRow = container.containerType.toUpperCase() === 'ROW' || container.containerType.toUpperCase() === 'BOX';
    const visuals = container.visuals || [];
    
    return (
        <div className={clsx(
            "w-full flex gap-6",
            isRow ? "flex-row flex-wrap" : "flex-col"
        )}>
            {visuals.map(vName => (
                <div key={vName} className={isRow ? "flex-1 min-w-[200px]" : "w-full"}>
                    <RenderObject 
                        name={vName} 
                        manifest={manifest} 
                        baselineManifest={baselineManifest}
                        parameters={parameters}
                        onParameterChange={onParameterChange}
                        crossFilterSource={crossFilterSource}
                        onShowContextMenu={onShowContextMenu}
                    />
                </div>
            ))}
        </div>
    );
};

const VisualCard: React.FC<{ 
    visual: VisualManifest,
    manifest: ReportManifest,
    baselineManifest: ReportManifest | null,
    parameters: Record<string, string | null>,
    onParameterChange: (name: string, value: string, source?: string) => void,
    crossFilterSource: string | null,
    onShowContextMenu: (val: any) => void
}> = ({ visual, manifest, baselineManifest, parameters, onParameterChange, crossFilterSource, onShowContextMenu }) => {
    const type = visual.visualType.toUpperCase();
    const isFilter = ['SLICER', 'DATEPICKER', 'SLIDER', 'MULTISELECT', 'SEARCH'].includes(type);
    const isDimmed = crossFilterSource && crossFilterSource !== visual.name && !isFilter;
    const isSource = crossFilterSource === visual.name;
    
    const cardStyle: React.CSSProperties = {};
    if (visual.styles?.HEIGHT) cardStyle.height = visual.styles.HEIGHT;
    if (visual.styles?.WIDTH) cardStyle.width = visual.styles.WIDTH;
    if (visual.styles?.MAX_HEIGHT) cardStyle.maxHeight = visual.styles.MAX_HEIGHT;
    if (visual.styles?.MIN_HEIGHT) cardStyle.minHeight = visual.styles.MIN_HEIGHT;

    return (
        <div 
            style={cardStyle}
            className={clsx(
                "w-full group/card flex flex-col rounded-3xl border transition-all duration-700 shadow-xl overflow-hidden backdrop-blur-sm",
                isFilter ? "border-indigo-500/20 hover:border-indigo-500/40 bg-[var(--bg-darker,#050507)]" : "border-[var(--border)] hover:border-indigo-500/30 bg-[var(--bg-darker,#050507)]",
                isDimmed && "opacity-40 grayscale-[0.7] scale-[0.99] pointer-events-none",
                isSource && "border-indigo-500 shadow-[0_0_30px_rgba(99,102,241,0.2)]"
            )}
        >
            {/* Component Header */}
            <div className="px-6 py-4 flex items-center justify-between border-b border-[var(--border)]/30">
                <h3 className="text-sm font-bold tracking-wide text-white/80 flex items-center gap-2">
                    {visual.name}
                </h3>
                <span className="text-[10px] font-bold text-[var(--muted)] uppercase tracking-[0.2em]">{type}</span>
            </div>

            <div className={clsx(
                "p-6 flex-1 relative overflow-hidden",
                isFilter ? "min-h-[auto]" : "min-h-[150px]"
            )}>
                {visual.error ? (
                    <div className="h-full flex flex-col items-center justify-center text-center p-8 space-y-3 opacity-60">
                         <AlertCircle className="text-red-400" size={32} />
                         <p className="text-xs text-red-400/80 font-mono leading-relaxed">{visual.error}</p>
                    </div>
                ) : (
                    <div className="h-full w-full">
                         {type === 'TABLE' && <ReportTable visual={visual} onShowContextMenu={(x, y, data) => onShowContextMenu({ x, y, visual, rowData: data })} />}
                          {type === 'CARD' && <ReportCard visual={visual} />}
                         {type === 'TEXT' && <ReportText visual={visual} />}
                         {type === 'SLICER' && (
                             <ReportSlicer 
                                 visual={visual} 
                                 parameters={parameters} 
                                 onParameterChange={onParameterChange} 
                             />
                         )}
                         {type === 'IMAGE' && <ReportImage visual={visual} />}
                         {type === 'MULTISELECT' && (
                             <ReportMultiSelect 
                                 visual={visual} 
                                 parameters={parameters} 
                                 onParameterChange={onParameterChange} 
                             />
                         )}
                         {['BAR', 'LINE', 'PIE', 'DONUT', 'SCATTER', 'HBAR', 'HORIZONTALBAR', 'BOXPLOT', 'TREEMAP', 'HEATMAP', 'COMBO', 'GAUGE', 'FUNNEL', 'WATERFALL', 'BUBBLE', 'RADAR', 'CANDLESTICK'].includes(type) && (
                             <ReportChart 
                                visual={visual} 
                                onParameterChange={onParameterChange}
                                baselineManifest={baselineManifest}
                                currentManifest={manifest}
                                onShowContextMenu={(x, y, data) => onShowContextMenu({ x, y, visual, rowData: data })}
                             />
                         )}
                         {type === 'MAP' && <ReportMapPlaceholder visual={visual} />}
                    </div>
                )}
            </div>
        </div>
    );
};

const ReportChart: React.FC<{ 
    visual: VisualManifest, 
    onParameterChange: (name: string, value: string, source?: string) => void,
    baselineManifest: ReportManifest | null,
    currentManifest: ReportManifest,
    onShowContextMenu: (x: number, y: number, rowData?: any[]) => void
}> = ({ visual, onParameterChange, baselineManifest, currentManifest, onShowContextMenu }) => {
    const chartRef = useRef<HTMLDivElement>(null);
    const chartInstance = useRef<echarts.ECharts | null>(null);
    const isDark = true; 

    useEffect(() => {
        if (!chartRef.current || !visual.chartConfig) return;

        if (!chartInstance.current) {
            chartInstance.current = echarts.init(chartRef.current, isDark ? 'dark' : undefined, {
                renderer: 'canvas'
            });
        }

        try {
            const option = typeof visual.chartConfig === 'string'
                ? JSON.parse(visual.chartConfig)
                : JSON.parse(JSON.stringify(visual.chartConfig));

            // Highlighting logic: Merge Baseline totals with current Filtered values
            const hasParams = currentManifest.parameters && Object.values(currentManifest.parameters).some(v => v);
            if (hasParams && baselineManifest) {
                const baselineVisual = baselineManifest.visuals.find(v => v.name === visual.name);
                const type = visual.visualType.toUpperCase();
                if (baselineVisual && (type === 'BAR' || type === 'HBAR' || type === 'HORIZONTALBAR' || type === 'LINE')) {
                    const xCol = (visual.options?.['mapping:x'] || visual.columns[0]);
                    const yCol = (visual.options?.['mapping:y'] || visual.columns[1]);
                    const xIdx = visual.columns.indexOf(xCol);
                    const yIdx = visual.columns.indexOf(yCol);

                    if (xIdx >= 0 && yIdx >= 0) {
                        const baselineMap: Record<string, number> = {};
                        baselineVisual.rows.forEach(r => { baselineMap[String(r[xIdx])] = parseFloat(r[yIdx] as any) || 0; });
                        
                        const currentMap: Record<string, number> = {};
                        visual.rows.forEach(r => { currentMap[String(r[xIdx])] = parseFloat(r[yIdx] as any) || 0; });

                        if (option.series && option.series.length > 0) {
                            const primarySeries = option.series[0];
                            const categories = option.xAxis?.data || option.yAxis?.data || [];
                            
                            const filteredData: number[] = [];
                            const remainingData: number[] = [];

                            categories.forEach((cat: any) => {
                                const total = baselineMap[String(cat)] || 0;
                                const filtered = currentMap[String(cat)] || 0;
                                filteredData.push(filtered);
                                remainingData.push(Math.max(0, total - filtered));
                            });

                            primarySeries.data = filteredData;
                            primarySeries.stack = 'highlight';
                            primarySeries.name = 'Filtered';

                            const remainingSeries = JSON.parse(JSON.stringify(primarySeries));
                            remainingSeries.name = 'Total';
                            remainingSeries.data = remainingData;
                            remainingSeries.itemStyle = { opacity: 0.15, color: '#888' };
                            remainingSeries.emphasis = { disabled: true };
                            remainingSeries.tooltip = { show: false };
                            option.series.push(remainingSeries);
                            option.series.forEach((s: any) => {
                                s.stack = 'highlight';
                                if (!s.emphasis) s.emphasis = {};
                                s.emphasis.focus = 'none'; // Disable hover-dimming per user request
                            });
                        }
                    }
                }
            } else {
                // If not filtered, still disable hover-dimming for consistency
                if (option.series) {
                    option.series.forEach((s: any) => {
                        if (!s.emphasis) s.emphasis = {};
                        s.emphasis.focus = 'none';
                    });
                }
            }

            // BUBBLE/MAP markers...
            delete option.__bubbleSymbolSize;
            delete option.__mapKey;
            delete option.__matchBy;
            delete option.__mapFile;

            option.backgroundColor = 'transparent';
            chartInstance.current.setOption(option, true);

            // Handle Interaction Events
            let lastHoveredRow: any = null;
            chartInstance.current.on('mousemove', (params: any) => {
                const idx = params.dataIndex != null ? params.dataIndex : -1;
                lastHoveredRow = (visual.rows || [])[idx] || null;
            });

            chartInstance.current.on('click', (params: any) => {
                const crossFilter = visual.actions?.find(a => a.type === 'CROSS_FILTER');
                if (crossFilter) {
                    const val = params.name || (Array.isArray(params.data) ? params.data[0] : params.data);
                    onParameterChange(crossFilter.parameterName, String(val), visual.name);
                    
                    // Highlight source bar, dim others
                    chartInstance.current?.dispatchAction({ type: 'downplay' });
                    chartInstance.current?.dispatchAction({
                        type: 'highlight',
                        seriesIndex: params.seriesIndex,
                        dataIndex: params.dataIndex
                    });
                } else {
                    const clickActions = visual.actions?.filter(a => a.trigger === 'ON_CLICK');
                    const idx = params.dataIndex != null ? params.dataIndex : -1;
                    const rowData = (visual.rows || [])[idx] || [];
                    clickActions?.forEach(action => {
                        // Handle drill down or other click actions
                        (window as any).vscode?.postMessage({ 
                            type: 'executeAction', 
                            action, 
                            rowData, 
                            columns: visual.columns 
                        });
                    });
                }
            });

            chartRef.current.addEventListener('contextmenu', (e: MouseEvent) => {
                const hasDrillDown = visual.actions?.some(a => a.type === 'DRILL_DOWN');
                if (hasDrillDown) {
                    e.preventDefault();
                    onShowContextMenu(e.clientX, e.clientY, lastHoveredRow);
                }
            });
        } catch (e) {
            console.error('Failed to render chart:', e);
        }

        const handleResize = () => chartInstance.current?.resize();
        window.addEventListener('resize', handleResize);

        return () => {
            window.removeEventListener('resize', handleResize);
            chartInstance.current?.dispose();
            chartInstance.current = null;
        };
    }, [visual.chartConfig, isDark, baselineManifest, currentManifest.parameters]);

    return (
        <div 
            ref={chartRef} 
            className="w-full h-full min-h-[350px]" 
            onContextMenu={e => {
                const hasDrill = visual.actions?.some(a => a.type === 'DRILL_DOWN');
                if (hasDrill) {
                    e.preventDefault();
                    onShowContextMenu(e.clientX, e.clientY);
                }
            }}
        />
    );
};

const ReportTable: React.FC<{ 
    visual: VisualManifest,
    onShowContextMenu: (x: number, y: number, rowData?: any[]) => void
}> = ({ visual, onShowContextMenu }) => {
    const grid = (visual.options?.GRID || 'HEADER').toUpperCase();

    return (
        <div className="w-full h-full relative">
            <div
                className="w-full h-full overflow-auto rounded-xl border border-[var(--border)]/30 bg-black/10 custom-scrollbar"
                onContextMenu={e => { 
                    e.preventDefault(); 
                    const tr = (e.target as HTMLElement).closest('tr');
                    const idx = tr ? Array.from((tr.parentElement as HTMLTableSectionElement)?.rows || []).indexOf(tr) : -1;
                    onShowContextMenu(e.clientX, e.clientY, idx >= 0 ? visual.rows[idx] : undefined); 
                }}
            >
                <table className={clsx(
                    "w-full text-left text-xs border-collapse",
                    (grid === 'ALL' || grid === 'BOTH' || grid === 'OUTSIDE') && "border border-[var(--border)]/40"
                )}>
                    <thead className={clsx(
                        "sticky top-0 bg-[var(--bg-darker)] shadow-md z-10",
                        (grid === 'HEADER' || grid === 'ALL' || grid === 'ROWS' || grid === 'BOTH') && "border-b border-[var(--border)]"
                    )}>
                        <tr>
                            {visual.columns.map((col, ci) => (
                                <th key={col} className={clsx(
                                    "px-4 py-3 font-bold text-indigo-300 uppercase tracking-widest text-[10px]",
                                    (grid === 'ALL' || grid === 'COLS' || grid === 'BOTH') && ci < visual.columns.length - 1 && "border-r border-[var(--border)]/30",
                                    (grid === 'HEADER' || grid === 'ALL' || grid === 'ROWS' || grid === 'BOTH') && "border-b border-[var(--border)]",
                                    (grid === 'LEFT' && ci === 0) && "border-l border-[var(--border)]",
                                    (grid === 'RIGHT' && ci === visual.columns.length - 1) && "border-r border-[var(--border)]"
                                )}>
                                    {col}
                                </th>
                            ))}
                        </tr>
                    </thead>
                    <tbody className={clsx(
                        (grid === 'ROWS' || grid === 'ALL' || grid === 'BOTH') ? "divide-y divide-[var(--border)]/40" : ""
                    )}>
                        {visual.rows.slice(0, 100).map((row, i) => {
                            const rowStyle = visual.rowStyles?.[i];
                            return (
                                <tr 
                                    key={i} 
                                    className="hover:bg-white/5 transition-colors"
                                    style={rowStyle ? { backgroundColor: rowStyle + '33' } : {}}
                                >
                                    {row.map((cell, ci) => (
                                        <td key={ci} className={clsx(
                                            "px-4 py-2.5 font-mono text-[var(--muted)]",
                                            (grid === 'ALL' || grid === 'COLS' || grid === 'BOTH') && ci < visual.columns.length - 1 && "border-r border-[var(--border)]/20",
                                            (grid === 'LEFT' && ci === 0) && "border-l border-[var(--border)]",
                                            (grid === 'RIGHT' && ci === visual.columns.length - 1) && "border-r border-[var(--border)]"
                                        )}>
                                            {cell !== null ? String(cell) : ''}
                                        </td>
                                    ))}
                                </tr>
                            );
                        })}
                        {visual.rows.length > 100 && (
                            <tr>
                                <td colSpan={visual.columns.length} className="px-4 py-3 text-center text-[var(--muted)] italic opacity-50">
                                    Showing first 100 rows of {visual.rows.length}...
                                </td>
                            </tr>
                        )}
                    </tbody>
                    {visual.summaryData && (
                        <tfoot className="sticky bottom-0 bg-[var(--bg-darker)] shadow-[0_-4px_6px_rgba(0,0,0,0.3)] z-10 border-t border-indigo-500/30">
                            {visual.summaryData.grandTotals && (
                                <tr className="bg-indigo-500/5">
                                    {visual.columns.map((col, ci) => (
                                        <td key={ci} className="px-4 py-3 font-black text-indigo-400 text-xs border-t border-indigo-500/20">
                                            {visual.summaryData!.grandTotals![col] ?? ''}
                                        </td>
                                    ))}
                                </tr>
                            )}
                            {visual.summaryData.aggregates.length > 0 && (
                                <tr className="bg-black/20">
                                    <td colSpan={visual.columns.length} className="px-4 py-2">
                                        <div className="flex flex-wrap gap-x-6 gap-y-2">
                                            {visual.summaryData.aggregates.map((agg, ai) => (
                                                <div key={ai} className="flex items-center gap-2">
                                                    <span className="text-[9px] font-bold text-[var(--muted)] uppercase tracking-widest">{agg.alias || `${agg.aggregate}(${agg.column})`}</span>
                                                    <span className="text-xs font-mono text-indigo-300">{agg.value}</span>
                                                </div>
                                            ))}
                                        </div>
                                    </td>
                                </tr>
                            )}
                        </tfoot>
                    )}
                </table>
            </div>
        </div>
    );
};

const ReportCard: React.FC<{ visual: VisualManifest }> = ({ visual }) => {
    const opts = visual.options || {};
    const labelColName = opts['mapping:label'];
    const valueColName = opts['mapping:value'];

    const labelIdx = labelColName ? visual.columns.indexOf(labelColName) : -1;
    const valueIdx = valueColName ? visual.columns.indexOf(valueColName) : 0;

    // Prefer the data value from the LABEL mapping column; fall back to column name
    const labelData = labelIdx >= 0 ? visual.rows[0]?.[labelIdx] : null;
    const label = labelData != null ? String(labelData) : (visual.columns[0] || visual.name);

    const value = visual.rows[0]?.[valueIdx];

    return (
        <div className="h-full flex flex-col justify-center py-6 px-4">
            <span className="text-[10px] font-bold uppercase tracking-[0.3em] text-indigo-400/70 mb-2">{label}</span>
            <span className="text-4xl font-black text-[var(--text)] tracking-tight drop-shadow-2xl">
                {value != null ? String(value) : 'No Data'}
            </span>
        </div>
    );
};

const ReportSlicer: React.FC<{ 
    visual: VisualManifest,
    parameters: Record<string, string | null>,
    onParameterChange: (name: string, value: string) => void
}> = ({ visual, parameters, onParameterChange }) => {
    // Slicers use the MAPPINGS(VALUE = col) to find the options
    const valueCol = visual.options['mapping:value'] || visual.columns[0];
    const colIdx = visual.columns.indexOf(valueCol);
    const options = useMemo(() => {
        const uniqueValues = new Set<string>();
        visual.rows.forEach(r => {
            const val = r[colIdx];
            if (val !== null) uniqueValues.add(val);
        });
        return Array.from(uniqueValues).sort();
    }, [visual, colIdx]);

    // Find the bound parameter from ACTIONS
    const setParamAction = visual.actions.find(a => a.type === 'SET_PARAMETER' && a.trigger === 'ON_CHANGE');
    const boundParam = setParamAction?.parameterName;
    
    // Controlled value from parameter state, falling back to defaultValue
    const currentValue = useMemo(() => {
        if (!boundParam) return visual.defaultValue || '';
        if (parameters[boundParam] !== undefined) return parameters[boundParam] || '';
        
        // Fallback matching for @ prefix mismatches
        if (boundParam.startsWith('@') && parameters[boundParam.substring(1)] !== undefined) 
            return parameters[boundParam.substring(1)] || '';
        if (!boundParam.startsWith('@') && parameters['@' + boundParam] !== undefined) 
            return parameters['@' + boundParam] || '';
            
        return visual.defaultValue || '';
    }, [boundParam, parameters, visual.defaultValue]);

    const handleChange = (e: React.ChangeEvent<HTMLSelectElement>) => {
        const val = e.target.value;
        if (boundParam) {
            onParameterChange(boundParam, val);
        }
    };

    // Whether defaultValue is a synthetic option not present in the data rows
    const defaultIsInOptions = visual.defaultValue != null && options.includes(visual.defaultValue);

    return (
        <div className="w-full">
            <select
                value={currentValue}
                onChange={handleChange}
                className="w-full border border-white/10 rounded-xl px-4 py-2.5 text-xs focus:outline-none focus:ring-2 focus:ring-indigo-500/40 transition-all cursor-pointer appearance-none shadow-inner"
                style={{ backgroundColor: '#1e1e2e', color: '#e2e8f0' }}
            >
                {!visual.defaultValue && <option value="" style={{ backgroundColor: '#1e1e2e', color: '#e2e8f0' }}>Select...</option>}
                {visual.defaultValue && !defaultIsInOptions && (
                    <option value={visual.defaultValue} style={{ backgroundColor: '#1e1e2e', color: '#e2e8f0' }}>{visual.defaultValue}</option>
                )}
                {options.map(opt => (
                    <option key={opt} value={opt} style={{ backgroundColor: '#1e1e2e', color: '#e2e8f0' }}>{opt}</option>
                ))}
            </select>
            {boundParam && (
                <div className="mt-2 flex items-center justify-between px-1">
                    <span className="text-[9px] font-bold text-indigo-400/50 uppercase tracking-widest">Bindings</span>
                    <span className="text-[9px] font-mono text-indigo-400/50">{boundParam}</span>
                </div>
            )}
        </div>
    );
};
const ReportMultiSelect: React.FC<{ 
    visual: VisualManifest,
    parameters: Record<string, string | null>,
    onParameterChange: (name: string, value: string) => void
}> = ({ visual, parameters, onParameterChange }) => {
    const valueCol = visual.options['mapping:value'] || visual.columns[0];
    const colIdx = visual.columns.indexOf(valueCol);
    const options = useMemo(() => {
        const uniqueValues = new Set<string>();
        visual.rows.forEach(r => {
            const val = r[colIdx];
            if (val !== null) uniqueValues.add(val);
        });
        return Array.from(uniqueValues).sort();
    }, [visual, colIdx]);

    const setParamAction = visual.actions.find(a => a.type === 'SET_PARAMETER' && a.trigger === 'ON_CHANGE');
    const boundParam = setParamAction?.parameterName;
    
    const currentValues = useMemo(() => {
        if (!boundParam) return [];
        const val = parameters[boundParam] || '';
        return val.split(',').map(v => v.trim()).filter(Boolean);
    }, [boundParam, parameters]);

    const handleToggle = (opt: string) => {
        if (!boundParam) return;
        let newValues;
        if (currentValues.includes(opt)) {
            newValues = currentValues.filter(v => v !== opt);
        } else {
            newValues = [...currentValues, opt];
        }
        onParameterChange(boundParam, newValues.join(','));
    };

    return (
        <div className="w-full flex flex-col gap-2">
            <div className="flex flex-col gap-1.5 max-h-[200px] overflow-y-auto pr-2 custom-scrollbar border border-[var(--border)] rounded-xl p-3 bg-current/5">
                {options.map(opt => (
                    <label key={opt} className="flex items-center gap-3 cursor-pointer group py-1">
                        <div className="relative flex items-center justify-center">
                            <input
                                type="checkbox"
                                checked={currentValues.includes(opt)}
                                onChange={() => handleToggle(opt)}
                                className="peer appearance-none w-4 h-4 rounded border border-[var(--border)] bg-current/5 checked:bg-indigo-500 checked:border-indigo-500 transition-all cursor-pointer"
                            />
                            <CheckSquare className="absolute w-3 h-3 text-[var(--text)] opacity-0 peer-checked:opacity-100 transition-opacity pointer-events-none" />
                        </div>
                        <span className={clsx(
                            "text-xs transition-colors",
                            currentValues.includes(opt) ? "text-indigo-300 font-bold" : "text-[var(--muted)] group-hover:text-[var(--text)]"
                        )}>
                            {opt}
                        </span>
                    </label>
                ))}
            </div>
            {boundParam && (
                <div className="mt-1 flex items-center justify-between px-1">
                    <span className="text-[9px] font-bold text-indigo-400/50 uppercase tracking-widest">Multi-Select</span>
                    <span className="text-[9px] font-mono text-indigo-400/50 italic">{currentValues.length} selected</span>
                </div>
            )}
        </div>
    );
};

const ReportImage: React.FC<{ visual: VisualManifest }> = ({ visual }) => {
    const src = visual.options['SRC'] || visual.options['source'] || '';
    const fit = visual.options['FIT'] || visual.options['object-fit'] || 'contain';
    
    if (!src) return <div className="h-full flex items-center justify-center text-[var(--muted)] text-xs italic">No image source provided.</div>;

    return (
        <div className="h-full w-full flex items-center justify-center overflow-hidden rounded-xl">
            <img 
                src={src} 
                alt={visual.name}
                className="max-w-full max-h-full transition-transform duration-500 group-hover:scale-105"
                style={{ objectFit: fit as any }}
            />
        </div>
    );
};

const SimpleMarkdown: React.FC<{ text: string }> = ({ text }) => {
    const html = useMemo(() => {
        // Basic escaping
        let h = text
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            // Bold/Italic/Code
            .replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>')
            .replace(/\*(.+?)\*/g,     '<em>$1</em>')
            .replace(/`(.+?)`/g,       '<code>$1</code>')
            // Headers
            .replace(/^### (.+)$/gm,   '<h3 class="text-lg font-bold mt-4 mb-2 text-[var(--text)]">$1</h3>')
            .replace(/^## (.+)$/gm,    '<h2 class="text-xl font-bold mt-6 mb-3 border-b border-[var(--border)] pb-2 text-[var(--text)]">$1</h2>')
            .replace(/^# (.+)$/gm,     '<h1 class="text-2xl font-black mt-8 mb-4 tracking-tight text-[var(--text)]">$1</h1>');

        // Table support
        const lines = h.split('\n');
        let inTable = false;
        let tableLines: string[] = [];
        let resultLines: string[] = [];

        const flushTable = () => {
            if (tableLines.length === 0) return;
            let tableHtml = '<div class="my-4 overflow-x-auto rounded-xl border border-[var(--border)] bg-current/5"><table class="w-full text-left text-sm border-collapse">';
            tableLines.forEach((line, idx) => {
                if (line.includes('---') && idx === 1) return;
                const cells = line.split('|').filter((_, i, a) => i > 0 && i < a.length - 1);
                const tag = idx === 0 ? 'th' : 'td';
                const className = idx === 0 
                    ? "px-4 py-3 bg-current/10 font-bold text-indigo-300 uppercase tracking-widest text-[10px] border-b border-[var(--border)]" 
                    : "px-4 py-2.5 border-b border-current/5 font-mono text-[var(--text)] opacity-70";
                
                tableHtml += '<tr>' + cells.map(c => `<${tag} class="${className}">${c.trim()}</${tag}>`).join('') + '</tr>';
            });
            tableHtml += '</table></div>';
            resultLines.push(tableHtml);
            tableLines = [];
        };

        lines.forEach(line => {
            if (line.trim().startsWith('|')) {
                inTable = true;
                tableLines.push(line);
            } else {
                if (inTable) flushTable();
                inTable = false;
                resultLines.push(line);
            }
        });
        if (inTable) flushTable();

        return resultLines.join('<br/>');
    }, [text]);

    return <div className="markdown-content" dangerouslySetInnerHTML={{ __html: html }} />;
};

const ReportText: React.FC<{ visual: VisualManifest }> = ({ visual }) => {
    // TEXT visuals usually store their content in defaultValue (parsed from VALUE property)
    const text = visual.defaultValue || visual.options['VALUE'] || visual.options['value'] || '';

    return (
        <div className="w-full h-full min-h-[100px] overflow-y-auto text-[var(--text)] text-sm opacity-90 leading-relaxed font-sans bg-transparent custom-scrollbar pr-4">
            <SimpleMarkdown text={text} />
        </div>
    );
};

const ReportMapPlaceholder: React.FC<{ visual: VisualManifest }> = ({ visual }) => {
    const mapKey = (visual.options?.['MAP_NAME'] || visual.options?.['map_name'] || '').toUpperCase();
    const mode   = (visual.options?.['MODE'] || 'CHOROPLETH').toUpperCase();
    return (
        <div className="h-full w-full flex flex-col items-center justify-center gap-3 text-center p-8 opacity-70">
            <span className="text-4xl select-none">🗺</span>
            <p className="text-sm font-bold text-[var(--muted)] tracking-wide">Map preview unavailable</p>
            <p className="text-xs text-[var(--muted)]/70 max-w-[280px] leading-relaxed">
                {mapKey ? `${mapKey} · ${mode}` : 'MAP'} charts require the HTTP server to load GeoJSON.
                Open the report in <span className="font-mono text-indigo-400">Report Portal</span> or run the
                script with <span className="font-mono text-indigo-400">--ui</span> to see the live map.
            </p>
        </div>
    );
};

const ContextMenu: React.FC<{ 
    x: number, 
    y: number, 
    visual: VisualManifest, 
    rowData?: any[], 
    onClose: () => void,
    onAction: (action: any) => void
}> = ({ x, y, visual, rowData: _rowData, onClose, onAction }) => {
    useEffect(() => {
        const handle = () => onClose();
        window.addEventListener('click', handle, { capture: true });
        return () => window.removeEventListener('click', handle, { capture: true });
    }, [onClose]);

    const drillDowns = (visual.actions || []).filter(a => a.type === 'DRILL_DOWN');

    const exportCsv = () => {
        const escape = (v: any) => '"' + String(v ?? '').replace(/"/g, '""') + '"';
        const lines = [visual.columns.map(escape).join(',')];
        visual.rows.forEach(r => lines.push(visual.columns.map((_, i) => escape(r[i])).join(',')));
        const blob = new Blob([lines.join('\r\n')], { type: 'text/csv' });
        const url  = URL.createObjectURL(blob);
        const a    = document.createElement('a');
        a.href     = url;
        a.download = `${visual.name}.csv`;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
    };

    return (
        <div 
            className="fixed z-[9999] bg-[var(--bg-darker)] border border-[var(--border)] rounded-xl shadow-2xl py-2 min-w-[200px] backdrop-blur-xl animate-in fade-in zoom-in duration-150"
            style={{ left: x, top: y }}
            onClick={e => e.stopPropagation()}
        >
            {drillDowns.map((action, i) => (
                <button 
                    key={i}
                    onClick={() => { onAction(action); onClose(); }}
                    className="w-full text-left px-4 py-2 text-sm text-[var(--text)] opacity-80 hover:bg-indigo-500/20 hover:text-white transition-colors flex items-center gap-2"
                >
                    <RefreshCw size={14} className="opacity-50" />
                    <span>Drill down to <b>{action.targetVisual || action.targetPage || 'Details'}</b></span>
                </button>
            ))}
            {drillDowns.length > 0 && <div className="h-px bg-[var(--border)] my-1" />}
            <button 
                onClick={() => { exportCsv(); onClose(); }}
                className="w-full text-left px-4 py-2 text-sm text-[var(--text)] opacity-80 hover:bg-current/5 transition-colors flex items-center gap-2"
            >
                <Download size={14} className="opacity-50" />
                <span>Export to CSV</span>
            </button>
        </div>
    );
};
