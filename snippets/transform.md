---
trigger: $transform
label: TRANSFORM #target FROM #source
description: Applies a table-level transformation algorithm (e.g. FILL_DATES) to a source table.
---
TRANSFORM #«target»
FROM #«source»
USING «FILL_DATES» (
  DATE_COL = '«OrderDate»',
  GAPS_FILL = «0»,
  BY_GROUP = '«Region»'
);
