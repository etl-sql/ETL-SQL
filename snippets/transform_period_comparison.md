---
trigger: $transform-mom
label: TRANSFORM USING PERIOD_COMPARISON
description: Computes period-over-period difference and growth percentages (DoD, MoM, YoY).
---
TRANSFORM #«target_table»
FROM #«source_table»
USING PERIOD_COMPARISON (
  DATE_COL = '«DateColumn»',
  VALUE_COL = '«ValueColumn»',
  PERIOD = '«MONTH»',
  BY_GROUP = '«Category»',
  DIFF_COL = '«Value_Diff»',
  PCT_COL = '«Value_Pct»'
);
