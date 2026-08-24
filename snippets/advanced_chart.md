---
trigger: $advanced_chart
label: CREATE VISUAL … AS CUSTOM CHART
description: Native layered chart with inherited encodings, inferred scales, constants, placement, and facets
---
CREATE VISUAL «VisualName» AS CUSTOM (
  SOURCE = «#prepared»,
  CHART (
    COORDINATE (TYPE = CARTESIAN, ASPECT_RATIO = «1»),
    ENCODINGS (
      X = «category» (TYPE = NOMINAL),
      Y = «value» (TYPE = QUANTITATIVE, AXIS = PRIMARY)
    ),
    LAYERS (
      primary = RECT (
        Z_INDEX = 0,
        ENCODINGS (COLOR = VALUE('#2563eb') (TYPE = NOMINAL)),
        CONDITIONS (COLOR WHEN «value» < 0 THEN '#b91c1c' ELSE '#2563eb')
      ),
      target = TICK (
        BAND_SIZE = 0.9,
        THICKNESS = 0.2,
        ENCODINGS (Y = DATUM(«target») (TYPE = QUANTITATIVE))
      )
    ),
    FACET (WRAP = «region», COLUMNS = «3»)
  )
);
