# Architecture Decision Evaluation: Geographic Density Map

## Status
Evaluated (Phase 9 / 0.19)

## Context
ETL-SQL provides a native Grammar of Graphics reporting engine (`ChartSpec`, `PlotPlan`, and pure C# `PlotPlanSvgRenderer` / managed `TerminalRenderer`) with zero external JavaScript or native V8 dependencies.

Currently, geographic visualizations are served by `MAP`:
- Point scatter / bubble overlays on geographic boundaries (`LAT`, `LON`, `SIZE`, `COLOR`).
- Choropleth region fills (`REGION`, `VALUE`).
- Built-in TopoJSON / GeoJSON topologies (World, USA, countries).
- Base map tile underlays (`BASE_MAP = 'url-template'`).

However, for high-density spatial point datasets (e.g. 50,000 delivery drop-offs, network sensor pings, or crime incident records), rendering 50,000 individual `<circle>` SVG elements causes browser DOM degradation, excessive SVG payload sizes (often > 10 MB), and visual occlusion (point over-plotting).

The existing `HEATMAP` visual in ETL-SQL is a discrete categorical × categorical matrix grid (such as DayOfWeek × HourOfDay), not a continuous 2D geographic density surface.

This document evaluates the architectural design, algorithmic approaches, SVG vector budgets, Zero-Trust security boundaries, and syntax proposals for adding `MAP (MODE = DENSITY)`.

---

## 1. Density Representation Approaches

Three primary approaches exist for visualizing 2D point density:

### 1.1 Hexagonal Binning (Hexbin)
Points are aggregated into regular hexagonal cells on a projected planar grid:
- **Geometry**: Each hexagon has 6 vertices calculated from $(c_x, c_y)$ and radius $r$.
- **Aggregation**: Points falling within each hexagon are counted or summed (`COUNT`, `SUM(weight)`, `AVG(metric)`).
- **Mark Representation**: A discrete set of `MarkKind.Path` regular polygons (hexagons). Hexagons with 0 counts are omitted.
- **Rendering Cost**: Modest. For a 40×40 grid, at most 1,600 SVG `<path>` elements are emitted, regardless of whether the source dataset contains 1,000 or 1,000,000 points.
- **Visual Clarity**: Uniform tessellation avoids rectangular aspect ratio distortions and provides equidistant neighbor relations.

### 1.2 2D Kernel Density Estimation (KDE) with Contour Isobands
Points are smoothed using a Gaussian kernel function over a 2D lattice:
- **Computation**: For each grid point $(i, j)$, $D(i, j) = \sum_{k} K\left(\frac{x_k - x_i}{h_x}\right) K\left(\frac{y_k - y_j}{h_y}\right)$.
- **Contour Generation**: Marching squares algorithm extracts polygon contour isobands for discrete density thresholds.
- **Mark Representation**: Concentric filled SVG `<path>` contour ribbons with smooth curves.
- **Rendering Cost**: Very low SVG element count (typically 10–30 contour paths). Computationally higher in C# during semantic planning ($O(N \times G_x \times G_y)$ without spatial indexing).

### 1.3 Raster Heatmap (Offscreen Canvas / PNG Data URI)
Points are splatted onto an off-screen bitmap with a radial blur gradient and color-mapped via palette lookup:
- **Trade-off**: Requires raster bitmap generation (`SkiaSharp` or managed byte buffer) and base64 embedding in SVG `<image>` tag.
- **Compatibility**: Conflicts with ETL-SQL's lightweight, pure C# managed rendering policy without adding native image encoding dependencies.

---

## 2. Evaluation Against Architecture Principles

| Criterion | Hexagonal Binning | 2D KDE Contours | Raster Heatmap |
| :--- | :--- | :--- | :--- |
| **Pure C# Managed Implementation** | Native arithmetic, 0 external deps | Marching squares in managed C#, 0 deps | Requires image encoder or external lib |
| **SVG Vector Budget** | < 2,000 path elements (well within budget) | < 50 path elements (minimal) | 1 `<image>` element, but binary payload |
| **Terminal Renderer** | Maps naturally to braille/block cells | Coarse contour approximation | Not representable in terminal |
| **Memory Footprint** | $O(\text{grid bins})$ during aggregation | $O(\text{grid size})$ matrix | $O(\text{pixels})$ memory buffer |
| **Interactivity (Tooltips/Click)** | Each hexagon can be hovered with count/stats | Contours represent ranges, not point details | No vector hover targets |
| **Zero-Trust Predictability** | Deterministic CPU time and allocation bounds | $O(N \times G)$ bounded by max point limits | Dependent on pixel resolution |

---

## 3. Syntax Proposal

Extend the existing `MAP` visual with `MODE = DENSITY`:

```sql
CREATE VISUAL DeliveryHotspots AS MAP (
    SOURCE = #deliveries,
    MODE = DENSITY,
    MAPPINGS (
        LAT = PickupLat,
        LON = PickupLon,
        WEIGHT = PackageVolume -- Optional; defaults to COUNT(1)
    ),
    OPTIONS (
        DENSITY_TYPE = HEXBIN,      -- HEXBIN | CONTOUR (Default: HEXBIN)
        BIN_SIZE = 25,             -- Hexagon radius in screen pixels (Default: 20)
        AGGREGATION = SUM,         -- COUNT | SUM | AVG (Default: COUNT)
        COLOR_SCHEME = 'YlOrRd',   -- Sequential palette (Default: 'Viridis')
        MIN_OPACITY = 0.2,         -- Lower bound opacity for sparse bins
        MAX_OPACITY = 0.9,         -- Upper bound opacity for dense bins
        BASE_MAP = 'https://tile.openstreetmap.org/{z}/{x}/{y}.png'
    )
);
```

### 3.1 Grammar of Graphics Lowering
1. **Source Data Extraction**: Extract `LAT`, `LON`, and optional `WEIGHT` into typed float arrays.
2. **Coordinate Projection**: Project $(\text{lon}, \text{lat}) \to (x, y)$ using the visual's active projection (Mercator, Albers, or Equirectangular).
3. **Hexagonal Lattice Tiling**:
   - Bin coordinates $(q, r, s)$ in axial hexagonal coordinate space with step size $\Delta = \text{BIN\_SIZE}$.
   - Group records by hex cell key $(q, r)$ and compute aggregate `Value`.
4. **Scale Resolution**:
   - Resolve `ScaleKind.Sequential` mapping `[min(Value), max(Value)]` to color gradient stops and opacity range.
5. **Mark Generation**:
   - Emit `MarkKind.Path` for each non-empty hex bin with pre-computed polygon path string `M ... L ... Z`.

---

## 4. Zero-Trust Security & Resource Bounds

1. **Max Points Threshold**: Input points for client/engine density computation are capped at `MaxPointCount = 250,000`. Datasets exceeding this must be pre-aggregated in ETL SQL or sampled.
2. **Max Hex Bin Grid**: The maximum number of rendered hexagons is hard-capped at 5,000 elements. Screen dimensions divided by `BIN_SIZE` cannot exceed 5,000 bins; `BIN_SIZE` is clamped to $\ge 5$ pixels.
3. **Base Map Allowlisting**: Any companion `BASE_MAP` tile template is validated against `SecurityService.ValidateHost(uri.Host)`.

---

## 5. Implementation Roadmap & Recommendation

- **Verdict**: **Accepted in principle** for implementation in Phase 9 / 0.19.
- **Phase 1**: Hexagonal Binning (`DENSITY_TYPE = HEXBIN`). Pure C# hex tessellation algorithm, direct generation of `MarkKind.Path`, full tooltip support with bin aggregate counts.
- **Phase 2**: Contour isobands (`DENSITY_TYPE = CONTOUR`). Marching squares algorithm on 2D regular lattice.
- **Recommendation**: Prioritize `HEXBIN` as the default density mode because it provides deterministic vector budgets, interactive tooltips per cell, and straightforward managed C# implementation.
