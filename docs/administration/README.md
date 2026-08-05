# Administration

Operator and administrator documentation for running ETL-SQL in production. Grouped by area; each
area's README lists its pages.

> **New here, or not sure which parts apply to you?**
> [**Administration by deployment profile**](by-profile.md) gives Solo, Team, Enterprise and SaaS
> each an ordered path through these documents. A Solo workstation can skip most of the Portal
> section entirely; a departmental deployment has to apply nearly all of it *per environment*.

## Areas

- [Platform Administration](platform/README.md) - install, configure, secure, scale, back up, and
  monitor the platform and services.
- [Portal Administration](portal/README.md) - administer the Portal application: users,
  permissions, publishing, subscriptions, and audit.
- [Orchestration](orchestration/README.md) - schedule, run, and monitor jobs from the command line,
  including DAGs and CI/CD.

## See Also

- [CLI Reference](../reference/cli/README.md) - every `etl-sql` command, generated from the command tree.
- [Configuration Settings](platform/appsettings-reference.md) and [Data Types](../reference/data-types.md).
- [Documentation Home](../README.md)
