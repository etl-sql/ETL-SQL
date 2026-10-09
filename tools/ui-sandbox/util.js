// Shared helpers for the UI sandbox stories.

// Canonical designer delivery modules. The sandbox Reload button opens a fresh document
// so transitive imports are refreshed too. TypeScript owners need successful compilation first.
export const DESIGNER_JS = '/src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer.js';
export const STUDIO_JS = '/src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio.js';

// Re-fetch the entry when mounting a fixture. Reload clears its dependencies' module cache.
export function importFresh(path) {
  return import(`${path}?t=${Date.now()}`);
}
