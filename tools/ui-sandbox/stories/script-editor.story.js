// Story: the lite rptsql editor (createScriptEditor — CodeMirror, no server).
import { importFresh, DESIGNER_JS } from '../util.js';

const SCRIPTS = {
  report:
`CREATE DATASET &sales AS (
  SELECT Date, Vendor, SUM(Amount) AS total
  FROM edw.Sales
);

CREATE VISUAL salesBar AS BAR (
  SOURCE = &sales,
  MAPPINGS (X = Date, Y = total, SERIES = Vendor)
);

CREATE PAGE Overview AS DASHBOARD( STRUCTURE = 'A', MAP ( 'A' = salesBar ) );`,
  etl:
`LOAD 'orders.csv' INTO #orders;

SELECT
  region,
  SUM(total) AS revenue  /* @d: total revenue by region; @pii: false; */
INTO #summary
FROM #orders
WHERE total > 0
GROUP BY region;`,
  empty: '',
};

export default {
  id: 'script-editor',
  title: 'Script editor',
  subtitle: 'createScriptEditor()',
  fixtures: [
    { id: 'report', label: 'Report SQL' },
    { id: 'etl',    label: 'ETL SQL (with tags)' },
    { id: 'empty',  label: 'Empty' },
  ],
  async mount(stage, fixtureId, ctx) {
    const value = SCRIPTS[fixtureId] ?? '';
    const mod = await importFresh(DESIGNER_JS);
    const ed = await mod.createScriptEditor(stage, {
      value,
      onChange: (v) => ctx.stat(`${v.length} chars · ${v.split('\n').length} lines`),
    });
    ctx.stat(`${value.length} chars · ${(value ? value.split('\n').length : 0)} lines`);
    return { dispose: () => ed.dispose() };
  },
};
