# Automating Quality Gates in CI/CD and Schedulers

ETL-SQL is designed to run unattended in continuous integration (CI/CD) pipelines, scheduled cron jobs, and enterprise orchestration platforms. Quality rules and workspace policies are enforced directly by the CLI, returning deterministic exit codes to halt automated workflows when validations fail.

---

> **Applies to:** every deployment profile (Solo, Team, Enterprise, SaaS).

## Workspace Policy Files (`etlsql-policy.json`)

Place an `etlsql-policy.json` file in your repository root to enforce mandatory tagging, classification patterns, and quality thresholds across all developers:

```json
{
  "$schema": "https://etl-sql.io/schemas/etlsql-policy.schema.json",
  "version": "1.0",
  "requiredTags": {
    "script": ["owner", "domain"],
    "column": ["pii"]
  },
  "thresholds": {
    "maxQuarantinePercent": 0.05,
    "maxNullPercent": 0.10
  }
}
```

When running `etl-sql run`, the engine searches up the directory tree, discovers the nearest policy file, and statically lints the script before execution. Missing required tags or invalid configurations fail the run with a non-zero exit code.

---

## Example 1: GitHub Actions CI Pipeline

Execute pipelines in CI/CD and preserve quality summary JSON artifacts for compliance audits.

```yaml
name: ETL Quality Verification

on:
  push:
    branches: [ main ]
  pull_request:

jobs:
  validate-and-run:
    runs-on: ubuntu-latest
    steps:
      - name: Checkout repository
        uses: actions/checkout@v4

      - name: Install ETL-SQL
        run: dotnet tool install -g ETL-SQL.App

      - name: Lint and Run Pipeline with Quality Gates
        run: |
          etl-sql run pipelines/customer_etl.etlsql \
            --quality-summary \
            --output-json artifacts/quality-evidence.json

      - name: Upload Quality Evidence
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: quality-evidence
          path: artifacts/quality-evidence.json
```

---

## Example 2: Linux Cron Job

Run nightly batch scripts with automated logging and JSON evidence generation.

```cron
# Run nightly at 02:00 UTC
0 2 * * * cd /opt/etl && /usr/local/bin/etl-sql run nightly.etlsql --quality-summary --output-json /var/log/etl/evidence-$(date +\%Y\%m\%d).json >> /var/log/etl/nightly.log 2>&1
```

---

## Example 3: Windows Task Scheduler

Configure unattended execution via Windows Task Scheduler:

- **Program/script**: `C:\Program Files\ETL-SQL\etl-sql.exe`
- **Arguments**: `run C:\ETL\pipelines\nightly.etlsql --quality-summary --output-json C:\ETL\evidence\nightly.json`
- **Start in**: `C:\ETL`

---

## Related Topics

- [Column Quality Rules](column-quality-rules.md) — Declaring `@expect` rules.
- [Run-Level Assertions](run-level-assertions.md) — Configuring `ASSERT JOB`.
- [Configuring Script Logging](../operations/configuring-script-logging.md) — Log files and retention.
