---
trigger: $waterfall
label: CREATE VISUAL … AS WATERFALL
description: Waterfall chart showing cumulative contributions to a total
---
CREATE VISUAL «VisualName» AS WATERFALL (
  SOURCE   = («SELECT * FROM #data»),
  MAPPINGS (LABEL = «category», VALUE = «delta»),
  OPTIONS  (TITLE = '«Chart Title»')
);
