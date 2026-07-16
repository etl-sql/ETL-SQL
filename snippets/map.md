---
trigger: $map
label: CREATE VISUAL … AS MAP
description: Geographic choropleth or points map
---
CREATE VISUAL «VisualName» AS MAP (
  SOURCE   = («SELECT * FROM #data»),
  MAPPINGS (REGION = «region_col», VALUE = «value_col»),
  OPTIONS  (MAP_NAME = '«WORLD»', TITLE = '«Map Title»')
);
