import React, { useMemo, useState } from 'react';
import {
  useReactTable,
  getCoreRowModel,
  getFilteredRowModel,
  flexRender,
  createColumnHelper,
} from '@tanstack/react-table';
import type { Column } from '@tanstack/react-table';
import { Download, Search, Filter } from 'lucide-react';

interface ResultGridProps {
  rows: any[];
  columns: string[];
}

export const ResultGrid: React.FC<ResultGridProps> = ({ rows, columns }) => {
  const columnHelper = createColumnHelper<any>();
  const [columnFilters, setColumnFilters] = useState<any[]>([]);
  const [showFilters, setShowFilters] = useState(false);

  const tableColumns = useMemo(
    () =>
      columns.map((col) =>
        columnHelper.accessor(col, {
          header: col,
          cell: (info) => {
            const val = info.getValue();
            if (val === null || val === undefined) {
              return <span className="opacity-30 italic text-[10px] tracking-tighter">NULL</span>;
            }
            return typeof val === 'object' ? JSON.stringify(val) : val;
          },
          footer: col,
        })
      ),
    [columns, columnHelper]
  );

  const table = useReactTable({
    data: rows,
    columns: tableColumns,
    state: {
      columnFilters,
    },
    onColumnFiltersChange: setColumnFilters,
    getCoreRowModel: getCoreRowModel(),
    getFilteredRowModel: getFilteredRowModel(),
  });

  const exportToCSV = () => {
    const headers = columns.join(',');
    const csvContent = rows.map(row => 
      columns.map(col => {
        const val = row[col];
        return typeof val === 'string' && val.includes(',') ? `"${val}"` : val;
      }).join(',')
    ).join('\n');
    
    const blob = new Blob([`${headers}\n${csvContent}`], { type: 'text/csv' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = `etl_results_${new Date().getTime()}.csv`;
    link.click();
  };

  if (!rows || rows.length === 0) return null;

  return (
    <div className="flex flex-row h-full min-h-0 animate-fade-in gap-1 p-1">
      {/* Table Main Area */}
      <div className="flex-1 overflow-auto scrollbar-fancy border border-[var(--border)] rounded bg-[var(--bg)]">
        <table className="w-full text-left border-collapse text-[14px] font-sans">
          <thead className="sticky top-0 bg-[var(--bg-darker)]/90 backdrop-blur-md shadow-sm z-10">
            {table.getHeaderGroups().map((headerGroup) => (
              <tr key={headerGroup.id}>
                {headerGroup.headers.map((header) => (
                  <th
                    key={header.id}
                    className="px-2 py-1 border-b border-white/10"
                  >
                    <div className="flex flex-col gap-0.5">
                       <span className="font-bold text-indigo-400 uppercase tracking-widest text-[9px] font-display leading-tight">
                         {flexRender(header.column.columnDef.header, header.getContext())}
                       </span>
                       {showFilters && <FilterInput column={header.column} />}
                    </div>
                  </th>
                ))}
              </tr>
            ))}
          </thead>
          <tbody>
            {table.getRowModel().rows.map((row) => (
              <tr key={row.id} className="hover:bg-indigo-500/[0.03] transition-colors group">
                {row.getVisibleCells().map((cell) => (
                  <td
                    key={cell.id}
                    className="px-2 py-0.5 border-b border-[var(--border)] whitespace-nowrap overflow-hidden text-ellipsis max-w-[300px] text-[var(--text-primary)] opacity-90 group-hover:opacity-100 transition-opacity"
                  >
                    <span className="font-mono">{flexRender(cell.column.columnDef.cell, cell.getContext())}</span>
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {/* Side-Aligned Toolbar (Zero-Waste) */}
      <div className="flex flex-col gap-1 shrink-0 px-0.5 pt-0.5">
        <button 
          onClick={() => setShowFilters(!showFilters)}
          className={`flex items-center justify-center p-2 rounded border transition-all ${showFilters ? 'bg-indigo-500/20 border-indigo-500/50 text-indigo-400' : 'bg-white/5 border-white/10 text-[var(--muted)] hover:bg-white/10'}`}
          title="Toggle Column Filtering"
        >
          <Filter size={12} />
        </button>

        <button 
          onClick={exportToCSV}
          className="flex items-center justify-center p-2 rounded bg-indigo-500/10 border border-indigo-500/20 text-indigo-400 hover:bg-indigo-500/20 transition-all group"
          title="Export to CSV"
        >
          <Download size={12} className="group-hover:translate-y-0.5 transition-transform" />
        </button>
      </div>
    </div>
  );
};

function FilterInput({ column }: { column: Column<any, any> }) {
  const columnFilterValue = column.getFilterValue();

  return (
    <div className="relative group">
      <Search size={10} className="absolute left-2 top-1/2 -translate-y-1/2 text-white/20 group-focus-within:text-indigo-400 transition-colors" />
      <input
        type="text"
        value={(columnFilterValue ?? '') as string}
        onChange={(e) => column.setFilterValue(e.target.value)}
        placeholder={`Filter...`}
        className="w-full bg-[var(--bg-darker)]/40 border border-[var(--border)] rounded px-6 py-1 text-[10px] focus:outline-none focus:border-[var(--primary)]/30 focus:bg-[var(--bg-darker)]/60 transition-all placeholder:opacity-20"
      />
    </div>
  );
}
