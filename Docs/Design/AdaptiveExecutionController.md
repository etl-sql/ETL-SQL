# Adaptive Execution Controller (v0.15.0 Phase 2) — Design

**Status:** DRAFT for review — no implementation yet.
**TODO items covered:** v0.15.0 Phase 2 (bounded resource controller; hysteresis/bounds/fairness/
overrides; deterministic single-worker preservation; scale-down proven as well as scale-up).
**Completion gate:** "Adaptive execution demonstrates higher utilization when resources are idle
and safe throttling under contention, with fairness and governance ceilings proven by automated
tests."

---

## 1. Goal

Today every tuning input is **static per run**: `Engine:BatchSize`, `MaxParallelDegree`,
`OperatorMemoryGrantMB`, `ExternalHashPartitions`, the 1-deep spill write pipeline. The operator
picks numbers for the worst case, so a lone job on an idle 32-core box runs at the same settings
as one of six concurrent jobs on a saturated 8-core VM. Phase 2 adds a **bounded, observing
controller** that moves a small set of runtime setpoints *within operator-configured and
governance-locked bounds*, from measured signals — up when capacity is idle, down under pressure —
without ever changing results.

### Non-goals

- **No new ceilings and no raising of existing ones.** The controller only moves *within*
  `[floor, configured/governed value]`. Governance (`Security:MaxParallelDegree`,
  `Engine:TotalMemoryGrantMB`, `Security:MaxSpillBytesPerScript`) is the hard upper boundary;
  the enterprise snapshot clamp from Phase 3 (v0.14.0) already binds those at execution start.
- **No cross-process coordination.** The Orchestrator's `JobThrottle` remains the machine-level
  job-admission control; this controller shapes work *inside* one engine process. (The shared
  `MemoryGrantArbiter` already coordinates memory across jobs in one process.)
- **No result changes.** Adaptation may change batch boundaries, worker counts, and spill timing —
  never row content, ordering semantics, or error behavior. Anything that would (e.g. reducing
  external-sort run size changes merge fan-in but not output) must be covered by differential
  correctness tests before it adapts.
- **No prediction/ML.** Reactive control with hysteresis only.
- **Not a replacement for `MemoryGovernorPolicy`.** SpillOrFail/SpillOnly remain the memory
  *enforcement* backstop; the controller's job is to relieve pressure *before* enforcement bites.

---

## 2. Existing machinery this builds on (inventory)

| Mechanism | Where | Role in this design |
| :--- | :--- | :--- |
| `MemoryGrantArbiter` (+ `Shared`) | `ETL-SQL.Core/MemoryGrantArbiter.cs` | Source of truth for grant headroom (`ReservedBytes` / `TotalBudgetBytes`); its lease count approximates active memory-consuming jobs for fairness. |
| `MemoryGovernorPolicy` (SpillOrFail/SpillOnly) | same | Enforcement backstop; unchanged. |
| Per-operator grant (`OperatorMemoryGrantMB`) | `EvaluatorOptions` | Becomes an adaptable setpoint (request sizing), never above the configured value. |
| Batch size (`Engine:BatchSize`, `Evaluator.BatchSize`) | `EvaluatorOptions`, streaming engines | Primary adaptable setpoint. |
| `MaxParallelDegree` (governed) | `ParallelStatementHandler`, external engines | Upper bound for the worker-degree setpoint. |
| 1-deep spill write pipeline (`pendingSpillWrite`) | `InMemoryDataSource.WriteBatchesCore` | Prefetch/pipeline-depth setpoint (0–2). |
| `ScenarioResourceSampler` (tests) | `tests/Scale` | Pattern for cheap CPU/GC sampling; production sampler is a slimmed sibling. |
| Spill telemetry (`TotalSpilledBytes`, extent writes) | `SpillStore`, telemetry | Storage-latency signal source (EWMA of ms/MB per extent write/read). |
| Phase 1 budgets (`Compare-AllocBudget.ps1`) | `scripts/` | Regression harness reused to prove adaptive-off parity and adaptive-on containment. |

---

## 3. Signals

Sampled by one process-wide `ResourceSignalSampler` on a `PeriodicTimer` (default **250 ms**;
`Engine:Adaptive:SampleMs`). All are O(1) reads — no allocation on the sampling path (Phase 1
lesson: the observer must not become the churn).

| Signal | Source | Notes |
| :--- | :--- | :--- |
| `CpuUtilization` (0–1) | Δ`Process.TotalProcessorTime` / wall / `ProcessorCount` | EWMA α=0.3. |
| `MemoryLoad` (0–1) | `GC.GetGCMemoryInfo().MemoryLoadBytes / HighMemoryLoadThresholdBytes` | Container-aware, same API the GC uses for its own pressure decisions. |
| `GrantPressure` (0–1) | `arbiter.ReservedBytes / TotalBudgetBytes` (0 when unbounded) | Direct headroom of the governed pool. |
| `Gen2Rate` | Δ`GC.CollectionCount(2)` per interval, EWMA | Sustained gen2 churn = memory thrash even when load % looks fine. |
| `SpillWriteLatency` | EWMA of ms/MB reported by `ISpillWriter` extent flushes | Storage saturation; reported via a static, lock-free accumulator. |
| `QueueDepth` | Per-pipeline: pending batches in the producer/consumer seam (e.g. spill pipeline depth, PARALLEL semaphore wait count) | Registered by participating pipelines; absent registrations contribute nothing. |

A composite **pressure state** is derived per dimension with two watermarks (defaults;
configurable): `High` (e.g. CPU > 0.90, MemoryLoad > 0.80, GrantPressure > 0.85) and `Low`
(CPU < 0.55, MemoryLoad < 0.55, GrantPressure < 0.50). Between watermarks is the **deadband**:
no changes. This, plus consecutive-sample requirements below, is the anti-oscillation core.

---

## 4. Controller model

One process-wide `AdaptiveExecutionController` owns the loop; each job/evaluator gets a cheap
`AdaptiveAdvisor` view it consults at natural boundaries (batch yield, extent flush, PARALLEL
fan-out, operator admission). **Pull model:** pipelines *ask* for current setpoints when they can
apply them; the controller never interrupts work in flight.

### Setpoints (all clamped to `[floor, configured value]`)

| Setpoint | Floor | Ceiling (never exceeded) | Applied at |
| :--- | :--- | :--- | :--- |
| `BatchRows` | 1,000 | configured `BatchSize` | streaming batch construction (`StreamingQueryEngine`, `WriteBatchesCore` flush size, spill `_flushBatchSize`) |
| `WorkerDegree` | 1 | `min(configured, governed MaxParallelDegree)` | `ParallelStatementHandler` semaphore, external-engine fan-out |
| `PipelineDepth` | 0 | 2 | pending spill writes (`WriteBatchesCore`), extent read-ahead |
| `SpillWriteConcurrency` | 1 | 2 | `ArrowSpillWriter` flush scheduling |
| `OperatorGrantRequestMB` | 64 | configured `OperatorMemoryGrantMB` | external engine admission |

Note the asymmetry with today: ceilings are the *configured* values, so with adaptation disabled
(or in the deadband at startup) behavior is **byte-identical to current defaults**. "Higher
utilization when idle" comes from operators being able to configure *higher* ceilings than they
dare run statically (e.g. `BatchSize 100k`), knowing the controller will retreat under pressure —
not from the controller inventing headroom above configuration.

### Control law: AIMD with hysteresis and cooldown

- **Scale down (fast, multiplicative):** any dimension ≥ `High` for **2 consecutive samples** →
  the setpoints that relieve that dimension are halved (memory pressure → `BatchRows`,
  `OperatorGrantRequestMB`, `PipelineDepth`; CPU pressure → `WorkerDegree`; storage latency →
  `SpillWriteConcurrency`, `PipelineDepth`). Floor-clamped.
- **Scale up (slow, additive):** all dimensions ≤ `Low` for **8 consecutive samples** (~2 s) →
  one step up on one setpoint per interval, round-robin (e.g. `BatchRows += 25% of configured`,
  `WorkerDegree += 1`). Ceiling-clamped.
- **Cooldown:** after any change, no further change for 4 samples (~1 s) so the effect is
  observed before reacting again.
- **Emergency path:** `RegisterAndCheckSpill` returning spill (grant pool exhausted) immediately
  drops `BatchRows`/`OperatorGrantRequestMB` to floor for that job, bypassing sampling — the
  arbiter signal is authoritative and already synchronous.

Asymmetric response (fast down / slow up) plus the deadband and cooldown is the standard,
provable anti-oscillation combination; the unit suite (§8) asserts it on synthetic signal traces.

### 4b. Fairness across concurrent jobs

Fairness rides the shared arbiter, not a new mechanism:

- The controller tracks active advisors (jobs). Each job's **share** = `1 / activeJobs` of the
  grant budget and of `ProcessorCount`.
- A job's effective ceilings under contention become
  `min(configured ceiling, share-derived cap)` with per-job floors (≥1 worker, ≥ floor batch), so
  a job arriving while another saturates the box ramps the incumbent's setpoints down to its
  share rather than starving.
- No preemption: incumbents shrink at scale-down speed, newcomers start at floor and ramp up.
- Single-job case degrades to the plain ceilings (share = 1) — no behavior change.

---

## 5. Configuration & governance

```jsonc
"Engine": {
  "Adaptive": {
    "Enabled": false,          // v0.15.0 default OFF (opt-in); revisit after soak (Phase 6)
    "SampleMs": 250,
    "CpuHigh": 0.90, "CpuLow": 0.55,
    "MemoryHigh": 0.80, "MemoryLow": 0.55,
    "GrantHigh": 0.85, "GrantLow": 0.50,
    "MinBatchRows": 1000,
    "MaxPipelineDepth": 2
  }
}
```

- `SET ADAPTIVE_EXECUTION ON|OFF` toggles per session **within** the config default; it is a
  performance-shape switch, not a resource grant, so it needs no governance ceiling — but if an
  enterprise later wants to force it off, the existing governed-key pattern
  (`Security:…` + `OperationPolicyBoundary`) fits without design change.
- **Governance interaction is one-way:** the controller reads the execution snapshot's governed
  ceilings as immovable maxima. It never writes to `EvaluatorOptions` configured values, so
  `SET`/policy checks observe unchanged configuration; adaptation lives in the advisor's
  *effective* values only.

---

## 6. Determinism & certification interplay

- **Deterministic mode preserved:** `Adaptive:Enabled=false` (the default) is exactly today's
  engine. Additionally `MaxParallelDegree=1` + adaptation off remains the certified
  single-worker debugging mode. Cert lanes (`Test-ScaleCertification`, Gate F,
  `Test-SpillAllocProfile`) run with adaptation **off** unless a scenario explicitly enables it.
- **Results never depend on adaptation.** Batch-size changes may alter batch *boundaries*;
  scenarios asserting per-batch shapes must either pin batch size (they already do via
  `CERT_BATCH_ROWS`) or assert aggregate results only (the cert suite's existing style).
- **Phase 1 budgets are the safety net:** an `AdaptiveOn` variant of the 10M spill-alloc profile
  must pass the same allocation/containment budget — adaptation may not buy throughput with
  memory (the completion gate's explicit requirement).

---

## 7. Delivery plan (three feature slices)

1. **Slice A — signals + controller in observe mode.** `ResourceSignalSampler`,
   `AdaptiveExecutionController` state machine, advisors, telemetry (`SHOW HOST METRICS`-style
   surface + decision log in the execution tree). Decisions are computed and recorded but not
   applied. Unit suite for the control law runs on injected signal traces with a fake clock.
2. **Slice B — pipeline setpoints.** Apply `BatchRows`, `PipelineDepth`,
   `SpillWriteConcurrency` in `StreamingQueryEngine`/`WriteBatchesCore`/`ArrowSpillWriter`.
   Integration tests: ballast-driven memory pressure → observed scale-down before governor
   spill-or-fail; idle box → ramp to configured ceiling; alloc-budget parity adaptive-off,
   containment adaptive-on.
3. **Slice C — worker degree, grant sizing, fairness.** `ParallelStatementHandler`, external
   engine fan-out, `OperatorGrantRequestMB`; two-concurrent-jobs fairness test (shares converge,
   no starvation, incumbents shrink at documented speed).

Each slice merges independently green; adaptation stays default-off through all three.

---

## 8. Test plan

| Layer | Proves | How |
| :--- | :--- | :--- |
| Control-law unit tests (fake clock, injected signals) | No oscillation on step/noise traces; deadband holds; cooldown honored; fast-down/slow-up asymmetry; floors/ceilings never crossed; governed ceiling respected even if config > governed | Pure state-machine tests, no engine |
| Advisor/fairness unit tests | Shares converge for N jobs; single job = full ceilings; newcomer ramp; incumbent shrink | Controller + N fake advisors |
| Integration: pressure | Ballast allocation → scale-down fires before `SpillOrFail` aborts; release ballast → recovery | ScaleCertification-style harness |
| Integration: idle ramp | Idle host, high configured ceilings → setpoints reach ceilings within bounded time | same |
| Budget parity | Adaptive OFF: 10M profile numbers unchanged (Compare-AllocBudget green). Adaptive ON: containment + correctness budgets still green | existing Phase 1 scripts |
| Determinism | Same query, adaptive on/off → identical result checksums (cert scenarios reused) | ScaleCertification |

---

## 9. Open questions (for review before Slice A)

1. **Default-on timing:** proposal is OFF for all of v0.15.0, revisit with Phase 6 soak evidence.
   Agree, or should idle-ramp-only (scale-up without scale-down risk) default on earlier?
2. **`SpillWriteConcurrency` max = 2:** the Phase 1 profile shows the single-writer pipeline is
   not the bottleneck at 10M on NVMe; is a second concurrent extent writer worth its complexity
   in Slice B, or defer to measurements from Slice A's observe mode?
3. **QueueDepth registration surface:** start with just the spill pipeline + PARALLEL semaphore,
   or also instrument connector read-ahead in Slice A?
4. **Scope of external-engine fan-out adaptation (Slice C):** partition-count changes mid-operator
   are not safe; adaptation applies only at operator admission. Confirm that's acceptable
   (an admitted long-running operator keeps its degree).
