---
trigger: $scatter
label: CREATE VISUAL … AS SCATTER
description: Scatter plot with numeric X and Y axes and optional bubble size
---
CREATE VISUAL «VisualName» AS SCATTER (
  SOURCE   = («SELECT * FROM #data»),
  MAPPINGS (X = «x_column», Y = «y_column», GROUP = «category»),
  OPTIONS  (TITLE = '«Chart Title»')
);
