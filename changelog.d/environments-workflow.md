### Added

- Added the departmental Environments workflow. `GET /api/admin/environments/plan` derives a full deployment plan from an environment id — databases, artifact root, key ring, service identity, service and unit names, the port block, and the per-environment key requirements — following the naming and port conventions in `Departmental_Isolation.md`. Deriving every resource from the id is what makes a plan checkable rather than a document someone has to follow carefully.

  `POST /api/admin/environments/validate` checks a proposed environment against what this Portal can see: its own environment, the environments named for fleet visibility, and the machine registry. Any shared resource is reported as a collision rather than a warning, because sharing one is enough to break isolation.

  Two boundaries are held deliberately. The Portal **generates plans and never applies them** — creating databases, accounts, key rings and endpoints belongs to a separately authorized deployment plane, since an environment able to provision another is not isolated from it, and the plan states that in the artifact rather than leaving it to the reader. Plans are also **secret-free**: keys appear as requirements at named configuration keys, never generated and never valued, so a plan is safe to review, store, and hand to whoever does the provisioning.

- Added `GET /api/admin/environments/current`, which measures this environment against the isolation contract and links to the read-only fleet workspace. Resources the process cannot observe from inside — a shared database login, two environments running under one OS account, whether a key is unique across environments — are reported as **unknown** rather than assumed isolated. A verification that quietly assumes the answer is worse than one that admits the gap.

- Added `EnvironmentIsolationTests`, which runs two deployments and proves the model rather than describing it: catalogs and search do not merge, a resource id from one environment is meaningless in the other, and a token minted in one is refused by the other while still working where it was minted.
