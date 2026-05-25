---
trigger: $bar
label: CREATE VISUAL … AS BAR
description: Bar chart visual with category X axis and numeric Y axis
---
CREATE VISUAL «VisualName» AS BAR (
  SOURCE   = («SELECT * FROM #data»),
  MAPPINGS (X = «category», Y = «value»),
  OPTIONS  (AXIS_SORT = VALUE_DESC, TITLE = '«Chart Title»')
);
