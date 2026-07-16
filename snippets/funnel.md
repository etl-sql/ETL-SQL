---
trigger: $funnel
label: CREATE VISUAL … AS FUNNEL
description: Funnel chart showing stage-by-stage drop-off
---
CREATE VISUAL «VisualName» AS FUNNEL (
  SOURCE   = («SELECT * FROM #data»),
  MAPPINGS (LABEL = «stage», VALUE = «count»),
  OPTIONS  (TITLE = '«Chart Title»')
);
