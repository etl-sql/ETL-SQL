import React, { useEffect, useRef, useState, useMemo } from 'react';
import * as echarts from 'echarts';
import { RefreshCw, AlertCircle, Calendar } from 'lucide-react';
import type { ReportManifest, VisualManifest, ContainerManifest, PageManifest } from '../types';
import { clsx } from 'clsx';

interface ReportTabProps {
    manifest: ReportManifest;
    onRefresh: (parameters?: Record<string, string | null>) => void;
}

export const ReportTab: React.FC<ReportTabProps> = ({ manifest, onRefresh }) => {
    const [activePageName, setActivePageName] = useState<string | null>(
        manifest.pages?.[0]?.name || null
    );

    // Local parameter state initialized from active page defaults
    const [parameters, setParameters] = useState<Record<string, string | null>>({});
    const [isRefreshing, setIsRefreshing] = useState(false);
    const debounceTimer = useRef<any>(null);

    const activePage = useMemo(() => 
        manifest.pages.find(p => p.name === activePageName) || manifest.pages[0],
    [manifest, activePageName]);

    // Reset isRefreshing when manifest changes (indicating refresh completed)
    useEffect(() => {
        setIsRefreshing(false);
    }, [manifest]);

    // Initialize parameters when manifest changes (if new variables are detected)
    useEffect(() => {
        // Variables are now managed primarily by the host environment, 
        // but we can initialize local state if needed.
    }, [manifest]);

    // Handle parameter changes from slicers/inputs
    const handleParameterUpdate = (name: string, value: string) => {
        setParameters(prev => ({ ...prev, [name]: value }));
        
        // Debounced auto-refresh
        if (debounceTimer.current) clearTimeout(debounceTimer.current);
        debounceTimer.current = setTimeout(() => {
            setIsRefreshing(true);
            onRefresh({ ...parameters, [name]: value });
        }, 500);
    };

    return (
        <div className="flex-1 flex flex-col min-h-0 bg-[var(--bg-dark)] text-[var(--text)] overflow-hidden font-display">
            {/* Header */}
            <header className="px-6 py-4 border-b border-[var(--border)] bg-[var(--bg-darker)]/40 backdrop-blur-xl flex items-center justify-between shrink-0">
                <div className="flex flex-col">
                    <h1 className="text-xl font-bold tracking-tight text-white flex items-center gap-2">
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
                    
                    <button 
                        onClick={() => {
                            setIsRefreshing(true);
                            onRefresh(parameters);
                        }}
                        disabled={isRefreshing}
                        className={clsx(
                            "p-2 rounded-xl border transition-all duration-300 group shadow-lg",
                            isRefreshing 
                                ? "bg-white/5 text-[var(--muted)] border-white/5 opacity-50 cursor-not-allowed"
                                : "bg-white/5 hover:bg-indigo-500/20 text-[var(--muted)] hover:text-indigo-400 border-white/5 hover:border-indigo-500/30"
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
                                            ? "bg-indigo-500 text-white border-indigo-400/50 shadow-[0_0_15px_rgba(99,102,241,0.4)]" 
                                            : "bg-white/5 text-[var(--muted)] border-transparent hover:bg-white/10"
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
            <main className="flex-1 overflow-y-auto p-8 custom-scrollbar relative">
                <div className="max-w-7xl mx-auto space-y-12">
                   {activePage && (
                       <RenderLayout 
                           page={activePage} 
                           manifest={manifest} 
                           parameters={parameters} 
                           onParameterChange={handleParameterUpdate} 
                       />
                   )}
                </div>
            </main>
        </div>
    );
};

const RenderLayout: React.FC<{ 
    page: PageManifest, 
    manifest: ReportManifest,
    parameters: Record<string, string | null>,
    onParameterChange: (name: string, value: string) => void
}> = ({ page, manifest, parameters, onParameterChange }) => {
    return (
        <GenericLayout 
            structure={page.structure} 
            slotMap={page.slotMap} 
            manifest={manifest} 
            parameters={parameters} 
            onParameterChange={onParameterChange} 
        />
    );
};

const GenericLayout: React.FC<{
    structure: string,
    slotMap: Record<string, string>,
    manifest: ReportManifest,
    parameters: Record<string, string | null>,
    onParameterChange: (name: string, value: string) => void
}> = ({ structure, slotMap, manifest, parameters, onParameterChange }) => {
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
                    <div key={slot} style={{ gridArea: slot }} className="flex">
                        <RenderObject 
                            name={objectName} 
                            manifest={manifest} 
                            parameters={parameters}
                            onParameterChange={onParameterChange}
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
    parameters: Record<string, string | null>,
    onParameterChange: (name: string, value: string) => void
}> = ({ name, manifest, parameters, onParameterChange }) => {
    // Find in visuals first
    const visual = manifest.visuals.find(v => v.name.toLowerCase() === name.toLowerCase());
    if (visual) return (
        <VisualCard 
            visual={visual} 
            parameters={parameters}
            onParameterChange={onParameterChange}
        />
    );

    // Find in containers
    const container = manifest.containers?.find(c => c.name.toLowerCase() === name.toLowerCase());
    if (container) return (
        <ContainerView 
            container={container} 
            manifest={manifest} 
            parameters={parameters}
            onParameterChange={onParameterChange}
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
    parameters: Record<string, string | null>,
    onParameterChange: (name: string, value: string) => void
}> = ({ container, manifest, parameters, onParameterChange }) => {
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
                    parameters={parameters} 
                    onParameterChange={onParameterChange} 
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
                        parameters={parameters}
                        onParameterChange={onParameterChange}
                    />
                </div>
            ))}
        </div>
    );
};

const VisualCard: React.FC<{ 
    visual: VisualManifest,
    parameters: Record<string, string | null>,
    onParameterChange: (name: string, value: string) => void
}> = ({ visual, parameters, onParameterChange }) => {
    const type = visual.visualType.toUpperCase();
    const isFilter = ['SLICER', 'DATEPICKER', 'SLIDER', 'MULTISELECT', 'SEARCH'].includes(type);
    
    const cardStyle: React.CSSProperties = {};
    if (visual.styles?.HEIGHT) cardStyle.height = visual.styles.HEIGHT;
    if (visual.styles?.WIDTH) cardStyle.width = visual.styles.WIDTH;
    if (visual.styles?.MAX_HEIGHT) cardStyle.maxHeight = visual.styles.MAX_HEIGHT;
    if (visual.styles?.MIN_HEIGHT) cardStyle.minHeight = visual.styles.MIN_HEIGHT;

    return (
        <div 
            style={cardStyle}
            className={clsx(
                "w-full group/card flex flex-col rounded-3xl border transition-all duration-500 shadow-xl overflow-hidden backdrop-blur-sm",
                isFilter ? "border-indigo-500/20 hover:border-indigo-500/40 bg-[var(--bg-darker,#050507)]" : "border-[var(--border)] hover:border-indigo-500/30 bg-[var(--bg-darker,#050507)]"
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
                isFilter ? "min-h-[auto]" : "min-h-[250px]"
            )}>
                {visual.error ? (
                    <div className="h-full flex flex-col items-center justify-center text-center p-8 space-y-3 opacity-60">
                         <AlertCircle className="text-red-400" size={32} />
                         <p className="text-xs text-red-400/80 font-mono leading-relaxed">{visual.error}</p>
                    </div>
                ) : (
                    <div className="h-full w-full">
                         {type === 'TABLE' && <ReportTable visual={visual} />}
                         {type === 'CARD' && <ReportCard visual={visual} />}
                         {type === 'SLICER' && (
                             <ReportSlicer 
                                 visual={visual} 
                                 parameters={parameters} 
                                 onParameterChange={onParameterChange} 
                             />
                         )}
                         {type === 'IMAGE' && <ReportImage visual={visual} />}
                         {['BAR', 'LINE', 'PIE', 'DONUT', 'SCATTER', 'HBAR', 'BOXPLOT', 'TREEMAP', 'HEATMAP', 'COMBO', 'GAUGE', 'FUNNEL', 'WATERFALL'].includes(type) && (
                             <ReportChart visual={visual} />
                         )}
                    </div>
                )}
            </div>
        </div>
    );
};

const ReportChart: React.FC<{ visual: VisualManifest }> = ({ visual }) => {
    const chartRef = useRef<HTMLDivElement>(null);
    const chartInstance = useRef<echarts.ECharts | null>(null);

    // Dynamic theme detection based on VS Code color scheme
    const isDark = true; // For now assuming dark as per our premium design rules

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
                : visual.chartConfig;
            
            // Inject transparent background for glassmorphism integration
            option.backgroundColor = 'transparent';
            
            chartInstance.current.setOption(option, true);
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
    }, [visual.chartConfig, isDark]);

    return <div ref={chartRef} className="w-full h-full min-h-[300px]" />;
};

const ReportTable: React.FC<{ visual: VisualManifest }> = ({ visual }) => {
    const [ctxMenu, setCtxMenu] = useState<{ x: number; y: number } | null>(null);
    const grid = (visual.options?.GRID || 'HEADER').toUpperCase();

    const exportCsv = () => {
        const escape = (v: string | null) => '"' + (v ?? '').replace(/"/g, '""') + '"';
        const lines = [visual.columns.map(escape).join(',')];
        visual.rows.forEach(r => lines.push(visual.columns.map((_, i) => escape(r[i] ?? null)).join(',')));
        const blob = new Blob([lines.join('\r\n')], { type: 'text/csv' });
        const url  = URL.createObjectURL(blob);
        const a    = document.createElement('a');
        a.href     = url;
        a.download = `${visual.name}.csv`;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
        setCtxMenu(null);
    };

    return (
        <div className="w-full h-full relative" onClick={() => setCtxMenu(null)}>
            <div
                className="w-full h-full overflow-auto rounded-xl border border-[var(--border)]/30 bg-black/10 custom-scrollbar"
                onContextMenu={e => { e.preventDefault(); setCtxMenu({ x: e.clientX, y: e.clientY }); }}
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

            {ctxMenu && (
                <div
                    className="fixed z-50 bg-[var(--bg-darker,#050507)] border border-[var(--border)] rounded-lg shadow-xl py-1 text-xs"
                    style={{ left: ctxMenu.x, top: ctxMenu.y }}
                    onClick={e => e.stopPropagation()}
                >
                    <button
                        className="w-full text-left px-4 py-2 hover:bg-white/10 text-[var(--text)] flex items-center gap-2"
                        onClick={exportCsv}
                    >
                        ⬇ Export to CSV
                    </button>
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
    const currentValue = (boundParam && parameters[boundParam]) || visual.defaultValue || '';

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
