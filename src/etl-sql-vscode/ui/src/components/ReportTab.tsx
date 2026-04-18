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

    // Initialize parameters when active page changes
    useEffect(() => {
        if (activePage) {
            setParameters(prev => {
                const next = { ...prev };
                Object.entries(activePage.parameters).forEach(([name, val]) => {
                    if (next[name] === undefined) next[name] = val;
                });
                return next;
            });
        }
    }, [activePage]);

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

            {/* Navigation Tabs (if multiple pages) */}
            {manifest.pages.length > 1 && (
                <div className="px-6 border-b border-[var(--border)] bg-[var(--bg-darker)]/20 shrink-0 flex gap-2 overflow-x-auto no-scrollbar py-2">
                    {manifest.pages.map(page => (
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
    // Basic CSS Grid areas from structure (e.g., 'A B / C C / D D')
    const rows = page.structure.split('/').map(r => r.trim());
    const rowCount = rows.length;
    const colCount = rows[0].split(/\s+/).length;
    const gridStyle = {
        gridTemplateAreas: rows.map(r => `'${r}'`).join(' '),
        gridTemplateColumns: `repeat(${colCount}, 1fr)`,
        gridTemplateRows: `repeat(${rowCount}, minmax(100px, auto))`, // reduced from 300px for filters
    };

    return (
        <div className="grid gap-6 animate-in fade-in slide-in-from-bottom-4 duration-700" style={gridStyle}>
            {Object.keys(page.slotMap).map(slot => {
                const objectName = page.slotMap[slot];
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
    const isRow = container.containerType.toUpperCase() === 'ROW' || container.containerType.toUpperCase() === 'BOX';
    
    return (
        <div className={clsx(
            "w-full flex gap-6",
            isRow ? "flex-row flex-wrap" : "flex-col"
        )}>
            {container.visuals.map(vName => (
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
    
    return (
        <div className={clsx(
            "w-full group/card flex flex-col rounded-3xl border transition-all duration-500 shadow-xl overflow-hidden backdrop-blur-sm",
            isFilter ? "border-indigo-500/20 hover:border-indigo-500/40 bg-[var(--bg-darker,#050507)]" : "border-[var(--border)] hover:border-indigo-500/30 bg-[var(--bg-darker,#050507)]"
        )}>
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
                <table className="w-full text-left text-xs border-collapse">
                    <thead className="sticky top-0 bg-[var(--bg-darker)] shadow-md z-10">
                        <tr>
                            {visual.columns.map(col => (
                                <th key={col} className="px-4 py-3 font-bold border-b border-[var(--border)] text-indigo-300 uppercase tracking-widest text-[10px]">
                                    {col}
                                </th>
                            ))}
                        </tr>
                    </thead>
                    <tbody className="divide-y divide-[var(--border)]/40">
                        {visual.rows.slice(0, 100).map((row, i) => (
                            <tr key={i} className="hover:bg-white/5 transition-colors">
                                {row.map((cell, ci) => (
                                    <td key={ci} className="px-4 py-2.5 font-mono text-[var(--muted)]">
                                        {cell !== null ? String(cell) : ''}
                                    </td>
                                ))}
                            </tr>
                        ))}
                        {visual.rows.length > 100 && (
                            <tr>
                                <td colSpan={visual.columns.length} className="px-4 py-3 text-center text-[var(--muted)] italic opacity-50">
                                    Showing first 100 rows of {visual.rows.length}...
                                </td>
                            </tr>
                        )}
                    </tbody>
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
