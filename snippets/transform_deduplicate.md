---
trigger: $transform-dedup
label: TRANSFORM USING DEDUPLICATE
description: Removes duplicate rows based on key columns with deterministic sorting and keep selection.
---
TRANSFORM #«target_table»
FROM #«source_table»
USING DEDUPLICATE (
  KEY_COLS = '«KeyColumn»',
  ORDER_BY = '«Priority DESC»',
  KEEP = '«FIRST»'
);
