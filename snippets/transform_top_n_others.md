---
trigger: $transform-top-n
label: TRANSFORM USING TOP_N_OTHERS
description: Ranks top N categories and aggregates remaining low-volume categories into 'Others'.
---
TRANSFORM #«target_table»
FROM #«source_table»
USING TOP_N_OTHERS (
  N = «5»,
  VALUE_COL = '«ValueColumn»',
  CATEGORY_COL = '«CategoryColumn»',
  OTHERS_LABEL = '«Others»',
  AGGREGATE = '«SUM»',
  BY_GROUP = '«Region»'
);
