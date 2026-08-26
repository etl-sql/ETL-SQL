# Production canaries

Production canaries are isolated synthetic journeys for the hosted fleet. They test the external
health, report, job, Gateway, export, and notification paths in that order. The shared contract lives
in `ETL-SQL.Core/Reliability/ProductionCanaries.cs`; hosting code supplies the real external probes
through `ConfiguredProductionCanaryExecutor`.

## Hosted SLO and coverage contract

The versioned plan is `tests/fixtures/production-canary-plan.json`. A change to an SLO, region,
failure domain, quota, cost ceiling, rotation interval, or alert route is a reviewed operational
change. The current launch gate is:

| Journey | Availability | Maximum latency | Evaluation window |
| :--- | :--- | :--- | :--- |
| External health | 99.95% | 2 seconds | 5 minutes |
| Report | 99.90% | 8 seconds | 10 minutes |
| Job | 99.90% | 15 seconds | 10 minutes |
| Gateway | 99.90% | 5 seconds | 10 minutes |
| Export | 99.50% | 20 seconds | 15 minutes |
| Notification | 99.50% | 30 seconds | 15 minutes |

Every journey runs from `us-central` and `us-east` and targets both `zone-a` and `zone-b`. Replace
those deployment labels in the plan when the hosted fleet changes. The runner records every
journey/region/domain combination independently.

The external-health handler must execute outside the ETL-SQL service boundary. The other handlers
must use the same public or tenant-facing entry points a hosted client uses:

- **ExternalHealth** — resolve the public endpoint, establish TLS, call health, and validate the
  expected deployment identity.
- **Report** — open a synthetic report session, execute its dataset, and verify the known result.
- **Job** — submit the synthetic job, observe durable completion, and verify its output identity.
- **Gateway** — execute a synthetic query through Gateway and verify its known scalar and audit row.
- **Export** — request an export, download it through the public path, and verify its digest.
- **Notification** — trigger a synthetic notification and confirm receipt at the isolated sink.

`ConfiguredProductionCanaryExecutor` requires exactly one handler for every journey. The plan also
requires the complete catalog in the order above. Missing coverage fails before a probe runs.

## Isolation boundary

Provision the plan's tenant, workload identity, resource namespace, and quota pool as dedicated
synthetic resources. All four identifiers must start with `synthetic-`. The canary tenant has its own
cost allocation and a USD 250 monthly ceiling. Its network policy denies customer networks, its
identity has no customer resource grants, and its quota pool is not shared with customer workloads.

Each handler returns every tenant it touched, explicit customer-data and customer-system access
flags, whether customer capacity was consumed, and attributed monthly cost. Certification fails if a
run touches any tenant except the configured synthetic tenant, accesses customer data or systems,
consumes customer capacity, reports a negative cost, or exceeds the canary cost ceiling. A probe that
cannot supply this evidence is not a passing canary.

Use synthetic fixtures only. Never copy customer data, destinations, notification addresses, secrets,
exports, or report definitions into the canary tenant. The notification handler must deliver to an
operator-owned sink that cannot relay externally.

Hosting code implements `ICanaryResourceProvisioner` against its control plane. Certification calls
`CanaryProvisioningCertification.ProvisionAndVerifyAsync` and retains the observed tenant, identity,
namespace, quota, cost guard, customer grants, network routes, and dedicated-capacity result. An
adapter that reports a customer grant, customer route, shared capacity, mismatched resource, or weak
cost guard fails provisioning evidence.

## Failure attribution and alerts

Every handler probes its synthetic dependencies separately from the ETL-SQL result. The runner emits
one `CanaryFailureKind` and one route:

| Evidence | Attribution | Route |
| :--- | :--- | :--- |
| Dependency probe failed | `SyntheticDependency` | `sre-synthetic-dependency-ticket` |
| Wrong ETL-SQL result | `EtlSqlCorrectness` | `sre-etlsql-page` |
| Availability below the window target | `EtlSqlAvailability` | `sre-etlsql-page` |
| ETL-SQL latency exceeded | `EtlSqlLatency` | `sre-etlsql-page` |
| Tenant, capacity, or cost boundary failed | `IsolationViolation` | `sre-etlsql-page` |

Dependency failure takes precedence over downstream correctness noise. Isolation failure takes
precedence over all other attribution. The ETL-SQL and dependency routes must be distinct in every
journey. A normal passing run has no alert route. Hosting code binds `ICanaryAlertSink` to the actual
paging/ticket system. A drill passes only when the sink returns a delivered receipt with a non-empty
alert ID on the expected route; merely calculating the route is failed evidence.

## Credentials and compromise response

Canary credentials are short lived. The current plan rotates them every 24 hours and rejects a
lifetime over 48 hours. Secret material stays in the hosting provider's workload-identity or secret
authority; the plan and evidence contain only credential identifiers and timestamps.

Schedule `CanaryCredentialLifecycle.RotateIfDueAsync` at least hourly. The credential authority
revokes the prior credential before issuing its replacement. On suspected compromise, call
`RespondToCompromiseAsync` immediately; it uses the same revoke-before-issue sequence and labels the
evidence `compromise`. A revoked, expired, future-issued, or overlong credential cannot start a run.
After replacement, run the normal external-health journey before returning the canary to service.

## Fault drills and evidence

Run the production-like certification from the repository root:

```powershell
./scripts/Test-ProductionCanaryCertification.ps1
```

Use `-Explain` to review the SLO and coverage matrix. Use `-NoBuild` only after building the test
project at the requested configuration. For every journey, region, and failure domain, the lane runs
the normal path and four drills:

- wrong-result injection, which must route an ETL-SQL correctness alert;
- availability samples below the journey target, which must route an ETL-SQL availability alert;
- latency injection just beyond that journey's SLO, which must route an ETL-SQL latency alert;
- synthetic dependency outage, which must route only the dependency alert.

All drills retain the synthetic tenant, capacity, quota, and cost boundaries. The lane writes a
commit-bound `certification.json`, `certification.md`, full test log, detailed JSON for every run,
synthetic provisioning evidence, and scheduled/compromise credential lifecycle evidence
under `certification-results/production-canaries/<run>/`. It fails on missing evidence, an unexpected
run count, an ambiguous or missing route, any isolation violation, or any failed normal journey.

Before a hosted release, bind the six handlers to that environment's external probes, execute the
same matrix against each declared region/failure domain, and retain adapter diagnostics with the
certification bundle. Deterministic repository evidence proves the contract and alert logic; it does
not substitute for current fleet observations.

## References

- [Deployment-profile certification](deployment-profile-certification.md)
- [Portal production readiness](../portal/production-readiness.md)
- [Provider-neutral fault certification](../../architecture/decisions/provider-neutral-fault-certification.md)
