# Production-canary certification

- Commit: `bb3007b98e41b8c4c1e68d2dd467acb276b246ec`
- Environment: `hosted-production`
- Synthetic tenant: `synthetic-canary-tenant`
- Result: **Passed**
- Runs: 120 / 120

The evidence covers the normal path plus correctness, availability, latency, and synthetic-dependency drills for every journey, region, and failure domain. Every run must retain the synthetic tenant and dedicated quota boundary.

- [Detailed journey evidence](journey-evidence/production-canary-report.json)
- [Synthetic provisioning evidence](journey-evidence/production-canary-provisioning.json)
- [Credential lifecycle evidence](journey-evidence/production-canary-credential-lifecycle.json)
- [Test log](production-canary-tests.log)
