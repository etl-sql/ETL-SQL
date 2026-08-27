---
trigger: $transform-normalize
label: TRANSFORM USING NORMALIZE
description: Scales numeric columns to a standard range via MIN_MAX [0, 1] or Z_SCORE standardization.
---
TRANSFORM #«target_table»
FROM #«source_table»
USING NORMALIZE (
  VALUE_COL = '«ValueColumn»',
  METHOD = '«MIN_MAX»',
  BY_GROUP = '«Category»',
  NORM_COL = '«Value_Normalized»'
);
