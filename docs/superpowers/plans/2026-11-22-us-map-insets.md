# Alaska and Hawaii Insets on US Maps Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fitted `US_STATES` maps draw Alaska and Hawaii as insets below the lower 48 in both native SVG renderers, with markers kept on their states.

**Architecture:** One internal helper, `UsStatesInsets`, moves a longitude/latitude inside the Alaska or Hawaii box to its inset position. Each renderer runs outline points, the fit extent and markers through it before projecting, only for the bundled `US_STATES` map with no `ZOOM`/`CENTER`.

**Tech Stack:** C# (.NET 10), xUnit; `src/ETL-SQL.Reporting`.

**Spec:** `docs/superpowers/specs/2026-11-22-us-map-insets-design.md`

## Global Constraints

- Alaska box: lon ≤ −129.5 and lat ≥ 51.0. Scale 0.35 about the box centre (lon −154.05, lat 61.48), centre placed near lon −117, lat 27.5.
- Hawaii box: −161 ≤ lon ≤ −154 and 18 ≤ lat ≤ 23. Scale 1, centre (lon −157.29, lat 20.59) placed near lon −105, lat 24.5.
- Offsets may be tuned by eye; tests assert properties, never the exact numbers.
- Applies only to the bundled `US_STATES` map with neither `ZOOM` nor `CENTER`. `MAP_FILE`, other built-in maps and explicit views are unchanged.
- Keep each file's existing line endings (check with Python). Run tests scoped to one project and a filter.

## Review Focus

- `MODE = POINTS` on `US_STATES` with only lower-48 markers: still fitted to the outline, insets included (Task 3 test `Map_UsStatesPoints_AnchorageMarkerLandsOnAlaskaInset` also asserts a Seattle marker is unchanged in place relative to Washington).
- `MAP_NAME = 'us-states'` spelled with a hyphen or lower case: same map, insets apply (Task 3 uses `us_states`).
- A marker exactly on a box edge (lon −129.5, lat 51.0): moves, consistently with outlines (Task 1 edge cases).
- Mercator projection on the PlotPlan path: insets stay inside the plot (Task 2 runs the choropleth with `PROJECTION = MERCATOR` as a second case).
- A choropleth row for "Alaska": its fill lands on the inset shape, not elsewhere (Task 3 asserts the Alaska `<title>` path is the one inside the inset area).

---

### Task 1: `UsStatesInsets` helper

**Files:**
- Create: `src/ETL-SQL.Reporting/Renderers/UsStatesInsets.cs`
- Test: `tests/ETL-SQL.Tests/Reporting/UsStatesInsetsTests.cs`

**Interfaces:**
- Produces: `internal static class UsStatesInsets` with
  `public static (double Lon, double Lat) Apply(double lon, double lat)` and
  `public static (decimal Lon, decimal Lat) Apply(decimal lon, decimal lat)` (converts and calls the double one),
  `public static bool IsBuiltInUsStates(string? mapName)` (true for `US_STATES`, `us-states`, `us_states`, any case).

- [ ] **Step 1: Write the failing tests** — `UsStatesInsetsTests`:
  - `LowerFortyEight_IsUnchanged` — theory over Seattle (−122.33, 47.61), Miami (−80.19, 25.76), Eastport ME (−66.99, 44.91), San Diego (−117.16, 32.72): `Apply` returns the input exactly.
  - `Anchorage_MovesBelowTheLowerFortyEight` — `Apply(-149.90, 61.22)`: result lon in −125..−105, lat in 22..32.
  - `Honolulu_MovesBelowTheLowerFortyEight` — `Apply(-157.86, 21.31)`: result lon in −112..−98, lat in 22..28.
  - `AlaskaShrinks_HawaiiKeepsItsSize` — distance between Juneau (−134.42, 58.30) and Nome (−165.41, 64.50) after `Apply` ≈ 0.35 × before (±0.01 relative); Hilo (−155.09, 19.72) to Lihue (−159.37, 21.98) unchanged (±1e-9).
  - `BoxEdges` — (−129.5, 51.0) moves; (−129.4, 51.0) and (−129.5, 50.9) do not; (−154, 23) moves; (−153.9, 20) does not.
  - `IsBuiltInUsStates` — true for `"US_STATES"`, `"us-states"`, `"Us_States"`; false for `null`, `""`, `"US_COUNTIES"`, `"WORLD"`.
- [ ] **Step 2: Run** `dotnet test tests/ETL-SQL.Tests/ETL-SQL.Tests.csproj --filter FullyQualifiedName~UsStatesInsetsTests` — expected: build fails (type missing).
- [ ] **Step 3: Implement** `UsStatesInsets` with the Global Constraints values as named constants and a one-line comment saying why insets exist.
- [ ] **Step 4: Run** the same filter — expected: all pass.
- [ ] **Step 5: Commit** `src/ETL-SQL.Reporting/Renderers/UsStatesInsets.cs` and the test file.

### Task 2: PlotPlan renderer (`PlotPlanSvgRenderer.RenderGeographic`)

**Files:**
- Modify: `src/ETL-SQL.Reporting/Renderers/PlotPlanSvgRenderer.cs` (`RenderGeographic` ~4458–4612, `GeographicDatum` ~4614)
- Test: `tests/ETL-SQL.Tests/Reporting/AdvancedVisualGapsTests.cs`

**Interfaces:**
- Consumes: `UsStatesInsets.Apply(decimal, decimal)`; built-in US states is `geography.SourceAuthority == "builtin:US_STATES"`.

- [ ] **Step 1: Write the failing test** `Map_UsStates_DrawsAlaskaAndHawaiiAsInsets` (theory: no projection option, and `PROJECTION = MERCATOR` if the MAP statement accepts it; otherwise one case), modelled on `Map_WithoutZoomOrCenter_FitsUsStatesToThePlot` with rows for California, Texas, Alaska, Hawaii. Parse each `plot-geographic-region` path with its `<title>`:
  - every point inside the viewBox;
  - lower-48 paths (titles other than Alaska/Hawaii) span > 0.80 of the width;
  - Alaska's and Hawaii's bounding boxes have max Y greater than Texas's min Y (they sit low) and min X less than Texas's max X (they sit left of Texas's east edge).
- [ ] **Step 2: Run** `--filter FullyQualifiedName~AdvancedVisualGapsTests.Map_` — expected: new test fails on the width share (about 0.5 today); the existing fit test still passes.
- [ ] **Step 3: Implement.** Compute `var insets = geography.SourceAuthority == "builtin:US_STATES" && no ZOOM && no CENTER;` once. When set, pass every ring point and the fit extent through `UsStatesInsets.Apply` before `FitGeographicView` / `ProjectGeographic`, and do the same inside `GeographicDatum` (add a `bool insets` parameter) for routes, points and labels.
- [ ] **Step 4: Run** the same filter plus `--filter FullyQualifiedName~PlotPlan` — expected: all pass.
- [ ] **Step 5: Commit** the renderer and test.

### Task 3: Named-visual renderer (`SpecializedNativeSvgRenderer.Map`)

**Files:**
- Modify: `src/ETL-SQL.Reporting/Renderers/SpecializedNativeSvgRenderer.cs` (`Map` ~896–1060, `MapView` ~1197, `GeometryPoints`, `PolygonPath`, `DrawMapOutline`)
- Test: `tests/ETL-SQL.Tests/Reporting/SpecializedNativeSvgRendererTests.cs`

**Interfaces:**
- Consumes: `UsStatesInsets.Apply(double, double)`, `UsStatesInsets.IsBuiltInUsStates(string?)`.
- Changes: `MapView` gains `bool Insets` and `(double X, double Y) Point(double lon, double lat)`; map code projects through `Point` instead of separate `X`/`Y` calls.

- [ ] **Step 1: Write the failing tests**:
  - `Map_UsStates_DrawsAlaskaAndHawaiiAsInsets` — `Visual("MAP", ["State","Revenue"], [California, Texas, Alaska, Hawaii rows])`, `MAP_NAME = US_STATES`; same three assertions as Task 2, finding paths by `<title>` text.
  - `Map_UsStatesPoints_AnchorageMarkerLandsOnAlaskaInset` — `MODE = POINTS`, `MAP_NAME = us_states`, markers Anchorage (−149.90, 61.22) and Seattle (−122.33, 47.61): Anchorage circle lies inside the bounding box of the outline path(s) left of x = Texas east edge and below Texas min Y; Seattle circle lies inside Washington's outline bounding box.
  - Existing `Map_WithZoomAndCenter_KeepsTheExplicitView` and `Map_WithoutZoomOrCenter_FitsTheGeometryToTheChart` keep passing unchanged.
- [ ] **Step 2: Run** `--filter FullyQualifiedName~SpecializedNativeSvgRendererTests.Map_` — expected: the two new tests fail.
- [ ] **Step 3: Implement.** `insets = string.IsNullOrWhiteSpace(v.ResolvedMapFile) && UsStatesInsets.IsBuiltInUsStates(v.Options.GetValueOrDefault("MAP_NAME")) && !explicitView`. Feed `GeometryPoints(...).Select(Apply)` (and marker points) to `FitView` when set; build the `MapView` with `Insets = insets`; `Point` applies `UsStatesInsets.Apply` first when `Insets`.
- [ ] **Step 4: Run** `--filter FullyQualifiedName~SpecializedNativeSvgRendererTests` — expected: all pass.
- [ ] **Step 5: Commit** the renderer and test.

### Task 4: Docs, recipes and TODO

**Files:**
- Modify: `docs/reference/visuals-reporting/visuals/map.md` (or the MAP reference page `scripts/audit-docs.js` maps to MAP; find with Grep for `MAP_NAME`), `TODO.md` (DOC09), README gallery image if `scripts/update-readme-gallery.mjs` covers the map recipe.

- [ ] **Step 1:** One sentence in the MAP reference where `US_STATES` is described: "Fitted `US_STATES` maps draw Alaska and Hawaii as insets below the lower 48; setting `ZOOM` or `CENTER` shows them at their real positions."
- [ ] **Step 2:** Run `dotnet test tests/ETL-SQL.Tests/ETL-SQL.Tests.csproj --filter "FullyQualifiedName~RunnableDocSampleTests|FullyQualifiedName~BuiltInMapCoordinateTests"` and `node scripts/audit-docs.js --strict` — expected: pass / "Docs audit passed."
- [ ] **Step 3:** Regenerate the gallery map image with `node scripts/update-readme-gallery.mjs` only if it needs no extra install; otherwise note it for the owner.
- [ ] **Step 4:** Tick DOC09 in `TODO.md` with the test names and commit.
