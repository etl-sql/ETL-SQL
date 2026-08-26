---
trigger: $html_visual
label: CREATE VISUAL … AS HTML
description: Sanitized repeated HTML component with scoped CSS, fallback text, and a declarative action
---
CREATE VISUAL «VisualName» AS HTML (
  SOURCE = «#prepared»,
  MODE = REPEATER,
  TEMPLATE = '<article class="component"><strong>{{«LabelColumn»}}</strong><span>{{«ValueColumn»}}</span></article>',
  STYLE (CSS = '.component { border: 1px solid var(--etl-border); padding: 0.75rem; }'),
  FALLBACK = '{{«LabelColumn»}}: {{«ValueColumn»}}',
  OPTIONS (MAX_ROWS = «100»)
);
