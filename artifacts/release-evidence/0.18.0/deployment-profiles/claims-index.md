# Deployment-profile release claims — v0.18.0

| Kind | Lane | Topology | Claim | Shared SaaS | Result | Release eligible | Evidence |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| Profile | Enterprise | Governed Enterprise contract; HA topology requires its separate certification lane | Enterprise | N/A | Passed | True | [20260820-172259/certification.json](20260820-172259/certification.json) |
| Profile | SaaS | Managed Dedicated (one host-fixed tenant runtime boundary per tenant) | Managed Dedicated | NotCertified | Passed | True | [20260820-172259/certification.json](20260820-172259/certification.json) |
| Profile | SharedSaaS | Shared tenant-aware control planes with hardened per-run execution | Shared SaaS hostile isolation | Certified | Passed | True | [20260820-172259/certification.json](20260820-172259/certification.json) |
| Profile | Solo | Local process, local artifacts, optional local SQLite | Solo | N/A | Passed | True | [20260820-172259/certification.json](20260820-172259/certification.json) |
| Profile | Team | Single-node Orchestrator with SQLite and local artifacts | Team single-node | N/A | Passed | True | [20260820-172259/certification.json](20260820-172259/certification.json) |
| Transition | EnterpriseToSaaS | Enterprise to Managed Dedicated SaaS | Enterprise to Managed Dedicated | NotCertified | Passed | True | [20260820-174547/certification.json](20260820-174547/certification.json) |
| Transition | SaaSToEnterpriseExit | Managed Dedicated SaaS to customer-operated self-hosted Enterprise, via the portable tenant bundle | Customer exit from Managed Dedicated | NotCertified | Passed | True | [20260820-174547/certification.json](20260820-174547/certification.json) |
| Transition | SoloToSaaS | Solo to Managed Dedicated SaaS | Solo to Managed Dedicated | NotCertified | Passed | True | [20260820-174547/certification.json](20260820-174547/certification.json) |
| Transition | SoloToTeam | Solo local state to Team single-node providers | Solo to Team | N/A | Passed | True | [20260820-174547/certification.json](20260820-174547/certification.json) |
| Transition | TeamToEnterprise | Team single-node providers to governed Enterprise providers | Team to Enterprise | N/A | Passed | True | [20260820-174547/certification.json](20260820-174547/certification.json) |
| Transition | Upgrade | In-place profile-preserving N to N+1 upgrade | Solo, Team, Enterprise, and Managed Dedicated | NotCertified | Passed | True | [20260820-174547/certification.json](20260820-174547/certification.json) |

Only rows with `releaseEligible = True` support a release claim. Managed Dedicated evidence never certifies Shared SaaS.
