---
trigger: $donut
label: CREATE VISUAL … AS DONUT
description: Donut chart with proportional slices and center label
---
CREATE VISUAL «VisualName» AS DONUT (
  SOURCE   = («SELECT * FROM #data»),
  MAPPINGS (VALUE = «value», NAME = «name»),
  OPTIONS  (LEGEND = ON, TITLE = '«Chart Title»')
);
