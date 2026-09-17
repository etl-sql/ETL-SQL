/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/studio-data.ts
 * Run: node scripts/sync-assets.js
 */
/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Snapshot metadata, local filtering, and host-backed sample loading for Studio.
 */
/**
 * Resolves the string name of a column from a string or column descriptor object.
 * @param {string|Object} column
 * @returns {string}
 */
export function columnName(column) {
    return typeof column === 'string' ? column : String(column?.name || column?.columnName || '');
}
/**
 * Infers whether a column is date, number, or text based on its declared type or sampled rows.
 * @param {string|Object} column
 * @param {Array<Object>} [rows]
 * @returns {string}
 */
export function columnType(column, rows = []) {
    const declared = typeof column === 'object' && column !== null ? String(column.type || column.dataType || '').toUpperCase() : '';
    if (/DATE|TIME/.test(declared))
        return 'date';
    if (/INT|DECIMAL|NUMERIC|FLOAT|DOUBLE|REAL|MONEY/.test(declared))
        return 'number';
    const name = columnName(column);
    const sample = rows.find(row => row?.[name] != null)?.[name];
    if (sample instanceof Date || (/date|time/i.test(name) && !Number.isNaN(Date.parse(sample))))
        return 'date';
    if (typeof sample === 'number')
        return 'number';
    return 'text';
}
/**
 * Extracts column descriptors from a snapshot's columns array or infers them from the first row.
 * @param {Object} snapshot
 * @returns {Array<string|Object>}
 */
export function snapshotColumns(snapshot) {
    if (Array.isArray(snapshot?.columns) && snapshot.columns.length)
        return snapshot.columns;
    const firstRow = snapshot?.rows?.[0];
    return firstRow && typeof firstRow === 'object'
        ? Object.keys(firstRow).map(name => ({ name, type: typeof firstRow[name] }))
        : [];
}
/**
 * Updates the context's snapshot package by applying active filters to the snapshot rows.
 * @param {Object} context
 * @param {Object} snapshot
 */
export function updateSnapshotPackage(context, snapshot) {
    const columns = snapshotColumns(snapshot).map(columnName);
    let rows = snapshot?.rows || [];
    rows = rows.filter(row => Object.entries(context.activeFilters).every(([field, filter]) => {
        if (!filter)
            return true;
        if (filter.kind === 'categorical')
            return !filter.values?.length || filter.values.includes(String(row?.[field]));
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
/**
 * Hydrates the design canvas from a compiled report preview. Unlike a one-source sample, a report
 * manifest carries the right rows and native SVG for each visual, which is required for scripts
 * that stage several #temp tables before drawing their dashboard.
 * @param {Object} context
 * @param {Object} manifest
 */
export function updateSnapshotPackageFromManifest(context, manifest) {
    const visuals = Array.isArray(manifest?.visuals) ? manifest.visuals : [];
    const sampleRows = {};
    const columnsByVisual = {};
    const visualSvgs = {};
    visuals.forEach((visual, index) => {
        const key = visual?.name || `visual${index}`;
        const rows = Array.isArray(visual?.rows) ? visual.rows : [];
        const columns = Array.isArray(visual?.columns) ? visual.columns : [];
        if (rows.length)
            sampleRows[key] = rows;
        if (columns.length)
            columnsByVisual[key] = columns;
        if (visual?.nativeSvg)
            visualSvgs[key] = visual.nativeSvg;
    });
    const representative = [...visuals]
        .filter(visual => Array.isArray(visual?.rows) && visual.rows.length)
        .sort((left, right) => (right.columns?.length || 0) - (left.columns?.length || 0))[0];
    const columns = representative?.columns || [];
    const objectRows = (representative?.rows || []).map(row => Object.fromEntries(columns.map((column, index) => [column, row?.[index]])));
    context.snapshot = {
        source: representative?.name || manifest?.title || 'report preview',
        columns,
        rowCount: objectRows.length,
        rows: objectRows,
    };
    Object.assign(context.snapshotPackage, {
        columns,
        columnsByVisual,
        sampleRows,
        visualSvgs,
        metadata: {
            isSampled: true,
            source: manifest?.title || null,
            rowCount: Object.values(sampleRows).reduce((total, rows) => total + rows.length, 0),
        },
    });
}
/**
 * Requests a source sample from the host API for a specified connection and table.
 * @param {Object} options
 * @returns {Promise<Object>}
 */
export async function requestSourceSample({ authFetch, url, connection, table, documentUri, script }) {
    const response = await authFetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ sourceKind: 'connection', connection, table, documentUri, script })
    });
    if (!response.ok)
        throw new Error(await response.text() || 'Data sample failed.');
    const sample = await response.json();
    return {
        source: sample.source || `${connection}.${table}`,
        columns: sample.columns || [],
        rowCount: sample.rowCount || sample.rows?.length || 0,
        rows: sample.rows || []
    };
}
