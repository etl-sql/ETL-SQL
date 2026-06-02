// Shared helpers for the UI sandbox stories.

// Canonical designer component module (the file you edit). Imported fresh on every
// mount so editing it + hitting ↻ Reload shows changes with no sync / portal build.
export const DESIGNER_JS = '/src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer.js';

// Dynamic import with a cache-bust query so the browser re-fetches the latest source.
export function importFresh(path) {
  return import(`${path}?t=${Date.now()}`);
}
