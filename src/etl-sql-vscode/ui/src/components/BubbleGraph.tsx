import React from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { Check, X, Loader2 } from 'lucide-react';
import type { ExecutionNode } from '../types';

interface BubbleGraphProps {
  nodes: ExecutionNode[];
}

export const BubbleGraph: React.FC<BubbleGraphProps> = ({ nodes }) => {
  return (
    <div className="flex flex-wrap gap-4 p-4 min-h-[100px] items-center justify-center">
      <AnimatePresence mode="popLayout">
        {nodes.map((node) => (
          <motion.div
            key={node.id}
            layout
            initial={{ scale: 0, opacity: 0 }}
            animate={{ scale: 1, opacity: 1 }}
            exit={{ scale: 0, opacity: 0 }}
            transition={{ type: "spring", stiffness: 260, damping: 20 }}
            className={`
              relative flex flex-col items-center justify-center 
              w-16 h-16 rounded-full border-2 transition-all duration-500
              ${node.status === 'Running' ? 'node-running animate-pulse' : 
                node.status === 'Completed' ? 'node-completed bg-emerald-500/10' : 
                node.status === 'Error' ? 'node-error bg-red-500/10' : 'node-pending'}
              glass-panel
            `}
          >
            <div className="absolute -top-1 -right-1">
              {node.status === 'Completed' && (
                <div className="bg-emerald-500 rounded-full p-1 shadow-lg">
                  <Check size={10} className="text-white" />
                </div>
              )}
              {node.status === 'Error' && (
                <div className="bg-red-500 rounded-full p-1 shadow-lg">
                  <X size={10} className="text-white" />
                </div>
              )}
              {node.status === 'Running' && (
                <div className="bg-blue-500 rounded-full p-1 shadow-lg animate-spin">
                  <Loader2 size={10} className="text-white" />
                </div>
              )}
            </div>
            
            <span className="text-[10px] font-bold text-center px-1 overflow-hidden text-ellipsis whitespace-nowrap w-full">
              {node.name}
            </span>
            <span className="text-[8px] opacity-50">
              {node.rowsProcessed > 0 ? `${(node.rowsProcessed / 1000).toFixed(1)}k` : ''}
            </span>
          </motion.div>
        ))}
      </AnimatePresence>
    </div>
  );
};
