# Reporting Phase 12 Refinement Measurements

Measured on 2026-08-24 with .NET 10 Debug binaries on the repository's local Windows validation host. The declared workload is the 5,000-row quantitative X/Y scatter with a diverging color range, stable-key jitter, fixed aspect, serialization, and native SVG rendering implemented by `RepresentativeRefinementWorkload_HasBoundedResolverAndRendererWork`.

| Metric | Measured result | Regression budget |
| :--- | ---: | ---: |
| PlotPlan resolver time | 65 ms | < 5,000 ms |
| Native SVG render time | 35 ms | < 5,000 ms |
| Resolver thread allocation | 14,677,904 bytes | < 268,435,456 bytes |
| Serialized PlotPlan size | 5,763,429 bytes | < 16 MiB |
| Native SVG size | 789,615 bytes | < 16 MiB |
| Resolved data marks | 5,000 | exactly one per input row |

The regression test also proves that scale inference/color resolution and display offsets are computed once in `PlotPlanResolver`; SVG consumes the resolved offsets and does not scan source columns or infer domains. Facet resolution separately rejects more than 100 panels, more than 1,000,000 panel-row work cells, and sub-minimum panels before allocating panel contracts.
