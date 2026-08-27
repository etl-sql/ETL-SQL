# ETL-SQL Development TODO List

Use this list as the execution ledger for all unfinished product and release work. All remaining
product work is active for the current planning horizon. Work top to bottom unless a dependency or
release-blocking defect changes the order. Once an item is verified, record its notable outcome in
`CHANGELOG.md` and check it completed.

Unfinished `ROADMAP.md` initiatives and release gates are represented below.

---

## 1. Hybrid Connectivity & Gateway Enhancements

Authoritative references:
[`secure-outbound-gateway.md`](docs/administration/platform/secure-outbound-gateway.md),
[`saas-tenant-isolation.md`](docs/architecture/saas-tenant-isolation.md#11-secure-outbound-data-gateway), and
[`verified-viewer-context.md`](docs/architecture/decisions/verified-viewer-context.md).

- [ ] Complete approved Gateway resource discovery and binding in the canonical Portal connection
  wizard. Extend the existing active-cluster selector with resources published by the selected live
  Gateway session (`IGatewaySession.PublishedResources`), and use the same resource-aware picker in
  `Admin → Connections` and `Admin → Data Gateways`. Replace manual Gateway and Resource ID entry for
  `SHARED:alias` bindings. Display only approved non-secret metadata: resource identity, connector type,
  allowed operation classes, approval state, online state, and last-seen time. Revalidate tenant grants,
  resource approval, and operation authority on the server when saving and again when executing; never
  expose physical endpoints or credential details through discovery metadata.
- [ ] Certify Verified Viewer Context propagation for SQL Server as a connector-specific capability.
  Add parameterized `SESSION_CONTEXT` setup using the existing HMAC-signed envelope, tenant/resource
  binding validation, and resource-level opt-in contract. Keep the service credential as the database
  identity and prohibit viewer claims from selecting database roles. Prove fail-closed cleanup before
  pooled connection reuse after success, provider failure, cancellation, timeout, and broken-connection
  paths. Do not advertise SQL Server support until the connector certification tests pass.
- [ ] Implement Ambiguous Write outcome alerting and Portal operations triage. When network disconnection
  or process termination causes an in-flight mutating operation to enter an ambiguous state in the outcome
  ledger, surface a deduplicated high-priority alert on the Portal operations dashboard and block unsafe
  automatic retry. Provide a dedicated triage view displaying operation ID, tenant, gateway, resource,
  correlation ID, execution timestamp, current owner, and immutable event history. Authorized operators
  may acknowledge and assign the case, attach evidence and notes, or record an externally verified outcome
  (`confirmed committed`, `confirmed not applied`, `compensated`, or `superseded`). Closing or dismissing
  an alert must never delete the ledger record, erase uncertainty without evidence, or weaken the
  fail-closed ambiguous-write safety invariant. Treat this workflow as a prerequisite for production
  Gateway write operations.

## Bugs

### Workstation Editor
- [ ] **Mouse select not working on single line**  I can click the left mouse button and drag multiple lines and it selects them but 
      if I just wanted to for example I had a line SELECT 1; SELECT 2; SELECT 3;  and I want to just drag to highlight SELECT 2; to just
      run that command I cannot do it I can only run the whole line.
- [ ] **Save button does nothing**  Save button does nothing, should pop open a message as to what you want to name the file and where
      you want to save it.  If that file contains passwords or secrets it should prompt you for a passphrase to encrypt those.

## v0.19.0 Release Evidence Gates

Target Release: **v0.19.0**
Authoritative policy: [`release-checklist.md`](docs/releases/release-checklist.md) and
[`Enterprise_Release_Evidence_Checklist.md`](docs/architecture/decisions/enterprise-release-evidence-checklist.md)

- [ ] Run the full local pre-release gate required by the release checklist, including the selected
  SLT, Docker integration, scale, packaging, and platform lanes.
- [ ] Pass the Enterprise Release Evidence Checklist, `test-lane.ps1`, `Test-PreRelease.ps1`,
  `Test-EnterpriseHardeningCertification.ps1`, `admin restore --validate`, `ha-soak validate`, and
  `SecurityBoundaryDocTests` as applicable to the shipped v0.19.0 claims.
- [ ] Build the deployment-profile claim matrix from evidence and do not promote unfinished Shared
  SaaS or hosted-production outcomes into release claims.
- [ ] Verify third-party notices/inventory, secret scanning, SBOM, checksums, installers, release
  notes, upgrade guidance, and changelog entries for the final shipped scope.
- [ ] Reconcile `TODO.md` and `ROADMAP.md` immediately before release: remove verified completed work,
  retain unfinished increments with accurate status, and ensure release notes describe only
  evidence-backed outcomes.
