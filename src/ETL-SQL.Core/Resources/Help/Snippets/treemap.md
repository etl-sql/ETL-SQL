---
trigger: $treemap
label: CREATE VISUAL … AS TREEMAP
description: Treemap showing hierarchical proportions as nested rectangles
---
CREATE VISUAL «VisualName» AS TREEMAP (
  SOURCE   = («SELECT * FROM #data»),
  MAPPINGS (LABEL = «category», VALUE = «value», PARENT = «parent»),
  OPTIONS  (TITLE = '«Chart Title»')
);
