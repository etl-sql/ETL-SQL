---
trigger: $transform-rolling
label: TRANSFORM USING ROLLING_AGGREGATE
description: Calculates rolling averages, moving sums, and cumulative time-series trends.
---
TRANSFORM #«target_table»
FROM #«source_table»
USING ROLLING_AGGREGATE (
  VALUE_COL = '«ValueColumn»',
  ORDER_COL = '«DateColumn»',
  WINDOW_SIZE = «7»,
  AGGREGATE = '«AVG»',
  BY_GROUP = '«Category»',
  ROLLING_COL = '«Value_Rolling»'
);
