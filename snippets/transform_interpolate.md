---
trigger: $transform-interpolate
label: TRANSFORM USING INTERPOLATE
description: Fills missing numeric null values via linear progression or forward/backward fills.
---
TRANSFORM #«target_table»
FROM #«source_table»
USING INTERPOLATE (
  VALUE_COL = '«ValueColumn»',
  ORDER_COL = '«OrderColumn»',
  METHOD = '«LINEAR»',
  BY_GROUP = '«Category»'
);
