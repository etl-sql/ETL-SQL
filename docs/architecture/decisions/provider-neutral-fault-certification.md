# Provider-neutral fault certification

**Status:** Accepted and implemented

**Applies from:** v0.18.0

## Decision

Fault certification uses one scenario catalog and one evidence contract across local, Docker, and
cloud execution adapters. Provider adapters may decide how to activate a fault, but they receive the
unchanged scenario definition and return the same provider-neutral observation. A hosting-specific
chaos product is not part of the semantic contract.

The catalog covers:

- process or worker loss;
- lease expiry and fencing races;
- database disconnect;
- partial artifact operations;
- storage outage;
- network partition;
- duplicate delivery;
- clock skew; and
- disk exhaustion.

Each definition owns its deterministic injection point, trigger, and expected safe outcome. The
catalog is implemented by `ProviderNeutralFaultScenarios` in Core. `IFaultInjectionHook` is the
runtime seam. `DeterministicFaultInjectionHook` activates an occurrence-addressed failure without a
sleep or wall-clock race.

## Safety evidence

Every run records the provider, deployment profile, adapter, repetition, operation identity,
checkpoint contract, observations, activated fault point, and invariant results. A run fails unless
all of these are true:

- no more than one mutation authority exists at once;
- a stale authority is rejected;
- every accepted delivery is either committed or visibly failed;
- an operation identity has at most one committed result; and
- the recovery claim matches the workload checkpoint contract.

The last invariant is intentionally asymmetric. A workload may report named-checkpoint resume only
when its request declares an explicit checkpoint and the observed resumed name matches it exactly.
Every other workload must report safe failure and eligibility for deliberate retry. Retry eligibility
does not mean automatic replay or checkpoint resume.

## Adapters

- `LocalFaultScenarioAdapter` runs the deterministic driver in-process.
- `DockerFaultScenarioAdapter` connects Docker lifecycle/network/provider controls to the same driver
  contract.
- `CloudFaultScenarioAdapter` connects cloud-provider controls to that contract.

The adapters contain no scenario translation. A provider driver supplies the physical fault action
through the adapter callback. The shipped deterministic matrix exercises every adapter contract. A
deployment may add a physical provider row only when its driver emits the complete evidence contract;
adapter-contract evidence must not be presented as proof of an untested external service.

## Certification matrix

`tests/fixtures/provider-neutral-fault-matrix.json` is the supported matrix. The runner executes every
selected row at least twice, verifies every catalog scenario on every repetition, hashes each detailed
report, and fails on missing or invalid evidence.

Run the complete matrix with:

```powershell
.\scripts\Test-ProviderNeutralFaultCertification.ps1 -Profile All
```

Deployment-profile certification invokes the fault runner for every selected profile. Its evidence
bundle retains the nested fault reports and logs, so a green profile claim cannot omit its fault
matrix.

## References

- [Deployment-profile certification](../../administration/platform/deployment-profile-certification.md)
- [Deployment profiles](../deployment-profiles.md)
- [Release checklist](../../releases/release-checklist.md)
