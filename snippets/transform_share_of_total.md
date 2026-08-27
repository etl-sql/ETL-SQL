---
trigger: $transform-share
label: TRANSFORM USING SHARE_OF_TOTAL
description: Computes percentage contribution of numeric values relative to group or grand total.
---
TRANSFORM #«target_table»
FROM #«source_table»
USING SHARE_OF_TOTAL (
  VALUE_COL = '«ValueColumn»',
  BY_GROUP = '«Category»',
  SHARE_COL = '«Value_Share»'
);
