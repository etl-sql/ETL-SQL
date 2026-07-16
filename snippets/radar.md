---
trigger: $radar
label: CREATE VISUAL … AS RADAR
description: Radar (spider) chart comparing multiple numeric axes per category
---
CREATE VISUAL «VisualName» AS RADAR (
  SOURCE   = («SELECT * FROM #data»),
  MAPPINGS (NAME = «category», VALUE = «value», AXIS = «metric»),
  OPTIONS  (LEGEND = ON, TITLE = '«Chart Title»')
);
