### Fixed

- **`--silent` printed "Linting failed:" and discarded every reason it failed.** `ILogger.WriteLine`
  derives its log level from the console colour — red is an error, yellow a warning — and silent
  mode keeps only errors. The lint reporter wrote its header red and each diagnostic yellow, so a
  silent run produced a non-zero exit code with no explanation. The colour had quietly become a
  severity decision.

  The lines explaining a fatal error are now emitted at error level. This also repairs the sample
  gate's `@expected-error` check, which reads that output and therefore could not verify a lint
  failure at all — the mechanism existed and silently did not work for the largest class of sample
  failure.

- **`eng.variables` emitted each variable's raw CLR value**, so the view's `value` column held a
  number in one row and a string in the next, and any columnar materialization of it —
  `SELECT … INTO`, or a spill — failed on the first value that did not fit the type inferred from an
  earlier row. The column is documented as text and already carried `*******` for a masked value, so
  it is now rendered consistently with invariant formatting; `data_type` still reports the original
  type. Fixes the `diagnostics_ssh_sink` sample.

### Changed

- The three `admin_operations` templates now declare the failure they are supposed to produce when
  run as shipped — `backup_and_report` because an uninjected variable is an error rather than a
  silent default, `capacity_report` and `daily_failure_digest` because the SMTP password is still
  the `ENC:` placeholder. Each asserts its exit code and message, so the guardrail is covered rather
  than the sample being known-broken.
