---
trigger: $style
label: CREATE STYLE &name
description: Reusable visual styling definition with palette, backgrounds, and borders
---
CREATE STYLE «StyleName» AS (
  PALETTE       = ('«#2563eb»', '«#16a34a»', '«#f59e0b»'),
  BACKGROUND    = '«#ffffff»',
  BORDER        = '«1px solid #e2e8f0»',
  BORDER_RADIUS = '«8px»',
  SHADOW        = «ON»,
  THEME         = '«light»'
);
