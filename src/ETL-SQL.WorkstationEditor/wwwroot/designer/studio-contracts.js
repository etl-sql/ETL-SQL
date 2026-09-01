/* GENERATED FILE - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-contracts.js
 * Edit the canonical source, then run: node .\scripts\sync-assets.js
 */

/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Stable host routes and script templates consumed by the Studio composition layer.
 */

export const STUDIO_ROUTES = Object.freeze({
    analyze: '/api/designer/analyze',
    complete: '/api/designer/complete',
    hover: '/api/designer/hover',
    format: '/api/designer/format',
    run: '/api/designer/run',
    dag: '/api/designer/dag',
    pipelineTask: '/api/designer/pipeline-task',
    parse: '/api/designer/parse',
    patch: '/api/designer/patch',
    queryFilter: '/api/designer/query-filter',
    optionSource: '/api/designer/option-source',
    dataSample: '/api/designer/data-sample',
    schema: '/api/designer/schema',
    sessionMetadata: '/api/session/metadata',
    connectorsSchema: '/api/connectors/schema',
});

export const STUDIO_CATALOG_ROUTES = Object.freeze({
    datasetRegistry: '/api/datasets',
});

export const STUDIO_WORKSPACE_ROUTES = Object.freeze({
    files: '/api/files',
    connections: '/api/connections',
});

export const STUDIO_STARTER_SCRIPTS = Object.freeze({
    report: `-- Sample dashboard. MOCKDB is a built-in in-memory connector, so this needs no database.
-- Replace the connection below with your own when you are ready.
SET REPORT TITLE = 'Sample Dashboard';

CREATE CONNECTION demo AS MOCKDB();

SELECT Region, SUM(Total) AS Revenue
INTO #revenue_by_region
FROM demo.Orders
GROUP BY Region;

CREATE VISUAL revenue_by_region AS BAR (
    SOURCE = #revenue_by_region,
    MAPPINGS (X = Region, Y = Revenue),
    OPTIONS (LEGEND = OFF)
);
`,
    etl: `-- Sample pipeline. MOCKDB is a built-in in-memory connector, so this needs no database.
-- Replace the connection below with your own when you are ready.
CREATE CONNECTION demo AS MOCKDB();

-- Stage the rows you care about in a #temp table.
SELECT SaleID, OrderDate, Region, Total
INTO #recent_sales
FROM demo.Orders
WHERE Total > 100;

-- Summarise the staged rows.
SELECT Region, COUNT(*) AS Orders, SUM(Total) AS Revenue
INTO #revenue_by_region
FROM #recent_sales
GROUP BY Region;

SELECT * FROM #revenue_by_region;
`,
    sql: `-- MOCKDB is a built-in in-memory connector, so this needs no database.
CREATE CONNECTION demo AS MOCKDB();

SELECT Region, COUNT(*) AS Orders, SUM(Total) AS Revenue
FROM demo.Orders
GROUP BY Region;
`,
});

export const REPORT_WORKFLOW_TEMPLATES = Object.freeze({
    dashboard: `-- Dashboard canvas: add data, then arrange charts, KPIs, tables, and slicers.
CREATE PAGE [Dashboard] AS DASHBOARD ( LAYOUT ( STRUCTURE = '.' ) );
`,
    paginated: `-- Paginated report: build detail bands for a fixed physical page.
CREATE PAGE [Paginated Report] AS PAGINATED (
  LAYOUT ( STRUCTURE = '.' ),
  PRINT_LAYOUT (
    PAGE_SIZE = 'Letter',
    ORIENTATION = 'PORTRAIT',
    MARGINS = (0.75, 0.75, 0.75, 0.75),
    UNITS = 'in',
    OVERFLOW = 'SPLIT'
  )
);
`,
});
