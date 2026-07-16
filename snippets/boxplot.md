---
trigger: $boxplot
label: CREATE VISUAL … AS BOXPLOT
description: Box and whisker plot showing distribution statistics per category
---
CREATE VISUAL «VisualName» AS BOXPLOT (
  SOURCE   = («SELECT * FROM #data»),
  MAPPINGS (CATEGORY = «category», VALUE = «value»),
  OPTIONS  (TITLE = '«Chart Title»')
);
