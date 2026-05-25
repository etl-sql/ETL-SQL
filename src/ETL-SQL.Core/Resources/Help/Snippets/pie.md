---
trigger: $pie
label: CREATE VISUAL … AS PIE
description: Pie chart with slices proportional to each value
---
CREATE VISUAL «VisualName» AS PIE (
  SOURCE   = («SELECT * FROM #data»),
  MAPPINGS (VALUE = «value», NAME = «name»),
  OPTIONS  (LEGEND = ON, TITLE = '«Chart Title»')
);
