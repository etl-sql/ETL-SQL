/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * GENERATED FILE - DO NOT EDIT.
 * Source: named Studio endpoints registered by Portal and WorkstationEditor.
 * Regenerate: node scripts/generate-browser-contracts.mjs
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
    seal: "/api/designer/seal",
    sessionMetadata: "/api/session/metadata",
} as const);

export const STUDIO_CATALOG_ROUTES = Object.freeze({
    datasetRegistry: "/api/datasets",
} as const);

export const STUDIO_WORKSPACE_ROUTES = Object.freeze({
    connectHostedServer: "/api/hosted/connect",
    connections: "/api/connections",
    disconnectHostedServer: "/api/hosted/disconnect",
    files: "/api/files",
    hostedPipelines: "/api/hosted/pipelines",
    hostedRunStatus: (value: string) => "/api/hosted/runs/" + encodeURIComponent(value),
    hostedStatus: "/api/hosted/status",
    recoverHostedRun: "/api/hosted/runs/recover",
    removeHostedServer: (value: string) => "/api/hosted/servers/" + encodeURIComponent(value),
    saveHostedServer: "/api/hosted/servers",
    startHostedRun: "/api/hosted/runs",
    stopHostedRun: "/api/hosted/runs/stop",
} as const);

