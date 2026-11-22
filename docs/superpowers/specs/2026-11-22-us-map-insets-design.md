# Alaska and Hawaii insets on US maps (TODO DOC09)

**Status:** short design, owner-approved approach A (2026-11-22).

## Why

A `US_STATES` map fitted to its geometry draws Alaska and Hawaii where they really are, so Alaska
takes a large share of the card and the lower 48 shrink. US maps usually show both states as insets
below the lower 48. It also kept the map recipes out of the README gallery's best framing (DOC06).

## Goals

- Fitted `US_STATES` maps (choropleth and `MODE = POINTS`) show Alaska and Hawaii as insets below
  the lower 48, so the lower 48 fill the card.
- A marker at an Alaska or Hawaii longitude/latitude still lands on its state's shape.
- Both native SVG renderers do it the same way: `PlotPlanSvgRenderer` (PlotPlan maps) and
  `SpecializedNativeSvgRenderer` (named-visual `MAP`).

## Non-goals

- No new projection; the lower 48 look exactly as today.
- No change to other bundled maps (`world`, `us-counties`, `mn-counties`, `canada-provinces`,
  `europe`) or to `MAP_FILE` maps.
- No new syntax. (A switch to turn insets off can come later if someone asks.)

## Anti-goals

- Moving a marker off its state, or moving a lower-48 point.
- Changing what an author sees when they set `ZOOM` or `CENTER`: those views are on real
  coordinates and keep real geography.

## Approaches

- **A. Shared shift-and-scale before projection (chosen).** One helper moves a point inside the
  Alaska or Hawaii box into its inset position before either renderer projects it. Outlines and
  markers go through the same helper, so markers stay on their shapes.
- **B. Bake the shifted shapes into `us-states.geojson`.** Cheapest, but Alaska and Hawaii markers
  would land in the ocean and `ZOOM`/`CENTER` would be wrong there. Rejected.
- **C. A true Albers USA composite projection.** Most faithful, but replaces both renderers'
  projection and changes how every US map looks. Rejected as too large for the gain.

## Design

- **Helper:** `UsStatesInsets` (internal static, `ETL-SQL.Reporting`), one method
  `(lon, lat) Apply(lon, lat)`. Points outside both boxes come back unchanged.
- **Boxes** (from the bundled file: Alaska lon −178.1 to −130.0, lat 51.6 to 71.4; Hawaii lon −159.8
  to −154.8, lat 18.9 to 22.2; no lower-48 point lies west of −125°):
  - Alaska: lon ≤ −129.5 and lat ≥ 51.0. Scale 0.35 about the box's centre, then place its centre
    near lon −117, lat 27.5 (below California and Arizona).
  - Hawaii: −161 ≤ lon ≤ −154 and 18 ≤ lat ≤ 23. Keep scale 1, place its centre near lon −105,
    lat 24.5 (below Texas's western edge).
  - Final offsets are tuned by eye against the rendered card; the test pins them only through
    the properties below, not exact numbers.
- **When it applies:** the map is the bundled `US_STATES` geometry and the view is fitted (no
  `ZOOM`, no `CENTER`). Applied to every outline ring point, to the fit extent, and to each
  `POINTS` marker before projection.

## Tests

- Helper: a lower-48 point (Seattle, Miami, Maine) is unchanged; Anchorage and Honolulu move into
  the inset area; a point just outside each box is unchanged.
- Each renderer, fitted `US_STATES` choropleth: the lower 48 take a larger share of the card width
  than before, and every projected Alaska and Hawaii point lies inside the card.
- Each renderer, `MODE = POINTS` with an Anchorage marker: the marker lies inside Alaska's projected
  outline bounding box.
- With `ZOOM` or `CENTER` set: output unchanged from today.

## Left to the builder

Exact inset offsets after looking at the render; whether to draw a thin frame line around the
insets (only if it reads better); refreshing the README map screenshot through
`scripts/update-readme-gallery.mjs`.
