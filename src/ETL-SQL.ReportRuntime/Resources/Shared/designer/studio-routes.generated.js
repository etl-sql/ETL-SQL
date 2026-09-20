/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/studio-routes.generated.ts
 * Run: node scripts/sync-assets.js
 */
/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * GENERATED FILE - DO NOT EDIT.
 * Source: named Studio endpoints registered by Portal and WorkstationEditor.
 * Regenerate: ETLSQL_UPDATE_BROWSER_CONTRACTS=1 dotnet test tests/ETL-SQL.Portal.Tests
 *   --filter FullyQualifiedName~BrowserContractsGeneratorTests
 * Then run node scripts/sync-assets.js.
 */
export const STUDIO_ROUTES = Object.freeze({
    analyze: "/api/designer/analyze",
    complete: "/api/designer/complete",
    connectorsSchema: "/api/connectors/schema",
    dag: "/api/designer/dag",
    dataModel: "/api/designer/data-model",
    dataSample: "/api/designer/data-sample",
    format: "/api/designer/format",
    governance: "/api/designer/governance",
    hover: "/api/designer/hover",
    optionSource: "/api/designer/option-source",
    parse: "/api/designer/parse",
    patch: "/api/designer/patch",
    pipelineRunPlan: "/api/designer/pipeline-run-plan",
    pipelineScope: "/api/designer/pipeline-scope",
    pipelineTask: "/api/designer/pipeline-task",
    preview: "/api/designer/preview",
    previewAs: "/api/designer/preview-as",
    previewPdf: "/api/designer/preview/pdf",
    queryFilter: "/api/designer/query-filter",
    run: "/api/designer/run",
    schema: "/api/designer/schema",
    sessionMetadata: "/api/session/metadata",
});
export const STUDIO_CATALOG_ROUTES = Object.freeze({
    datasetRegistry: "/api/datasets",
});
export const STUDIO_WORKSPACE_ROUTES = Object.freeze({
    connections: "/api/connections",
    files: "/api/files",
});
