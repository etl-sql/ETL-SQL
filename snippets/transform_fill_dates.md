---
trigger: $transform-fill-dates
label: TRANSFORM USING FILL_DATES
description: Fills missing calendar dates in daily time-series with default/zero values across groups.
---
TRANSFORM #«target_table»
FROM #«source_table»
USING FILL_DATES (
  DATE_COL = '«DateColumn»',
  GAPS_FILL = «0»,
  BY_GROUP = '«Category»'
);
