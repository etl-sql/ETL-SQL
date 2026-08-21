---
trigger: $advanced_chart
label: CREATE VISUAL … AS CUSTOM CHART
description: Native layered chart with explicit scales, encodings, conditions, and optional facets
---
CREATE VISUAL «VisualName» AS CUSTOM (
  SOURCE = «#prepared»,
  CHART (
    COORDINATE (TYPE = CARTESIAN),
    SCALES (
      x_scale = BAND (CHANNEL = X, ORDER = SOURCE),
      y_scale = LINEAR (CHANNEL = Y, INCLUDE_ZERO = ON)
    ),
    LAYERS (
      primary = RECT (
        Z_INDEX = 0,
        ENCODINGS (
          X = «category» (TYPE = NOMINAL, SCALE = x_scale),
          Y = «value» (TYPE = QUANTITATIVE, SCALE = y_scale, AXIS = PRIMARY)
        ),
        CONDITIONS (COLOR WHEN «value» < 0 THEN '#b91c1c' ELSE '#2563eb')
      )
    )
  )
);
