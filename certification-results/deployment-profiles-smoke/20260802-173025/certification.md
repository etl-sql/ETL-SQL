# Deployment-profile certification

- Commit: `ec4cb8bfe5dab170f1cfda6700aa46f4a1442fc2`
- Worktree dirty: `True`
- Lanes: SoloToTeam, TeamToEnterprise, EnterpriseToSaaS, SoloToSaaS, Upgrade
- Result: **Passed**

| Lane | Phase | Result | Evidence |
| :--- | :--- | :--- | :--- |
| Contract | Deployment journey fixture contract | Passed | [contract-deployment-journey-fixture-contract.log](contract-deployment-journey-fixture-contract.log) |
| SoloToTeam | Solo to Team lifecycle | Passed | [solototeam-solo-to-team-lifecycle.log](solototeam-solo-to-team-lifecycle.log) |
| SoloToTeam | Solo to Team inventory and import | Passed | [solototeam-solo-to-team-inventory-and-import.log](solototeam-solo-to-team-inventory-and-import.log) |
| SoloToTeam | Solo to Team Portal replay | Passed | [solototeam-solo-to-team-portal-replay.log](solototeam-solo-to-team-portal-replay.log) |
| TeamToEnterprise | Team to Enterprise lifecycle | Passed | [teamtoenterprise-team-to-enterprise-lifecycle.log](teamtoenterprise-team-to-enterprise-lifecycle.log) |
| TeamToEnterprise | Team to Enterprise state migration | Passed | [teamtoenterprise-team-to-enterprise-state-migration.log](teamtoenterprise-team-to-enterprise-state-migration.log) |
| TeamToEnterprise | Team to Enterprise configuration promotion | Passed | [teamtoenterprise-team-to-enterprise-configuration-promotion.log](teamtoenterprise-team-to-enterprise-configuration-promotion.log) |
| EnterpriseToSaaS | Enterprise to SaaS lifecycle | Passed | [enterprisetosaas-enterprise-to-saas-lifecycle.log](enterprisetosaas-enterprise-to-saas-lifecycle.log) |
| EnterpriseToSaaS | Enterprise to SaaS onboarding | Passed | [enterprisetosaas-enterprise-to-saas-onboarding.log](enterprisetosaas-enterprise-to-saas-onboarding.log) |
| EnterpriseToSaaS | Enterprise to SaaS Portal validation | Passed | [enterprisetosaas-enterprise-to-saas-portal-validation.log](enterprisetosaas-enterprise-to-saas-portal-validation.log) |
| SoloToSaaS | Solo to SaaS lifecycle | Passed | [solotosaas-solo-to-saas-lifecycle.log](solotosaas-solo-to-saas-lifecycle.log) |
| SoloToSaaS | Solo to SaaS onboarding | Passed | [solotosaas-solo-to-saas-onboarding.log](solotosaas-solo-to-saas-onboarding.log) |
| Upgrade | All-profile upgrade lifecycle | Passed | [upgrade-all-profile-upgrade-lifecycle.log](upgrade-all-profile-upgrade-lifecycle.log) |
| Upgrade | Profile contract upgrade invariants | Passed | [upgrade-profile-contract-upgrade-invariants.log](upgrade-profile-contract-upgrade-invariants.log) |
| Upgrade | Portal N to N+1 migration and restore | Passed | [upgrade-portal-n-to-n-1-migration-and-restore.log](upgrade-portal-n-to-n-1-migration-and-restore.log) |

## Uncovered

None for the selected lane contract.
