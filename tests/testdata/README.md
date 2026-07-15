# Test Fixtures

This directory contains fixtures owned by the automated test suite. Tests may read these files directly, but any test that needs to modify data must copy the fixture into a temporary directory first.

## Policy

- Keep fixtures deterministic, synthetic, and sanitized.
- Store test-only fixtures here instead of the repository root sample data folder.
- Do not let tests write back to this directory during normal runs.
- Use `Path.GetTempPath()` or a per-test temp directory for generated, mutated, or deleted files.

Root-level `testdata/` is for sample-facing data. Shared files can exist in both places when tests need stable copies that should not be affected by sample edits.
