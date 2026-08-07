import React, { useMemo, useState } from 'react';
import {
  useReactTable,
  getCoreRowModel,
  getFilteredRowModel,
  flexRender,
  createColumnHelper,
  type ColumnFiltersState,
} from '@tanstack/react-table';
import type { Column } from '@tanstack/react-table';
import { Download, Search, Filter } from 'lucide-react';

interface ResultGridProps {
  rows: Record<string, unknown>[];
  columns: string[];
}

export const ResultGrid: React.FC<ResultGridProps> = ({ rows, columns }) => {
  const columnHelper = useMemo(() => createColumnHelper<Record<string, unknown>>(), []);
  const [columnFilters, setColumnFilters] = useState<ColumnFiltersState>([]);
  const [showFilters, setShowFilters] = useState(false);

  // Per-column date format decision: a column is "date-only" only when EVERY Date
  // value in that column has a zero time component. If even one value has a
  // non-midnight time the whole column renders as datetime, keeping midnight rows
  // consistent with their neighbours.
  const dateOnlyCols = useMemo(() => {
    const result = new Set<string>();
    for (const col of columns) {
      let hasDate = false;
      let hasTime = false;
      for (const row of rows) {
        const v = row[col];
        if (v instanceof Date) {
          hasDate = true;
          if (v.getHours() !== 0 || v.getMinutes() !== 0 || v.getSeconds() !== 0 || v.getMilliseconds() !== 0) {
            hasTime = true;
            break; // one non-midnight value is enough — whole column is datetime
          }
        }
      }
      if (hasDate && !hasTime) result.add(col);
    }
    return result;
  }, [rows, columns]);

  const formatDate = (val: Date, colName: string): string =>
    dateOnlyCols.has(colName)
      ? val.toISOString().slice(0, 10)                          // YYYY-MM-DD
      : val.toISOString().replace('T', ' ').replace('Z', '');   // YYYY-MM-DD HH:mm:ss.sss

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
            if (val instanceof Date) {
              return formatDate(val, col);
            }
            return typeof val === 'object' ? JSON.stringify(val) : val;
          },
          footer: col,
        })
      ),
    // eslint-disable-next-line react-hooks/exhaustive-deps -- formatDate is stable per render; dateOnlyCols included via closure
    [columns, columnHelper, dateOnlyCols]
  );

  // eslint-disable-next-line react-hooks/incompatible-library -- TanStack Table v8 not yet compatible with React Compiler
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

  const csvEscape = (val: unknown, col: string): string => {
    let s: string;
    if (val == null) {
      s = '';
    } else if (val instanceof Date) {
      s = formatDate(val, col);
    } else {
      s = String(val);
    }
    return s.includes(',') || s.includes('"') || s.includes('\n') || s.includes('\r')
      ? `"${s.replace(/"/g, '""')}"` : s;
  };

  const exportToCSV = () => {
    const lines = [
      columns.map((c) => csvEscape(c, c)).join(','),
      ...rows.map(row => columns.map(col => csvEscape(row[col], col)).join(','))
    ].join('\r\n');

    const blob = new Blob([lines], { type: 'text/csv' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = `etl_results_${new Date().getTime()}.csv`;
    link.click();
    URL.revokeObjectURL(url);
  };

  if (!columns || columns.length === 0) return null;

  return (
    <div className="flex flex-row h-full min-h-0 gap-1">
      {/* Table Main Area */}
      <div className="flex-1 overflow-auto scrollbar-fancy border border-[var(--border)] bg-[var(--bg)]">
        <table className="w-full text-left border-collapse text-[13px] font-sans">
          <thead className="sticky top-0 bg-[var(--vscode-editorGroupHeader-tabsBackground,var(--bg-darker))] z-10">
            {table.getHeaderGroups().map((headerGroup) => (
              <tr key={headerGroup.id}>
                {headerGroup.headers.map((header) => (
                  <th
                    key={header.id}
                    className="px-2 py-1 border-b border-[var(--border)]"
                  >
                    <div className="flex flex-col gap-0.5">
                       <span className="font-semibold text-[var(--text)] text-[11px] leading-tight">
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
            {table.getRowModel().rows.length === 0 ? (
              <tr>
                <td colSpan={columns.length} className="px-2 py-4 text-center text-[var(--muted)] text-[11px] italic opacity-50">
                  0 rows returned
                </td>
              </tr>
            ) : (
              table.getRowModel().rows.map((row) => (
                <tr key={row.id} className="hover:bg-[var(--vscode-list-hoverBackground,rgba(90,93,94,0.18))] group">
                  {row.getVisibleCells().map((cell) => (
                    <td
                      key={cell.id}
                      className="px-2 py-0.5 border-b border-[var(--border)] whitespace-nowrap overflow-hidden text-ellipsis max-w-[300px] text-[var(--text-primary)]"
                    >
                      <span className="font-mono">{flexRender(cell.column.columnDef.cell, cell.getContext())}</span>
                    </td>
                  ))}
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>

      {/* Side-Aligned Toolbar (Zero-Waste) */}
      <div className="flex flex-col gap-1 shrink-0">
        <button 
          onClick={() => setShowFilters(!showFilters)}
          className={`flex items-center justify-center p-1.5 border ${showFilters ? 'bg-[var(--vscode-list-activeSelectionBackground,rgba(90,93,94,0.31))] border-[var(--vscode-focusBorder,#007fd4)] text-[var(--text)]' : 'bg-transparent border-[var(--border)] text-[var(--muted)] hover:bg-[var(--vscode-toolbar-hoverBackground,rgba(90,93,94,0.31))] hover:text-[var(--text)]'}`}
          title="Toggle Column Filtering"
        >
          <Filter size={12} />
        </button>

        <button 
          onClick={exportToCSV}
          className="flex items-center justify-center p-1.5 border border-[var(--border)] text-[var(--muted)] hover:text-[var(--text)] hover:bg-[var(--vscode-toolbar-hoverBackground,rgba(90,93,94,0.31))]"
          title="Export to CSV"
        >
          <Download size={12} />
        </button>
      </div>
    </div>
  );
};

function FilterInput({ column }: { column: Column<Record<string, unknown>, unknown> }) {
  const columnFilterValue = column.getFilterValue();

  return (
    <div className="relative group">
      <Search size={10} className="absolute left-2 top-1/2 -translate-y-1/2 text-[var(--muted)]" />
      <input
        type="text"
        value={(columnFilterValue ?? '') as string}
        onChange={(e) => column.setFilterValue(e.target.value)}
        placeholder={`Filter...`}
        className="w-full bg-[var(--vscode-input-background,var(--bg-darker))] border border-[var(--vscode-input-border,var(--border))] px-6 py-0.5 text-[11px] text-[var(--vscode-input-foreground,var(--text))] focus:outline-none focus:border-[var(--vscode-focusBorder,#007fd4)] placeholder:opacity-60"
      />
    </div>
  );
}
