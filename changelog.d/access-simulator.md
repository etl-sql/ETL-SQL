### Added

- Added an identity access simulator: `GET /api/admin/access-simulator/user/{id}?reportId=&datasetId=` explains what one identity can reach and **why**, composing roles, groups, folder and report ACLs, dataset grants, shared-connection grants, Studio capability, and row-level security into a single answer that names its sources. Each of those was already queryable on its own, which was the problem — reconstructing "why can this person open that report?" meant checking five surfaces and composing them by hand.

  Row-level security is explained by naming the identity tokens the script filters on and the values that would be bound for the user. The report is never run, and a test asserts that no data from it appears anywhere in the response: a tool for auditing who can see data must not become a way to see it.

  The report answer and its explanation are both produced by `FolderPermissionService`, so the diagnostic cannot drift from the enforcement it describes. Reading another identity's effective access is itself a privileged act and is audited as `SIMULATE_ACCESS`.
