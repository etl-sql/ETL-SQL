# Historical scale references

These files preserve the 2026-08-20 Smoke and Standard baselines byte for byte. They are historical
evidence for the v0.19.0 investigation. They use the earlier fixture and capture configuration and
cannot be compared directly with the corrected v0.20.0 fixture.

- [Smoke](2026-08-20-smoke.json)
- [Standard](2026-08-20-standard.json)

The October 4 references pool both clean v0.19.0 control runs from the matching-fixture calibration.
See the
[v0.20.0 performance record](../../docs/releases/v0.20.0-performance-results.md) for the method,
retained warnings and provenance. They are preserved byte for byte here:

- [October 4 Smoke, shared host](2026-10-04-smoke-shared-host.json)
- [October 4 Standard, shared host](2026-10-04-standard-shared-host.json)

The active references in the parent directory now use the clean October 23 calibration's
predetermined first candidate capture, with one scenario per process and scaled own-path warm-up.
See the [calibration record](../../docs/benchmarks/scale-baseline-calibration-2026-10-23.md#scaled-warm-up-calibration)
and [promotion hashes](../scale-calibration-v2-2026-10-23.json). Older protocols cannot be compared
directly with these references.
