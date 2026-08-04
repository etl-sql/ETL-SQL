# Enterprise Security Review Packet

Status: Prepared, not signed off.

This packet is the required review record for enterprise policy, identity, fleet visibility, HA,
recovery, audit, and executable-boundary claims. It is a working artifact for enterprise reviewer
signoff; it is not evidence that the review has passed until the signoff section is completed and all
high-severity findings are closed.

## Scope

The review covers the enterprise controls that can change deployment trust boundaries or customer
security expectations:

- Machine enrollment, signed organization policy retrieval, protected cache, rollback checks, and
  standalone behavior.
- Policy authority operations, certificate handling, policy publication, and rollback.
- Runtime enforcement for filesystem, process, connector/network, and resource boundaries.
- Portal and Orchestrator HA ownership, leases, readiness, audit delivery, and shared storage.
- Backup, restore, clone safety, re-enrollment, key coverage, and recovery reporting.
- Read-only fleet aggregation and any proposed future remote fleet mutation or upgrade capability.
- Portal authoring authority: Studio capabilities, the draft review path, protected branches, and
  the separation of duties between authoring, approving, and publishing.
- Portal disclosure surfaces: support bundle, configuration export, access simulator, and the
  administrative posture endpoints — each returns a description of the deployment to a caller.

## Trust Boundaries

| Boundary | Inside Boundary | Outside Boundary | Required Evidence |
| :--- | :--- | :--- | :--- |
| Enrolled client runtime | Signed policy validation, local cache, enforced organization rules | Local administrators, alternate executables, host OS policy | Enterprise hardening certification and OS-boundary documentation. |
| Policy authority | Policy validation, versioning, signing, activation, rollback | Certificate custody, administrator identity provider, network ingress | Policy-authority tests, audit trail, certificate-expiry alerting. |
| Portal HA | Portal nodes, shared artifact root, shared database, shared key ring | Load balancer, DNS, PostgreSQL failover, file/object storage | Topology readiness checks and HA failure certification. |
| Orchestrator HA | Scheduler leadership, leases, job execution, audit/outbox delivery | PostgreSQL availability, service supervision, external connectors | Lease/fencing tests and recovery drill evidence. |
| Backup and restore | Archive metadata, catalog data, artifact references, key backups | Offline media, immutable retention, operator custody | Restore validation report and clone-safety operator actions. |
| Fleet aggregation | Read-only status polling | Remote mutation, upgrade, restart, policy push, secret rotation | Fleet containment tests and separate threat model for any mutation proposal. `HaAndSecurityDocReconciliationTests.FleetAggregation_ExposesNoMutatingRoutes` fails the build if a mutating route is added to a fleet controller, so the non-approval below is enforced rather than stated. |
| Portal authoring | Studio capabilities, draft content, approval record, commit trailer | Git server authorization, branch protection on the remote, reviewer identity outside the Portal | `AuthorizationMatrixTests`, `ReportDraftWorkflowTests`. Separation of duties is absolute — an author cannot approve their own draft **including as Admin** — and a service account's capabilities are capped by its owner's at token issue. |
| Portal disclosure | Counts, versions, states, redacted configuration, effective-permission explanations | Report and dataset **content**, secret values, key material | Support-bundle and access-simulator tests assert the JWT secret, dataset at-rest key, report names, and simulated report rows are absent from the whole response. Every disclosure route is audited. |

## Required Review Decisions

Record the decision, reviewer, and evidence link for each item before closing the gate:

- Policy-signing private key custody, rotation, and emergency rollback procedure.
- Machine enrollment clone-safety behavior after restore, VM image reuse, and cross-environment copy.
- Fail-closed versus fail-open behavior for policy retrieval, audit/security outbox delivery, and
  mutation workflows.
- Scope and storage of service account credentials used by Portal, Orchestrator, fleet readers, and
  restore drills.
- HA readiness behavior when PostgreSQL, shared storage, load balancer affinity, key ring, or JWT
  configuration is inconsistent. Note that `Portal:Topology:ExpectedMode=Auto` infers
  `HighAvailability` from a configured key ring alone, so an under-configured node fails closed and
  is removed from traffic — see
  [HA Topology Failure Certification](HA_Topology_Failure_Certification.md#how-expectedmode-auto-resolves--and-why-it-can-take-a-healthy-node-out-of-rotation).
- Whether `Portal:Studio:RequireApprovalToPublish` and `Portal:SourceControl:ProtectedBranches` are
  required for this deployment. Both default to off/empty so an upgrade never interposes a review
  step; a review path without a protected branch only asks nicely, and a protected branch without a
  review path only blocks people.
- Who may call the disclosure surfaces, and whether the review-hash acknowledgement on support
  bundles and configuration exports is treated as binding or advisory.
- Documentation claims around application-level policy enforcement versus host-level containment.

## High-Severity Finding Register

The ROADMAP security-review gate cannot close with any open high-severity finding.

| Id | Area | Severity | Finding | Owner | Status | Resolution Evidence |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| TBD | TBD | TBD | Populate during review. | TBD | Pending review | TBD |

Acceptable statuses are `Open`, `Mitigated`, `Accepted`, or `Closed`. High-severity findings must be
`Closed` before release claims are made. Medium findings require an owner and target release. Accepted
findings require an explicit reviewer note and release-note language.

## Remote Fleet Mutation Non-Approval

The approved enterprise fleet design is read-only status aggregation. No remote mutation, upgrade,
restart, policy push, job run, shared-secret rotation, or remote command execution is approved by this
packet.

Any future remote mutation proposal requires a separate threat model that includes:

- Credential theft and confused-deputy abuse cases.
- Tenant, environment, node, action, and payload authorization rules.
- Human approval workflow for important or destructive actions.
- Audit contract with requester, approver, target, correlation ID, before/after state, and outcome.
- Rollback, emergency stop, replay protection, and partial-failure handling.

## Signoff

| Role | Name | Date | Decision | Notes |
| :--- | :--- | :--- | :--- | :--- |
| Senior security reviewer | TBD | TBD | Pending | Required before making enterprise security claims. |
| Engineering owner | TBD | TBD | Pending | Confirms mitigations and tests are complete. |
| Release owner | TBD | TBD | Pending | Confirms release notes match reviewed claims. |

## References

- [Enterprise Release Gates](Enterprise_Release_Gates.md)
- [Enterprise Platform Strategy](../roadmaps/Enterprise_Platform_Strategy.md)
- [Administrators Guide](../../administration/platform/README.md)
- [HA Topology Failure Certification](HA_Topology_Failure_Certification.md)
- [Disaster Recovery Objectives](Disaster_Recovery_Objectives.md)
