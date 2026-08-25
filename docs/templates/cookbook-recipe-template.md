# Recipe Title

> **Page-type: Cookbook recipe**
> Owns: one self-contained, runnable end-to-end scenario
> (extract → stage → validate → merge → cleanup → notify).
> Links to (does not restate): guide pages for workflow context; reference pages for syntax.
> Required sections: Goal, Requirements, Complete Script, Validation, Cleanup,
> Operational Notes.
> The complete script must run as-is without editing beyond connection and credential
> substitution.

Describe the scenario and final outcome.

## Goal

What this recipe accomplishes.

## Requirements

- Required connectors, source data, destinations, permissions, and secrets.

## Complete Script

```sql
-- Complete runnable script covering:
-- Extract → Stage → Validate → Merge → Cleanup → Notify
```

## Validation

```sql
-- Queries or checks that prove the recipe worked.
```

## Cleanup

```sql
-- Cleanup or rollback commands when applicable.
```

## Operational Notes

Document scheduling, WHAT_IF usage, lineage, audit, secrets, retries, and alerting
considerations.
