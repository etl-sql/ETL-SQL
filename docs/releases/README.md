# Release Notes Authoring Guide

This directory contains curated, user-facing release notes for every ETL-SQL version.
Each file (`vx.y.z.md`) is the single source of truth that ships with the GitHub Release,
is referenced by the VS Code extension "What's New" page, and is indexed by the Report
Portal `/about` route.

> **Key file:** [`TEMPLATE.md`](TEMPLATE.md) — copy this file to start a new release notes
> draft. Every section is documented with inline guidance that you delete as you fill it in.

---

## Why Curated Notes Matter

The `CHANGELOG.md` is an exhaustive, developer-oriented log maintained as work lands.
Release notes serve a different purpose: they tell **users, administrators, and evaluators**
what changed, why it matters to them, and what they need to do. A good release note answers
three questions for every audience:

1. **What changed?** — concrete capability or behavior delta.
2. **Why should I care?** — user-facing benefit, risk reduction, or productivity gain.
3. **What do I need to do?** — upgrade steps, config changes, migration actions.

---

## Audiences

ETL-SQL ships to multiple audiences. Each section of the release notes template is tagged
with the audiences it serves:

| Audience | What they look for |
| :--- | :--- |
| **Script Authors** | New language features, functions, connectors, IDE improvements |
| **Report Consumers** | New visual types, interactivity, performance, Portal UX |
| **Administrators** | Security patches, config changes, HA updates, governance, upgrade steps |
| **Evaluators / Decision Makers** | Headline capabilities, maturity signals, competitive differentiators |

---

## Quality Bar

Before a release notes file is committed, verify it meets these standards:

- [ ] **Headline summary** — 1–3 sentences framing the release theme; scannable in 5 seconds.
- [ ] **Every highlight answers "why"** — not just what was built, but the user problem it solves.
- [ ] **Breaking changes are prominent** — listed before general improvements, with migration steps.
- [ ] **Deprecations include timelines** — when will it be removed, and what replaces it.
- [ ] **Known issues are honest** — anything shipped-but-incomplete or with workarounds.
- [ ] **Upgrade instructions are concrete** — exact commands, config keys, or migration scripts.
- [ ] **Performance claims are quantified** — "2× faster" not "improved performance."
- [ ] **Security items reference CVEs or design docs** — not vague "hardening" language.
- [ ] **Links work** — changelog, design docs, migration guides, and admin guide references resolve.
- [ ] **Consistent structure** — follows `TEMPLATE.md` section order so users build reading habits.

---

## Authoring Workflow

1. **Start early.** Copy `TEMPLATE.md` to `vx.y.z.md` when the release branch is created,
   not the day before tagging.
2. **Populate incrementally.** As features merge into the release branch, add their highlight
   entry immediately while context is fresh.
3. **Cross-reference design docs.** For every major feature, check `docs/architecture/decisions/` and
   `docs/architecture/roadmaps/` — pull the "why" and architectural context into the release note.
4. **Review the diff.** Before finalizing, run `git diff --stat vLAST..HEAD -- src` and
   verify no shipped feature is missing from the notes.
5. **Peer review.** Release notes are prose — they benefit from a second pair of eyes for
   clarity, completeness, and tone.

---

## File Naming Convention

| Release Type | Filename | Example |
| :--- | :--- | :--- |
| Official release | `vx.y.z.md` | `v0.15.0.md` |
| Patch release | `vx.y.z.md` | `v0.15.1.md` |
| Unofficial / backfilled | `vx.y.z.md` (with "Unofficial" label in header) | `v0.1.0.md` |

---

## Integration Points

| Consumer | How it uses the release notes |
| :--- | :--- |
| `scripts/Invoke-Release.ps1` | Resolves `docs/releases/$Tag.md` as the GitHub Release body |
| `scripts/invoke-release.sh` | Same resolution for POSIX environments |
| GitHub Release page | Body text comes from this file |
| VS Code extension | Links to the release notes URL in the "What's New" notification |
| Portal `/about` | Displays the current version's release summary |
| `docs/guides/release-checklist.md` | Phase 1 requires authoring this file before tagging |

---

## References

- [TEMPLATE.md](TEMPLATE.md) — the starting point for every new release
- [Release Checklist](../guides/release-checklist.md) — the full release process
- [CHANGELOG.md](../../CHANGELOG.md) — exhaustive developer-oriented change log
- [Release Capability Matrix](../architecture/roadmaps/Release_Capability_Matrix.md) — evidence requirements
- [Release Workflows](../architecture/roadmaps/Release_Workflows.md) — CI/CD and validation strategy
