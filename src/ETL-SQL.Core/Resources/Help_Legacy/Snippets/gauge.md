---
trigger: $gauge
label: CREATE VISUAL … AS GAUGE
description: Gauge chart showing a single value against a target range
---
CREATE VISUAL «VisualName» AS GAUGE (
  SOURCE   = («SELECT «value_col» FROM #data»),
  MAPPINGS (VALUE = «value_col»),
  OPTIONS  (MIN = 0, MAX = «100», TARGET = «goal», TITLE = '«Gauge Title»')
);
