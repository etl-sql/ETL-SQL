import React from 'react';
import { motion } from 'framer-motion';
import { Zap, Database, Cpu, Activity } from 'lucide-react';
import type { PerformanceMetrics } from '../types';

interface PerformanceTabProps {
  metrics: PerformanceMetrics | null;
}

export const PerformanceTab: React.FC<PerformanceTabProps> = ({ metrics }) => {
  if (!metrics) {
    return (
      <div className="flex flex-col items-center justify-center h-full opacity-20 space-y-4">
        <Activity size={48} strokeWidth={1} />
        <p className="text-sm font-display font-bold uppercase tracking-widest">No Performance Data</p>
      </div>
    );
  }

  return (
    <div className="flex flex-col h-full overflow-y-auto scrollbar-fancy animate-fade-in p-6 gap-6">
      {/* Primary Metrics */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-4 shrink-0">
        <MetricCard 
          icon={Zap} 
          label="Execution Time" 
          value={`${metrics.executionMs}ms`} 
          color="text-amber-400"
          subValue="Total engine throughput"
        />
        <MetricCard 
          icon={Database} 
          label="Rows Processed" 
          value={metrics.rowsProcessed.toLocaleString()} 
          color="text-indigo-400"
          subValue="Across all stages"
        />
        <MetricCard 
          icon={Cpu} 
          label="Memory Usage" 
          value={`${metrics.memoryMb} MB`} 
          color="text-emerald-400"
          subValue="Peak memory allocation"
        />

        {/* Statement Usage Chart placeholder/list */}
        <div className="col-span-1 md:col-span-3 glass-card p-6 mt-2 relative overflow-hidden bg-white/[0.01]">
          <h3 className="text-[10px] font-bold uppercase tracking-[0.2em] mb-6 opacity-60 flex items-center gap-2">
            <Activity size={12} />
            Execution Breakdown by Stage
          </h3>
          <div className="space-y-4">
            {metrics.statements.map((s, idx) => (
              <div key={idx} className="space-y-1.5">
                <div className="flex justify-between text-[11px]">
                  <span className="font-bold opacity-80">{s.type}</span>
                  <span className="font-mono text-indigo-300">{s.totalMs}ms</span>
                </div>
                <div className="h-1.5 w-full bg-white/5 rounded-full overflow-hidden">
                  <motion.div 
                    initial={{ width: 0 }}
                    animate={{ width: `${Math.min(100, (s.totalMs / metrics.executionMs) * 100)}%` }}
                    className="h-full bg-gradient-to-r from-indigo-500 to-indigo-400 rounded-full"
                  />
                </div>
              </div>
            ))}
          </div>
        </div>
      </div>

      {/* Latency Viz */}
      <div className="glass-card p-6 flex flex-col items-center justify-center opacity-40 border-indigo-500/10 shrink-0">
          <div className="relative w-24 h-24 flex items-center justify-center">
             <svg className="w-full h-full transform -rotate-90">
                <circle cx="48" cy="48" r="42" fill="transparent" stroke="currentColor" strokeWidth="4" className="text-white/5" />
                <circle cx="48" cy="48" r="42" fill="transparent" stroke="currentColor" strokeWidth="4" className="text-indigo-500/20" strokeLinecap="round" />
             </svg>
             <Zap size={20} className="absolute text-indigo-400" />
          </div>
          <span className="text-[9px] uppercase tracking-widest mt-4 font-bold">Latency Verified</span>
      </div>
    </div>
  );
};

const MetricCard: React.FC<{ icon: any, label: string, value: string, color: string, subValueText?: string, subValue?: string }> = ({ icon: Icon, label, value, color, subValue }) => (
  <div className="glass-card p-6 border-b-2 border-transparent hover:border-indigo-500/20 transition-all group">
    <div className="flex items-center gap-3 mb-4">
      <div className={`p-2 rounded bg-white/5 ${color} group-hover:scale-110 transition-transform`}>
        <Icon size={18} />
      </div>
      <span className="text-[10px] font-bold uppercase tracking-widest opacity-40">{label}</span>
    </div>
    <div className={`text-2xl font-display font-bold mb-1 tracking-tight ${color}`}>{value}</div>
    {subValue && <div className="text-[9px] opacity-30 font-medium uppercase tracking-wider">{subValue}</div>}
  </div>
);
