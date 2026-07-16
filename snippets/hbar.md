---
trigger: $hbar
label: CREATE VISUAL … AS HBAR
description: Horizontal bar chart with category Y axis and numeric X axis
---
CREATE VISUAL «VisualName» AS HBAR (
  SOURCE   = («SELECT * FROM #data»),
  MAPPINGS (Y = «category», X = «value»),
  OPTIONS  (AXIS_SORT = VALUE_DESC, TITLE = '«Chart Title»')
);
