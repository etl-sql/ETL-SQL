---
trigger: $tooltip
label: TOOLTIP detail popover
description: Chart-in-popover detail surface driven by the activated row
---
CREATE VISUAL «DetailVisual» AS BAR (
  SOURCE = (SELECT «Category», «Measure» FROM #«source» WHERE «KeyColumn» = @hover_value),
  MAPPINGS (X = «Category», Y = «Measure»)
);

CREATE CONTAINER «DetailBox» AS BOX (
  LAYOUT (
    STRUCTURE = 'A',
    MAP ('A' = «DetailVisual»)
  )
);

CREATE VISUAL «TriggerVisual» AS BAR (
  SOURCE = (SELECT «KeyColumn», SUM(«Measure») AS «Measure» FROM #«source» GROUP BY «KeyColumn»),
  MAPPINGS (X = «KeyColumn», Y = «Measure»),
  TOOLTIP = «DetailBox»
);
