# Deployment claim identities shared by execution and release evidence validation.
function Get-DeploymentCertificationLanes {
    param([Parameter(Mandatory)][ValidateSet('Profile', 'Transition')][string]$Kind)
    if ($Kind -eq 'Profile') { return @('Solo', 'Team', 'Enterprise', 'SaaS', 'SharedSaaS') }
    return @('SoloToTeam', 'TeamToEnterprise', 'EnterpriseToSaaS', 'SoloToSaaS', 'SaaSToEnterpriseExit', 'Upgrade')
}

function Get-TopologyClaim {
    param([string]$Name)
    switch ($Name) {
        "Solo" {
            [ordered]@{ lane = $Name; topology = "Local process, local artifacts, optional local SQLite"; claim = "Solo" }
        }
        "Team" {
            [ordered]@{ lane = $Name; topology = "Single-node Orchestrator with SQLite and local artifacts"; claim = "Team single-node" }
        }
        "Enterprise" {
            [ordered]@{ lane = $Name; topology = "Governed Enterprise contract; HA topology requires its separate certification lane"; claim = "Enterprise" }
        }
        "SaaS" {
            [ordered]@{ lane = $Name; topology = "Managed Dedicated (one host-fixed tenant runtime boundary per tenant)"; claim = "Managed Dedicated"; sharedSaaS = "NotCertified" }
        }
        "SharedSaaS" {
            [ordered]@{
                lane = $Name
                topology = "Shared tenant-aware application state and deterministic sandbox adapters"
                claim = "Shared SaaS application isolation and sandbox policy contracts"
                claimScope = "ApplicationAndPolicyContracts"
                sharedSaaS = "NotCertified"
                uncovered = @("Live hardened-runtime containment is not certified by this lane.", "Hosted production regions, fleet capacity and recovery objectives are not certified by this lane.")
            }
        }
        "SoloToTeam" {
            [ordered]@{ lane = $Name; topology = "Solo local state to Team single-node providers"; claim = "Solo to Team" }
        }
        "TeamToEnterprise" {
            [ordered]@{ lane = $Name; topology = "Team single-node providers to governed Enterprise providers"; claim = "Team to Enterprise" }
        }
        "EnterpriseToSaaS" {
            [ordered]@{ lane = $Name; topology = "Enterprise to Managed Dedicated SaaS"; claim = "Enterprise to Managed Dedicated"; sharedSaaS = "NotCertified" }
        }
        "SoloToSaaS" {
            [ordered]@{ lane = $Name; topology = "Solo to Managed Dedicated SaaS"; claim = "Solo to Managed Dedicated"; sharedSaaS = "NotCertified" }
        }
        "SaaSToEnterpriseExit" {
            [ordered]@{ lane = $Name; topology = "Managed Dedicated SaaS to customer-operated self-hosted Enterprise, via the portable tenant bundle"; claim = "Customer exit from Managed Dedicated"; sharedSaaS = "NotCertified" }
        }
        "Upgrade" {
            [ordered]@{ lane = $Name; topology = "In-place profile-preserving N to N+1 upgrade"; claim = "Solo, Team, Enterprise, and Managed Dedicated"; sharedSaaS = "NotCertified" }
        }
    }
}

