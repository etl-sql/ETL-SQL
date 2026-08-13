# Gemini Implementation Tasks

These packets are bounded implementation assignments for a junior developer or coding agent. They
advance open `TODO.md` cells without delegating the final security claim or checkbox decision.

## Working agreement

- Read the repository `AGENTS.md` before editing.
- Start each packet from the latest committed branch state and use a separate branch or worktree.
- Do not edit `TODO.md`, `ROADMAP.md`, release claims, capability-matrix colors, or release evidence.
- Do not relax a fail-closed check merely to make Docker Desktop pass.
- Preserve unrelated working-tree changes.
- Use `apply_patch` for edits and commit only files belonging to the packet.
- Return the commit hash, changed files, commands run, test counts, and unresolved questions.
- A passing mock is not real-provider evidence. Label Docker Desktop results as Standard/`runc`.
- Final review and any parent-TODO closure remain the senior engineer's responsibility.

## Suggested order

1. [GEMINI-001 — Sandbox worker image](001-sandbox-worker-image.md)
2. [GEMINI-002 — Standard Docker Desktop execution](002-standard-runc-execution.md), after 001
3. [GEMINI-003 — Shared backup surface inventory](003-shared-backup-surface-inventory.md), independent

Do not combine these packets into one commit. Small, reviewable commits make security review and
rework much safer.
