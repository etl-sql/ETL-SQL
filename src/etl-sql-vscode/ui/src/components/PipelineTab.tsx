import React, { useMemo, useState, useRef } from 'react';
import { motion } from 'framer-motion';
import { CheckCircle2, Circle, PlayCircle, AlertCircle, Clock, Database, ZoomIn, ZoomOut, Maximize } from 'lucide-react';
import type { ExecutionNode } from '../types';

interface PipelineTabProps {
  nodes: ExecutionNode[];
  isFinished?: boolean;
}

interface NodeWithPosition extends ExecutionNode {
  level: number;
  index: number;
}

export const PipelineTab: React.FC<PipelineTabProps> = ({ nodes, isFinished }) => {
  const [scale, setScale] = useState(1);
  const [offset, setOffset] = useState({ x: 0, y: 0 });
  const [isDragging, setIsDragging] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);

  // Constants for compact layout
  const NODE_WIDTH = 180;
  const NODE_HEIGHT = 60;
  const LEVEL_GAP = 100;
  const NODE_GAP = 24;

  const levels = useMemo(() => {
    const result: NodeWithPosition[][] = [];
    const traverse = (node: ExecutionNode, level: number) => {
      if (!result[level]) result[level] = [];
      const nodeWithPos: NodeWithPosition = { 
        ...node, 
        level, 
        index: result[level].length,
        status: (isFinished && (node.status === 'Running' || node.status === 'Pending')) 
          ? 'Completed' : node.status
      };
      result[level].push(nodeWithPos);
      if (node.children) node.children.forEach(child => traverse(child, level + 1));
    };
    nodes.forEach(root => traverse(root, 0));
    return result;
  }, [nodes, isFinished]);

  // Zoom/Pan Handlers
  const handleWheel = (e: React.WheelEvent) => {
    if (e.ctrlKey || e.metaKey) {
      e.preventDefault();
      const delta = e.deltaY > 0 ? 0.9 : 1.1;
      setScale(prev => Math.min(Math.max(0.2, prev * delta), 3));
    } else {
       setOffset(prev => ({ x: prev.x - e.deltaX, y: prev.y - e.deltaY }));
    }
  };

  const handleMouseDown = () => setIsDragging(true);
  const handleMouseUp = () => setIsDragging(false);
  const handleMouseMove = (e: React.MouseEvent) => {
    if (isDragging) {
      setOffset(prev => ({ x: prev.x + e.movementX, y: prev.y + e.movementY }));
    }
  };

  const resetView = () => {
    setScale(1);
    setOffset({ x: 0, y: 0 });
  };

  if (!nodes || nodes.length === 0) {
    return (
      <div className="flex flex-col items-center justify-center h-full opacity-20 space-y-4 font-display">
        <PlayCircle size={48} strokeWidth={1} />
        <p className="text-sm font-bold uppercase tracking-[0.2em]">No Active Pipeline</p>
      </div>
    );
  }

  return (
    <div className="relative h-full w-full overflow-hidden bg-[var(--bg-darker)]/40 font-sans">
      {/* Zoom Controls Overlay */}
      <div className="absolute bottom-6 right-6 z-50 flex items-center gap-2 bg-[var(--bg-darker)] backdrop-blur-md border border-[var(--border)] rounded-full p-2">
         <button onClick={() => setScale(s => Math.min(s + 0.1, 3))} className="p-2 hover:bg-white/10 rounded-full transition-colors text-white/60 hover:text-white" title="Zoom In"><ZoomIn size={14} /></button>
         <div className="w-[1px] h-4 bg-white/10 mx-1" />
         <button onClick={() => setScale(s => Math.max(s - 0.1, 0.2))} className="p-2 hover:bg-white/10 rounded-full transition-colors text-white/60 hover:text-white" title="Zoom Out"><ZoomOut size={14} /></button>
         <div className="w-[1px] h-4 bg-white/10 mx-1" />
         <button onClick={resetView} className="p-2 hover:bg-white/10 rounded-full transition-colors text-white/60 hover:text-white" title="Reset View"><Maximize size={14} /></button>
         <span className="px-3 text-[10px] font-bold text-indigo-400 font-mono">{Math.round(scale * 100)}%</span>
      </div>

      <div 
        ref={containerRef}
        className={`h-full w-full cursor-grab ${isDragging ? 'cursor-grabbing' : ''}`}
        onWheel={handleWheel}
        onMouseDown={handleMouseDown}
        onMouseUp={handleMouseUp}
        onMouseMove={handleMouseMove}
        onMouseLeave={() => setIsDragging(false)}
      >
        <div 
          className="absolute origin-top-left transition-transform duration-75 ease-out min-h-[400px]"
          style={{ transform: `translate(${offset.x}px, ${offset.y}px) scale(${scale})`, padding: '8px' }}
        >
          {/* SVG Connections with adjusted math */}
          <svg 
            className="absolute inset-0 pointer-events-none opacity-20" 
            style={{ 
              width: levels.length * (NODE_WIDTH + LEVEL_GAP) + 500, 
              height: '2000px' 
            }}
          >
            {levels.map((levelNodes, lIdx) => 
              levelNodes.map((node, nIdx) => 
                node.children?.map((child) => {
                  const x1 = lIdx * (NODE_WIDTH + LEVEL_GAP) + (NODE_WIDTH) + 100;
                  const y1 = nIdx * (NODE_HEIGHT + NODE_GAP) + (NODE_HEIGHT / 2) + 100;
                  const nextLevel = levels[lIdx + 1];
                  if (!nextLevel) return null;
                  
                  const targetNodeIdx = nextLevel.findIndex(n => n.id === child?.id);
                  if (targetNodeIdx === -1) return null;

                  const x2 = (lIdx + 1) * (NODE_WIDTH + LEVEL_GAP) + 100;
                  const y2 = targetNodeIdx * (NODE_HEIGHT + NODE_GAP) + (NODE_HEIGHT / 2) + 100;
                  const midX = (x1 + x2) / 2;
                  
                  return (
                    <path
                      key={`${node.id}-${child.id}`}
                      d={`M ${x1} ${y1} C ${midX} ${y1}, ${midX} ${y2}, ${x2} ${y2}`}
                      stroke="var(--primary)"
                      strokeWidth="1.5"
                      fill="none"
                    />
                  );
                })
              )
            )}
          </svg>

          <div className="flex gap-[100px] relative z-10">
            {levels.map((levelNodes, lIdx) => (
              <div key={lIdx} className="flex flex-col gap-6">
                {levelNodes.map((node) => (
                  <NodeItem key={node.id} node={node} />
                ))}
              </div>
            ))}
          </div>
        </div>
      </div>
    </div>
  );
};

const NodeItem: React.FC<{ node: ExecutionNode }> = ({ node }) => {
  const statusConfig = {
    Pending: { icon: Circle, color: 'text-[var(--muted)]', border: 'border-[var(--border)]' },
    Running: { icon: PlayCircle, color: 'text-blue-400', border: 'border-blue-500/30' },
    Completed: { icon: CheckCircle2, color: 'text-emerald-400', border: 'border-emerald-500/30' },
    Error: { icon: AlertCircle, color: 'text-red-400', border: 'border-red-500/30' },
  };

  const config = statusConfig[node.status as keyof typeof statusConfig] || statusConfig.Pending;
  const Icon = config.icon;

  return (
    <motion.div
      layout
      className={`
        flex items-center gap-2.5 px-3 py-2 w-[180px] h-[60px] glass-card border-l-2 ${config.border}
        transition-all duration-300 hover:bg-white/[0.05] relative group
      `}
    >
      <div className={`${config.color} shrink-0`}>
        {node.status === 'Running' ? (
          <Icon size={16} className="animate-spin-slow" />
        ) : (
          <Icon size={16} />
        )}
      </div>
      
      <div className="flex-1 min-w-0">
        <div className="text-[10px] font-bold font-display truncate uppercase tracking-widest text-[var(--text)] opacity-80 group-hover:opacity-100 transition-opacity flex items-center gap-1.5">
          {node.name}
          {node.iterationCount && node.iterationCount > 1 && (
            <span className="bg-indigo-500/20 text-indigo-400 px-1 rounded text-[8px] border border-indigo-500/20">
              x{node.iterationCount}
            </span>
          )}
        </div>
        <div className="flex items-center gap-2 mt-0.5 opacity-40 text-[8px] font-mono font-bold">
          <span className="flex items-center gap-0.5"><Clock size={8} />{node.durationMs ?? 0}ms</span>
          <span className="flex items-center gap-0.5"><Database size={8} />{node.rowsProcessed?.toLocaleString() ?? 0}</span>
        </div>
      </div>

      {node.status === 'Running' && (
        <div className="absolute inset-0 bg-blue-500/[0.03] animate-pulse rounded-lg pointer-events-none" />
      )}
    </motion.div>
  );
};
