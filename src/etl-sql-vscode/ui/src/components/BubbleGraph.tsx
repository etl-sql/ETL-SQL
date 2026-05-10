import React from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { Check, X, Loader2 } from 'lucide-react';
import type { ExecutionNode } from '../types';

interface BubbleGraphProps {
  nodes: ExecutionNode[];
}

export const BubbleGraph: React.FC<BubbleGraphProps> = ({ nodes }) => {
  return (
    <div className="flex flex-wrap gap-2 p-2 min-h-[80px] items-center">
      <AnimatePresence mode="popLayout">
        {nodes.map((node) => (
          <motion.div
            key={node.id}
            layout
            initial={{ scale: 0, opacity: 0 }}
            animate={{ scale: 1, opacity: 1 }}
            exit={{ scale: 0, opacity: 0 }}
            transition={{ duration: 0.12 }}
            className={`
              relative flex flex-col justify-center
              w-28 h-12 border px-2 bg-[var(--vscode-editor-background,var(--bg))]
              ${node.status === 'Running' ? 'border-[var(--vscode-progressBar-background,#0e70c0)]' :
                node.status === 'Completed' ? 'border-[var(--vscode-testing-iconPassed,#73c991)]' :
                node.status === 'Faulted' ? 'border-[var(--color-err)]' : 'border-[var(--border)]'}
            `}
          >
            <div className="absolute top-1.5 right-1.5">
              {node.status === 'Completed' && (
                <Check size={12} className="text-[var(--vscode-testing-iconPassed,#73c991)]" />
              )}
              {node.status === 'Faulted' && (
                <X size={12} className="text-[var(--color-err)]" />
              )}
              {node.status === 'Running' && (
                <Loader2 size={12} className="animate-spin text-[var(--vscode-progressBar-background,#0e70c0)]" />
              )}
            </div>
            
            <span className="text-[11px] font-medium pr-4 overflow-hidden text-ellipsis whitespace-nowrap w-full">
              {node.name}
            </span>
            <span className="text-[10px] text-[var(--muted)]">
              {node.rowsProcessed > 0 ? `${(node.rowsProcessed / 1000).toFixed(1)}k` : ''}
            </span>
          </motion.div>
        ))}
      </AnimatePresence>
    </div>
  );
};
