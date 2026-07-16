# Architecture Decisions

Decision documents capture why a design, operational policy, or release gate exists. They are supporting context, not the primary source of syntax truth.

## Classification

Each decision document should start with one of these status labels:

- **Current reference** - use as current implementation guidance.
- **Implementation record** - describes shipped work and evidence.
- **Active roadmap** - planned or in-progress work.
- **Historical design note** - useful rationale, but current behavior belongs elsewhere.
- **Superseded** - retained for history only.

## Authoring Rules

- Link to the current guide or reference page for user-facing behavior.
- Link to source files or tests when documenting implementation contracts.
- Keep open questions explicit and date them when possible.
- Move stable facts into `docs/reference/`, `docs/guides/`, or subsystem architecture docs.

Use [Decision Record Template](../../templates/decision-record-template.md) for new decisions.

