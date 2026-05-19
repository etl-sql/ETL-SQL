import React, { useEffect, useRef, useState, useMemo } from 'react';
import * as echarts from 'echarts';
import { RefreshCw, AlertCircle, Calendar, FileText, File, Download, ExternalLink } from 'lucide-react';
import type { ReportManifest, VisualManifest, ContainerManifest, PageManifest, ReportAction } from '../types';
import { clsx } from 'clsx';

type ContextMenuArgs = { x: number; y: number; visual: VisualManifest; rowData?: unknown[] };

interface VsCodeWindow {
    vscode?: { postMessage: (message: unknown) => void };
}

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
    const [baselineManifest] = useState<ReportManifest>(() => manifest);

    // Local parameter state initialized from manifest defaults
    const [parameters, setParameters] = useState<Record<string, string | null>>(manifest.parameters || {});
    const [isRefreshing, setIsRefreshing] = useState(false);
    const [contextMenu, setContextMenu] = useState<ContextMenuArgs | null>(null);
    const debounceTimer = useRef<ReturnType<typeof setTimeout> | null>(null);

    const activePage = useMemo(() =>
        manifest.pages.find(p => p.name === activePageName) || manifest.pages[0],
    [manifest, activePageName]);

    // Reset isRefreshing and re-sync parameters when manifest changes (refresh completed)
    useEffect(() => {
        // eslint-disable-next-line react-hooks/set-state-in-effect
        setIsRefreshing(false);
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

    return (
        <div className="flex-1 flex flex-col min-h-0 bg-[var(--bg)] text-[var(--text)] overflow-hidden font-sans">
            {/* Header */}
            <header className="px-4 py-2.5 border-b border-[var(--border)] bg-[var(--bg-darker)] flex items-center justify-between gap-4 shrink-0">
                <div className="flex flex-col min-w-0">
                    <h1 className="text-[13px] font-semibold text-[var(--text)] truncate">
                        {manifest.title || 'Untitled Report'}
                    </h1>
                    {manifest.description && (
                        <p className="text-[11px] text-[var(--muted)] truncate mt-0.5">{manifest.description}</p>
                    )}
                </div>

                <div className="flex items-center gap-2 shrink-0">
                    <div className={clsx(
                        "flex items-center gap-1.5 px-2 py-1 border text-[11px] transition-colors",
                        isRefreshing 
                            ? "bg-[var(--vscode-progressBar-background,#0e639c)]/10 border-[var(--vscode-progressBar-background,#0e639c)]/40 text-[var(--vscode-progressBar-background,#0e639c)]"
                            : "bg-[var(--vscode-badge-background,#4d4d4d)]/10 border-[var(--border)] text-[var(--muted)]"
                    )}>
                        {isRefreshing ? <RefreshCw size={12} className="animate-spin" /> : <Calendar size={12} />}
                        <span>{isRefreshing ? 'Refreshing' : `Built ${new Date(manifest.builtAt).toLocaleTimeString()}`}</span>
                    </div>
                    
                    <div className="flex items-center border border-[var(--border)] bg-[var(--vscode-toolbar-hoverBackground,transparent)]/20">
                        <button
                            onClick={() => (window as Window & VsCodeWindow).vscode?.postMessage({ type: 'serve' })}
                            className="flex items-center gap-1.5 px-2 py-1 text-[11px] text-[var(--text)] hover:bg-[var(--vscode-toolbar-hoverBackground,rgba(90,93,94,0.31))]"
                            title="Open interactive report"
                        >
                            <ExternalLink size={13} />
                            <span>Open</span>
                        </button>
                        <div className="w-px h-4 bg-[var(--border)]" />
                        <button 
                            onClick={() => onExport?.('markdown')}
                            className="p-1.5 text-[var(--text)] hover:bg-[var(--vscode-toolbar-hoverBackground,rgba(90,93,94,0.31))]"
                            title="Export to Markdown"
                        >
                            <FileText size={14} />
                        </button>
                        <button 
                            onClick={() => onExport?.('pdf')}
                            className="p-1.5 text-[var(--text)] hover:bg-[var(--vscode-toolbar-hoverBackground,rgba(90,93,94,0.31))]"
                            title="Export to PDF"
                        >
                            <File size={14} />
                        </button>
                        <button 
                            onClick={() => onExport?.('text')}
                            className="p-1.5 text-[var(--text)] hover:bg-[var(--vscode-toolbar-hoverBackground,rgba(90,93,94,0.31))]"
                            title="Export to Text"
                        >
                            <Download size={14} />
                        </button>
                    </div>

                    <button 
                        onClick={() => {
                            setIsRefreshing(true);
                            onRefresh(parameters);
                        }}
                        disabled={isRefreshing}
                        className={clsx(
                            "p-1.5 border transition-colors",
                            isRefreshing 
                                ? "bg-[var(--vscode-toolbar-hoverBackground,rgba(90,93,94,0.18))] text-[var(--muted)] border-[var(--border)] opacity-50 cursor-not-allowed"
                                : "bg-transparent hover:bg-[var(--vscode-toolbar-hoverBackground,rgba(90,93,94,0.31))] text-[var(--text)] border-[var(--border)]"
                        )}
                        title="Refresh Report"
                    >
                        <RefreshCw size={14} className={clsx(isRefreshing && "animate-spin")} />
                    </button>
                </div>
            </header>

            {/* Navigation Tabs (if defined or multiple pages) */}
            {(manifest.navigations?.length || manifest.pages.length > 1) && (
                <div className={clsx(
                    "px-2 shrink-0 flex gap-1 overflow-x-auto no-scrollbar py-1",
                    "border-b border-[var(--border)] bg-[var(--vscode-editorGroupHeader-tabsBackground,var(--bg-darker))]"
                )}>
                    {/* Render Explicit Navigations if any */}
                    {manifest.navigations?.map(nav => (
                        <div key={nav.name} className={clsx("flex gap-1", nav.orientation === 'VERTICAL' ? "flex-col" : "flex-row")}>
                            {nav.pages.map(pageName => (
                                <button
                                    key={pageName}
                                    onClick={() => setActivePageName(pageName)}
                                    className={clsx(
                                        "px-2.5 py-1 text-[12px] whitespace-nowrap border border-transparent",
                                        activePageName === pageName 
                                            ? "bg-[var(--vscode-list-activeSelectionBackground,#094771)] text-[var(--vscode-list-activeSelectionForeground,var(--text))] border-[var(--vscode-focusBorder,#007fd4)]"
                                            : "text-[var(--muted)] hover:bg-[var(--vscode-toolbar-hoverBackground,rgba(90,93,94,0.31))] hover:text-[var(--text)]"
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
                                "px-2.5 py-1 text-[12px] whitespace-nowrap border border-transparent",
                                activePageName === page.name 
                                    ? "bg-[var(--vscode-list-activeSelectionBackground,#094771)] text-[var(--vscode-list-activeSelectionForeground,var(--text))] border-[var(--vscode-focusBorder,#007fd4)]"
                                    : "text-[var(--muted)] hover:bg-[var(--vscode-toolbar-hoverBackground,rgba(90,93,94,0.31))] hover:text-[var(--text)]"
                            )}
                        >
                            {page.name}
                        </button>
                    ))}
                </div>
            )}

            {/* Content Area */}
            <main className="flex-1 overflow-auto p-3 custom-scrollbar relative">
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
    onShowContextMenu: (val: ContextMenuArgs) => void
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
    onShowContextMenu: (val: ContextMenuArgs) => void
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
        <div className="grid gap-3" style={gridStyle}>
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
    onShowContextMenu: (val: ContextMenuArgs) => void
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
        <div className="w-full p-3 border border-[var(--vscode-inputValidation-errorBorder,#be1100)] flex items-center gap-2 text-[var(--color-err)] bg-[var(--vscode-inputValidation-errorBackground,transparent)]">
            <AlertCircle size={16} />
            <div className="flex flex-col">
                <span className="text-[12px] font-semibold">Reference error</span>
                <span className="text-[12px]">Object "{name}" not found in manifest.</span>
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
    onShowContextMenu: (val: ContextMenuArgs) => void
}> = ({ container, manifest, baselineManifest, parameters, onParameterChange, crossFilterSource, onShowContextMenu }) => {
    if (container.structure && container.slotMap) {
        return (
            <div className="w-full flex flex-col gap-2">
                {(container.title || container.subtitle) && (
                    <div className="flex flex-col gap-1 px-1">
                        {container.title && <h2 className="text-[13px] font-semibold text-[var(--text)]">{container.title}</h2>}
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
            "w-full flex gap-3",
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
    onShowContextMenu: (val: ContextMenuArgs) => void
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
                "w-full group/card flex flex-col border overflow-hidden bg-[var(--vscode-editor-background,var(--bg))]",
                isFilter ? "border-[var(--vscode-focusBorder,#007fd4)]/60" : "border-[var(--border)]",
                isDimmed && "opacity-45 grayscale pointer-events-none",
                isSource && "border-[var(--vscode-focusBorder,#007fd4)]"
            )}
        >
            {/* Component Header */}
            <div className="px-3 py-2 flex items-center justify-between border-b border-[var(--border)] bg-[var(--vscode-editorGroupHeader-tabsBackground,var(--bg-darker))]">
                <h3 className="text-[12px] font-semibold text-[var(--text)] flex items-center gap-2">
                    {visual.name}
                </h3>
                <span className="text-[11px] text-[var(--muted)]">{type}</span>
            </div>

            <div className={clsx(
                "p-3 flex-1 relative overflow-hidden",
                isFilter ? "min-h-[auto]" : "min-h-[150px]"
            )}>
                {visual.error ? (
                    <div className="h-full flex flex-col items-center justify-center text-center p-4 gap-2 text-[var(--color-err)]">
                         <AlertCircle size={20} />
                         <p className="text-xs font-mono leading-relaxed">{visual.error}</p>
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
    onShowContextMenu: (x: number, y: number, rowData?: unknown[]) => void
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
                        baselineVisual.rows.forEach(r => { baselineMap[String(r[xIdx])] = Number(r[yIdx]) || 0; });

                        const currentMap: Record<string, number> = {};
                        visual.rows.forEach(r => { currentMap[String(r[xIdx])] = Number(r[yIdx]) || 0; });

                        if (option.series && option.series.length > 0) {
                            const primarySeries = option.series[0];
                            const categories: unknown[] = option.xAxis?.data || option.yAxis?.data || [];

                            const filteredData: number[] = [];
                            const remainingData: number[] = [];

                            categories.forEach((cat) => {
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
            let lastHoveredRow: unknown[] | null = null;
            chartInstance.current.on('mousemove', (params) => {
                const idx = (params as { dataIndex?: number }).dataIndex ?? -1;
                lastHoveredRow = (idx >= 0 ? (visual.rows || [])[idx] : null) as unknown[] | null;
            });

            chartInstance.current.on('click', (params) => {
                const p = params as { name?: string; data?: unknown; seriesIndex?: number; dataIndex?: number };
                const interactionMode = visual.interactions?.ON_SELECT?.toUpperCase();
                if (interactionMode && interactionMode !== 'NONE') {
                    const val = p.name || (Array.isArray(p.data) ? p.data[0] : p.data);
                    const matchingColumn = visual.interactions?.MATCHING || visual.options?.['mapping:x'] || visual.columns?.[0];
                    onParameterChange(`@${matchingColumn}`, String(val), visual.name);

                    // Highlight source bar, dim others
                    chartInstance.current?.dispatchAction({ type: 'downplay' });
                    chartInstance.current?.dispatchAction({
                        type: 'highlight',
                        seriesIndex: p.seriesIndex,
                        dataIndex: p.dataIndex
                    });
                } else {
                    const clickActions = visual.actions?.filter(a => a.trigger === 'ON_CLICK');
                    const idx = p.dataIndex ?? -1;
                    const rowData = (visual.rows || [])[idx] || [];
                    clickActions?.forEach(action => {
                        // Handle drill down or other click actions
                        (window as Window & VsCodeWindow).vscode?.postMessage({
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
                    onShowContextMenu(e.clientX, e.clientY, lastHoveredRow ?? undefined);
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
    // eslint-disable-next-line react-hooks/exhaustive-deps -- chart re-renders on config/theme/baseline change only; visual callbacks intentionally stable
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

function parseHexColor(hex: string): [number, number, number] {
    const h = hex.replace('#', '');
    const full = h.length === 3 ? h.split('').map(c => c + c).join('') : h;
    return [parseInt(full.slice(0, 2), 16), parseInt(full.slice(2, 4), 16), parseInt(full.slice(4, 6), 16)];
}

function interpolateColor(fromHex: string, toHex: string, t: number): string {
    const [r1, g1, b1] = parseHexColor(fromHex);
    const [r2, g2, b2] = parseHexColor(toHex);
    return `rgb(${Math.round(r1 + (r2 - r1) * t)},${Math.round(g1 + (g2 - g1) * t)},${Math.round(b1 + (b2 - b1) * t)})`;
}

function buildSparklineSvg(valuesJson: string, type: string, color?: string): string {
    let vals: number[];
    try {
        const raw = JSON.parse(valuesJson) as (number | null)[];
        vals = raw.map(v => (v === null ? NaN : parseFloat(String(v)))).filter(v => !isNaN(v));
    } catch { return ''; }
    if (vals.length < 2) return '';
    const W = 60, H = 20, PAD = 2;
    const mn = Math.min(...vals), mx = Math.max(...vals);
    const range = mx - mn || 1;
    const c = color || '#4472C4';
    const pts = vals.map((v, i) => {
        const x = (PAD + (i / (vals.length - 1)) * (W - PAD * 2)).toFixed(1);
        const y = (H - PAD - ((v - mn) / range) * (H - PAD * 2)).toFixed(1);
        return [x, y] as [string, string];
    });
    if (type === 'bar') {
        const bw = Math.max(2, (W - PAD * 2) / vals.length - 1);
        const bars = pts.map(([x, y]) =>
            `<rect x="${(parseFloat(x) - bw / 2).toFixed(1)}" y="${y}" width="${bw.toFixed(1)}" height="${(H - PAD - parseFloat(y)).toFixed(1)}" fill="${c}"/>`
        ).join('');
        return `<svg width="${W}" height="${H}" xmlns="http://www.w3.org/2000/svg">${bars}</svg>`;
    }
    const ptStr = pts.map(p => p.join(',')).join(' ');
    if (type === 'area') {
        const [x0] = pts[0]; const [xn] = pts[pts.length - 1];
        const area = `<polygon points="${ptStr} ${xn},${H - PAD} ${x0},${H - PAD}" fill="${c}" fill-opacity="0.2" stroke="none"/>`;
        const line = `<polyline points="${ptStr}" fill="none" stroke="${c}" stroke-width="1.5" stroke-linejoin="round" stroke-linecap="round"/>`;
        return `<svg width="${W}" height="${H}" xmlns="http://www.w3.org/2000/svg">${area}${line}</svg>`;
    }
    return `<svg width="${W}" height="${H}" xmlns="http://www.w3.org/2000/svg"><polyline points="${ptStr}" fill="none" stroke="${c}" stroke-width="1.5" stroke-linejoin="round" stroke-linecap="round"/></svg>`;
}

function formatCellValue(val: unknown, format?: string | null): string {
    if (val == null) return '';
    const s = String(val);
    if (!format) return s;
    const num = parseFloat(s);
    if (isNaN(num)) return s;
    const type = format.charAt(0).toUpperCase();
    const prec = parseInt(format.substring(1));
    const precision = isNaN(prec) ? undefined : prec;
    try {
        if (type === 'C') return new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD', minimumFractionDigits: precision, maximumFractionDigits: precision }).format(num);
        if (type === 'N') return new Intl.NumberFormat('en-US', { minimumFractionDigits: precision, maximumFractionDigits: precision }).format(num);
        if (type === 'P') return new Intl.NumberFormat('en-US', { style: 'percent', minimumFractionDigits: precision, maximumFractionDigits: precision }).format(num);
    } catch { /* ignore */ }
    return s;
}

const ReportTable: React.FC<{
    visual: VisualManifest,
    onShowContextMenu: (x: number, y: number, rowData?: unknown[]) => void
}> = ({ visual, onShowContextMenu }) => {
    const [sortCol, setSortCol] = useState(-1);
    const [sortDir, setSortDir] = useState<'asc' | 'desc'>('asc');
    const [searchText, setSearchText] = useState('');
    const [page, setPage] = useState(0);

    const grid      = (visual.options?.GRID     || 'HEADER').toUpperCase();
    const opts      = visual.options || {};
    const colMeta   = visual.columnMeta || [];
    const rawPageSize = parseInt(opts['PAGE_SIZE'] || '50', 10);
    const pageSize  = isNaN(rawPageSize) || rawPageSize <= 0 ? 0 : rawPageSize;
    const showSearch = (opts['SEARCH'] || 'ON').toUpperCase() !== 'OFF';
    const striped   = (opts['STRIPED'] || 'ON').toUpperCase() !== 'OFF';

    const filteredRows = useMemo(() => {
        let rows = visual.rows as unknown[][];
        if (searchText) {
            const q = searchText.toLowerCase();
            rows = rows.filter(row => row.some(c => c != null && String(c).toLowerCase().includes(q)));
        }
        if (sortCol >= 0) {
            rows = [...rows].sort((a, b) => {
                const av = a[sortCol] ?? '', bv = b[sortCol] ?? '';
                const an = parseFloat(String(av)), bn = parseFloat(String(bv));
                const cmp = !isNaN(an) && !isNaN(bn) ? an - bn : String(av).localeCompare(String(bv));
                return sortDir === 'asc' ? cmp : -cmp;
            });
        }
        return rows;
    }, [visual.rows, searchText, sortCol, sortDir]);

    const totalPages = pageSize > 0 ? Math.max(1, Math.ceil(filteredRows.length / pageSize)) : 1;
    const safePageSize = pageSize > 0 ? pageSize : filteredRows.length;
    const pageRows = filteredRows.slice(page * safePageSize, (page + 1) * safePageSize);

    function handleSort(ci: number) {
        if (sortCol === ci) {
            setSortDir(d => d === 'asc' ? 'desc' : 'asc');
        } else {
            setSortCol(ci);
            setSortDir('asc');
        }
        setPage(0);
    }

    return (
        <div className="w-full h-full flex flex-col overflow-hidden">
            {showSearch && (
                <div className="px-2 py-1 flex-shrink-0">
                    <input
                        type="text"
                        placeholder="Search…"
                        value={searchText}
                        onChange={e => { setSearchText(e.target.value); setPage(0); }}
                        className="w-full max-w-[220px] text-xs px-2 py-0.5 border border-[var(--border)] rounded bg-[var(--vscode-editor-background,var(--bg))] text-[var(--text)] outline-none focus:border-blue-400"
                    />
                </div>
            )}
            <div
                className="flex-1 min-h-0 overflow-auto border border-[var(--border)] bg-[var(--vscode-editor-background,var(--bg))] custom-scrollbar"
                onContextMenu={e => {
                    e.preventDefault();
                    const tr = (e.target as HTMLElement).closest('tr');
                    const idx = tr ? Array.from((tr.parentElement as HTMLTableSectionElement)?.rows || []).indexOf(tr) : -1;
                    onShowContextMenu(e.clientX, e.clientY, idx >= 0 ? pageRows[idx] as unknown[] : undefined);
                }}
            >
                <table className={clsx(
                    "w-full text-left text-xs border-collapse",
                    (grid === 'ALL' || grid === 'BOTH' || grid === 'OUTSIDE') && "border border-[var(--border)]/40"
                )}>
                    <thead className={clsx(
                        "sticky top-0 bg-[var(--vscode-editorGroupHeader-tabsBackground,var(--bg-darker))] z-10",
                        (grid === 'HEADER' || grid === 'ALL' || grid === 'ROWS' || grid === 'BOTH') && "border-b border-[var(--border)]"
                    )}>
                        <tr>
                            {visual.columns.map((col, ci) => {
                                const meta = colMeta[ci];
                                return (
                                    <th
                                        key={col}
                                        onClick={() => handleSort(ci)}
                                        style={{ textAlign: (meta?.align as React.CSSProperties['textAlign']) }}
                                        className={clsx(
                                            "px-2 py-1.5 font-normal text-[var(--muted)] text-[12px] cursor-pointer select-none hover:bg-black/5",
                                            (grid === 'ALL' || grid === 'COLS' || grid === 'BOTH') && ci < visual.columns.length - 1 && "border-r border-[var(--border)]/30",
                                            (grid === 'HEADER' || grid === 'ALL' || grid === 'ROWS' || grid === 'BOTH') && "border-b border-[var(--border)]",
                                        )}
                                    >
                                        {col}
                                        {sortCol === ci && <span className="ml-1 text-[10px]">{sortDir === 'asc' ? '▲' : '▼'}</span>}
                                    </th>
                                );
                            })}
                        </tr>
                    </thead>
                    <tbody className={clsx(
                        (grid === 'ROWS' || grid === 'ALL' || grid === 'BOTH') ? "divide-y divide-[var(--border)]/40" : ""
                    )}>
                        {pageRows.map((row, i) => {
                            const origIdx = visual.rows.indexOf(row as never);
                            const rowBg   = visual.rowStyles?.[origIdx];
                            const rowFont = visual.rowFontStyles?.[origIdx];
                            const altRow  = striped && (page * safePageSize + i) % 2 === 1;
                            return (
                                <tr
                                    key={i}
                                    className={clsx(
                                        "hover:bg-[var(--vscode-list-hoverBackground,rgba(90,93,94,0.31))]",
                                        altRow && !rowBg && "bg-[var(--bg-darker,rgba(0,0,0,0.04))]"
                                    )}
                                    style={{
                                        ...(rowBg  ? { backgroundColor: rowBg + '33' } : {}),
                                        ...(rowFont ? { color: rowFont } : {}),
                                    }}
                                >
                                    {(row as unknown[]).map((cell, ci) => {
                                        const meta = colMeta[ci];
                                        const rawVal = cell != null ? String(cell) : '';
                                        const fmtVal = formatCellValue(cell, meta?.format);

                                        // COLOR_SCALE: gradient background
                                        let bgColor: string | undefined;
                                        if (meta?.colorScaleFrom && meta.colorScaleTo && meta.colorScaleMax !== undefined) {
                                            const num = parseFloat(rawVal);
                                            if (!isNaN(num)) {
                                                const range = (meta.colorScaleMax - (meta.colorScaleMin ?? 0)) || 1;
                                                const t = Math.max(0, Math.min(1, (num - (meta.colorScaleMin ?? 0)) / range));
                                                bgColor = interpolateColor(meta.colorScaleFrom, meta.colorScaleTo, t);
                                            }
                                        }

                                        // DATA_BAR: proportional fill bar
                                        let dataBarPct: number | undefined;
                                        if (meta?.dataBar && meta.dataBarMax !== undefined) {
                                            const num = parseFloat(rawVal);
                                            const dmin = meta.dataBarMin ?? 0;
                                            const dmax = meta.dataBarMax;
                                            if (!isNaN(num) && dmax > dmin) {
                                                dataBarPct = Math.max(0, Math.min(100, (num - dmin) / (dmax - dmin) * 100));
                                            }
                                        }

                                        return (
                                            <td
                                                key={ci}
                                                style={{
                                                    textAlign: (meta?.align as React.CSSProperties['textAlign']),
                                                    ...(bgColor ? { backgroundColor: bgColor } : {}),
                                                    ...(dataBarPct !== undefined ? { position: 'relative', padding: 0 } : {}),
                                                }}
                                                className={clsx(
                                                    "font-mono text-[var(--text)]",
                                                    dataBarPct === undefined && "px-2 py-1.5",
                                                    (grid === 'ALL' || grid === 'COLS' || grid === 'BOTH') && ci < visual.columns.length - 1 && "border-r border-[var(--border)]/20",
                                                )}
                                            >
                                                {dataBarPct !== undefined ? (
                                                    <>
                                                        <div style={{
                                                            position: 'absolute', inset: 0, height: '100%',
                                                            width: dataBarPct.toFixed(1) + '%',
                                                            backgroundColor: meta?.dataBarColor || '#4472C4',
                                                            opacity: 0.28, pointerEvents: 'none'
                                                        }} />
                                                        <span style={{ position: 'relative', display: 'block', padding: '6px 8px', zIndex: 1 }}>
                                                            {fmtVal}
                                                        </span>
                                                    </>
                                                ) : meta?.cellRenderer === 'image' ? (
                                                    rawVal
                                                        ? <img src={rawVal} alt=""
                                                              style={{ maxHeight: (meta.imageWidth ?? 32) + 'px',
                                                                       maxWidth: ((meta.imageWidth ?? 32) * 3) + 'px',
                                                                       verticalAlign: 'middle' }} />
                                                        : null
                                                ) : meta?.cellRenderer === 'hyperlink' ? (
                                                    <a href={/^https?:\/\//i.test(rawVal) ? rawVal : '#'}
                                                       target="_blank" rel="noopener noreferrer">
                                                        {meta.hyperlinkLabel || rawVal}
                                                    </a>
                                                ) : meta?.cellRenderer === 'sparkline' ? (
                                                    rawVal
                                                        ? <span style={{ lineHeight: 0, verticalAlign: 'middle' }}
                                                                dangerouslySetInnerHTML={{ __html: buildSparklineSvg(rawVal, meta.sparklineType ?? 'line') }} />
                                                        : null
                                                ) : fmtVal}
                                            </td>
                                        );
                                    })}
                                </tr>
                            );
                        })}
                    </tbody>
                    {visual.summaryData && (
                        <tfoot className="sticky bottom-0 bg-[var(--vscode-editorGroupHeader-tabsBackground,var(--bg-darker))] z-10 border-t border-[var(--border)]">
                            {visual.summaryData.grandTotals && (
                                <tr>
                                    {visual.columns.map((col, ci) => {
                                        const meta = colMeta[ci];
                                        return (
                                            <td key={ci}
                                                style={{ textAlign: (meta?.align as React.CSSProperties['textAlign']) }}
                                                className="px-2 py-1.5 font-semibold text-[var(--text)] text-xs border-t border-[var(--border)]">
                                                {formatCellValue(visual.summaryData!.grandTotals![col], meta?.format) ?? ''}
                                            </td>
                                        );
                                    })}
                                </tr>
                            )}
                            {visual.summaryData.aggregates.length > 0 && (
                                <tr>
                                    <td colSpan={visual.columns.length} className="px-2 py-1.5">
                                        <div className="flex flex-wrap gap-x-4 gap-y-1">
                                            {visual.summaryData.aggregates.map((agg, ai) => (
                                                <div key={ai} className="flex items-center gap-2">
                                                    <span className="text-[11px] text-[var(--muted)]">{agg.alias || `${agg.aggregate}(${agg.column})`}</span>
                                                    <span className="text-xs font-mono text-[var(--text)]">{agg.value}</span>
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
            {pageSize > 0 && totalPages > 1 && (
                <div className="flex items-center gap-2 px-2 py-1 border-t border-[var(--border)] text-[11px] text-[var(--muted)] flex-shrink-0">
                    <button
                        onClick={() => setPage(p => Math.max(0, p - 1))}
                        disabled={page === 0}
                        className="px-2 py-0.5 border border-[var(--border)] rounded disabled:opacity-30 hover:bg-black/5 cursor-pointer"
                    >◀</button>
                    <span className="flex-1 text-center">
                        {page * pageSize + 1}–{Math.min((page + 1) * pageSize, filteredRows.length)} of {filteredRows.length}
                    </span>
                    <button
                        onClick={() => setPage(p => Math.min(totalPages - 1, p + 1))}
                        disabled={page >= totalPages - 1}
                        className="px-2 py-0.5 border border-[var(--border)] rounded disabled:opacity-30 hover:bg-black/5 cursor-pointer"
                    >▶</button>
                </div>
            )}
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
        <div className="h-full flex flex-col justify-center py-3 px-2">
            <span className="text-[11px] text-[var(--muted)] mb-1">{label}</span>
            <span className="text-3xl font-semibold text-[var(--text)]">
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
            if (val != null) uniqueValues.add(String(val));
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
                className="w-full border border-[var(--vscode-dropdown-border,var(--border))] px-2 py-1.5 text-xs focus:outline-none focus:border-[var(--vscode-focusBorder,#007fd4)] cursor-pointer appearance-none"
                style={{
                    backgroundColor: 'var(--vscode-dropdown-background, var(--bg-darker))',
                    color: 'var(--vscode-dropdown-foreground, var(--text))'
                }}
            >
                {!visual.defaultValue && <option value="">Select...</option>}
                {visual.defaultValue && !defaultIsInOptions && (
                    <option value={visual.defaultValue}>{visual.defaultValue}</option>
                )}
                {options.map(opt => (
                    <option key={opt} value={opt}>{opt}</option>
                ))}
            </select>
            {boundParam && (
                <div className="mt-1.5 flex items-center justify-between px-1 text-[11px] text-[var(--muted)]">
                    <span>Binding</span>
                    <span className="font-mono">{boundParam}</span>
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
            if (val != null) uniqueValues.add(String(val));
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
            <div className="flex flex-col max-h-[200px] overflow-y-auto custom-scrollbar border border-[var(--border)] bg-[var(--vscode-editor-background,var(--bg))]">
                {options.map(opt => (
                    <label key={opt} className="flex items-center gap-2 cursor-pointer group px-2 py-1 hover:bg-[var(--vscode-list-hoverBackground,rgba(90,93,94,0.31))]">
                        <input
                            type="checkbox"
                            checked={currentValues.includes(opt)}
                            onChange={() => handleToggle(opt)}
                            className="w-3.5 h-3.5 cursor-pointer"
                        />
                        <span className={clsx(
                            "text-xs",
                            currentValues.includes(opt) ? "text-[var(--text)] font-semibold" : "text-[var(--muted)] group-hover:text-[var(--text)]"
                        )}>
                            {opt}
                        </span>
                    </label>
                ))}
            </div>
            {boundParam && (
                <div className="mt-1 flex items-center justify-between px-1 text-[11px] text-[var(--muted)]">
                    <span>Multi-select</span>
                    <span className="font-mono">{currentValues.length} selected</span>
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
        <div className="h-full w-full flex items-center justify-center overflow-hidden">
            <img 
                src={src} 
                alt={visual.name}
                className="max-w-full max-h-full"
                style={{ objectFit: fit as React.CSSProperties['objectFit'] }}
            />
        </div>
    );
};

const SimpleMarkdown: React.FC<{ text: string }> = ({ text }) => {
    const html = useMemo(() => {
        // Basic escaping
        const h = text
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
        const resultLines: string[] = [];

        const flushTable = () => {
            if (tableLines.length === 0) return;
            let tableHtml = '<div class="my-3 overflow-x-auto border border-[var(--border)]"><table class="w-full text-left text-sm border-collapse">';
            tableLines.forEach((line, idx) => {
                if (line.includes('---') && idx === 1) return;
                const cells = line.split('|').filter((_, i, a) => i > 0 && i < a.length - 1);
                const tag = idx === 0 ? 'th' : 'td';
                const className = idx === 0 
                    ? "px-2 py-1.5 bg-[var(--vscode-editorGroupHeader-tabsBackground,var(--bg-darker))] font-normal text-[var(--muted)] text-[12px] border-b border-[var(--border)]"
                    : "px-2 py-1.5 border-b border-[var(--border)] font-mono text-[var(--text)]";
                
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
        <div className="h-full w-full flex flex-col items-center justify-center gap-2 text-center p-4 text-[var(--muted)]">
            <p className="text-sm font-semibold">Map preview unavailable</p>
            <p className="text-xs max-w-[320px] leading-relaxed">
                {mapKey ? `${mapKey} · ${mode}` : 'MAP'} charts require the HTTP server to load GeoJSON.
                Open the report in <span className="font-mono text-[var(--text)]">Report Portal</span> or run the
                script with <span className="font-mono text-[var(--text)]">--ui</span> to see the live map.
            </p>
        </div>
    );
};

const ContextMenu: React.FC<{
    x: number,
    y: number,
    visual: VisualManifest,
    rowData?: unknown[],
    onClose: () => void,
    onAction: (action: ReportAction) => void
}> = ({ x, y, visual, onClose, onAction }) => {
    useEffect(() => {
        const handle = () => onClose();
        window.addEventListener('click', handle, { capture: true });
        return () => window.removeEventListener('click', handle, { capture: true });
    }, [onClose]);

    const drillDowns = (visual.actions || []).filter(a => a.type === 'DRILL_DOWN');

    const exportCsv = () => {
        const escape = (v: unknown) => '"' + String(v ?? '').replace(/"/g, '""') + '"';
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
            className="fixed z-[9999] bg-[var(--vscode-menu-background,var(--bg-darker))] border border-[var(--vscode-menu-border,var(--border))] py-1 min-w-[200px] text-[var(--vscode-menu-foreground,var(--text))]"
            style={{ left: x, top: y }}
            onClick={e => e.stopPropagation()}
        >
            {drillDowns.map((action, i) => (
                <button 
                    key={i}
                    onClick={() => { onAction(action); onClose(); }}
                    className="w-full text-left px-3 py-1.5 text-[12px] hover:bg-[var(--vscode-menu-selectionBackground,var(--vscode-list-hoverBackground))] hover:text-[var(--vscode-menu-selectionForeground,var(--text))] flex items-center gap-2"
                >
                    <RefreshCw size={14} className="opacity-50" />
                    <span>Drill down to <b>{action.targetVisual || action.targetPage || 'Details'}</b></span>
                </button>
            ))}
            {drillDowns.length > 0 && <div className="h-px bg-[var(--border)] my-1" />}
            <button 
                onClick={() => { exportCsv(); onClose(); }}
                className="w-full text-left px-3 py-1.5 text-[12px] hover:bg-[var(--vscode-menu-selectionBackground,var(--vscode-list-hoverBackground))] hover:text-[var(--vscode-menu-selectionForeground,var(--text))] flex items-center gap-2"
            >
                <Download size={14} className="opacity-50" />
                <span>Export to CSV</span>
            </button>
        </div>
    );
};
