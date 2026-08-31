/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Snapshot metadata, local filtering, and host-backed sample loading for Studio.
 */

export function columnName(column) {
    return typeof column === 'string' ? column : String(column?.name || column?.columnName || '');
}

export function columnType(column, rows = []) {
    const declared = typeof column === 'object' ? String(column?.type || column?.dataType || '').toUpperCase() : '';
    if (/DATE|TIME/.test(declared)) return 'date';
    if (/INT|DECIMAL|NUMERIC|FLOAT|DOUBLE|REAL|MONEY/.test(declared)) return 'number';
    const name = columnName(column);
    const sample = rows.find(row => row?.[name] != null)?.[name];
    if (sample instanceof Date || (/date|time/i.test(name) && !Number.isNaN(Date.parse(sample)))) return 'date';
    if (typeof sample === 'number') return 'number';
    return 'text';
}

export function snapshotColumns(snapshot) {
    if (Array.isArray(snapshot?.columns) && snapshot.columns.length) return snapshot.columns;
    const firstRow = snapshot?.rows?.[0];
    return firstRow && typeof firstRow === 'object'
        ? Object.keys(firstRow).map(name => ({ name, type: typeof firstRow[name] }))
        : [];
}

export function updateSnapshotPackage(context, snapshot) {
    const columns = snapshotColumns(snapshot).map(columnName);
    let rows = snapshot?.rows || [];
    rows = rows.filter(row => Object.entries(context.activeFilters).every(([field, filter]) => {
        if (!filter) return true;
        if (filter.kind === 'categorical') return !filter.values?.length || filter.values.includes(String(row?.[field]));
        if (filter.kind === 'number') {
            const value = Number(row?.[field]);
            return Number.isFinite(value)
                && (filter.minimum == null || value >= Number(filter.minimum))
                && (filter.maximum == null || value <= Number(filter.maximum));
        }
        if (filter.kind === 'date') {
            const value = String(row?.[field] || '').slice(0, 10);
            return (!filter.minimum || value >= filter.minimum) && (!filter.maximum || value <= filter.maximum);
        }
        return true;
    }));
    context.snapshotPackage.columns = columns;
    context.snapshotPackage.sampleRows = snapshot?.source
        ? { [snapshot.source]: rows.map(row => columns.map(column => row?.[column])) }
        : {};
    context.snapshotPackage.metadata = { isSampled: true, source: snapshot?.source || null, rowCount: rows.length };
}

export async function requestSourceSample({ authFetch, url, connection, table, documentUri, script }) {
    const response = await authFetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ sourceKind: 'connection', connection, table, documentUri, script })
    });
    if (!response.ok) throw new Error(await response.text() || 'Data sample failed.');
    const sample = await response.json();
    return {
        source: sample.source || `${connection}.${table}`,
        columns: sample.columns || [],
        rowCount: sample.rowCount || sample.rows?.length || 0,
        rows: sample.rows || []
    };
}
