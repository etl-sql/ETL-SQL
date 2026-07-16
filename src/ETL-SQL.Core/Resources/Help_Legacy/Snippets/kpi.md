---
trigger: $kpi
label: CREATE VISUAL … AS CARD
description: KPI card showing a single prominent metric with optional goal and trend
---
CREATE VISUAL «VisualName» AS CARD (
  SOURCE   = («SELECT «value_col», '«Label»' AS label FROM #data»),
  MAPPINGS (VALUE = «value_col», LABEL = label),
  OPTIONS  (FORMAT = 'N0', ABBREVIATE = ON, TITLE = '«KPI Title»')
);
