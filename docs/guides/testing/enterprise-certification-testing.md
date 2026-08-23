# Enterprise Hardening & Certification Testing

ETL-SQL includes automated security and compliance certification suites to verify zero-trust operation boundaries, tamper-proof policy distribution, and dual-platform behavior.

---

> **Applies to:** contributors and maintainers working on security governance, enterprise policy, or deployment isolation.

## Running Hardening Certification Locally

Execute the certification runner script:

```powershell
.\scripts\Test-EnterpriseHardeningCertification.ps1
```

This script validates:
- Enterprise policy enrollment and dynamic policy refresh
- Cryptographic signature validation and policy tampering rejection
- Sandbox path substitution and symlink attack prevention
- DNS rebinding, redirect prevention, and proxy bypass blocking
- Zero-trust log redaction of secrets, tokens, and PII columns
- Unenrolled standalone mode compatibility

---

## Dual-Platform CI Evidence

CI pipelines execute hardening certification on both **Windows** (`windows-latest`) and **Linux** (`ubuntu-latest`).

### Evidence Outputs

Every run produces structured audit files saved to `certification-results/enterprise-hardening/<run-id>/<platform>/`:
- `enterprise-hardening-summary.json` — Machine-readable test execution metadata.
- `enterprise-hardening-summary.md` — Human-readable compliance table.
- Detailed `.trx` test execution logs.

> [!IMPORTANT]
> A passing run on Windows is not a substitute for Linux verification (or vice versa), because path sandbox semantics and child process boundaries differ between operating systems.

---

## Related Topics

- [Test Lanes and Suite Execution](test-lanes-and-execution.md) — Running targeted test lanes.
- [Platform Administration](../../administration/platform/README.md) — Enterprise configuration.
- [Security and Secret Management](../../administration/platform/secrets.md) — Secret provider setup.
