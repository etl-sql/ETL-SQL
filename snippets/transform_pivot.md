---
trigger: $transform-pivot
label: TRANSFORM USING PIVOT
description: Rotates category rows into columns to construct cross-tabulation matrix summaries.
---
TRANSFORM #«target_table»
FROM #«source_table»
USING PIVOT (
  ROW_FIELDS = '«RowColumn»',
  PIVOT_FIELD = '«PivotColumn»',
  VALUE_FIELD = '«ValueColumn»',
  AGGREGATE = '«SUM»'
);
