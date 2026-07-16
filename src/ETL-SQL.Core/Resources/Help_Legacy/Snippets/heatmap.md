---
trigger: $heatmap
label: CREATE VISUAL … AS HEATMAP
description: Heatmap with X/Y category axes and color-encoded value
---
CREATE VISUAL «VisualName» AS HEATMAP (
  SOURCE   = («SELECT * FROM #data»),
  MAPPINGS (X = «x_column», Y = «y_column», VALUE = «value»),
  OPTIONS  (TITLE = '«Chart Title»')
);
