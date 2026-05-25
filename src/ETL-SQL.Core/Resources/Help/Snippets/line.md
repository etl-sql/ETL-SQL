---
trigger: $line
label: CREATE VISUAL … AS LINE
description: Line chart for trends and time-series data
---
CREATE VISUAL «VisualName» AS LINE (
  SOURCE   = («SELECT * FROM #data»),
  MAPPINGS (X = «date_col», Y = «value»),
  OPTIONS  (SMOOTH = ON, AXIS_SORT = SOURCE, TITLE = '«Chart Title»')
);
