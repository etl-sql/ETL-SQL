using System.IO.Compression;

namespace ETL_SQL.Tests.Docs;

public sealed class ReleaseEvidenceArchiveTests
{
    [Theory]
    [InlineData("valid", true)]
    [InlineData("valid-default-scope", true)]
    [InlineData("valid-nested", true)]
    [InlineData("mixed-claim", false)]
    [InlineData("mixed-report", false)]
    [InlineData("mixed-nested-report", false)]
    [InlineData("wrong-version", false)]
    [InlineData("dirty", false)]
    [InlineData("missing-fingerprint", false)]
    [InlineData("wrong-fingerprint", false)]
    [InlineData("nested-missing-fingerprint", false)]
    [InlineData("nested-wrong-fingerprint", false)]
    [InlineData("short-candidate", false)]
    [InlineData("skipped", false)]
    [InlineData("stale", false)]
    [InlineData("missing-log", false)]
    [InlineData("escaping-path", false)]
    [InlineData("inflated-claim", false)]
    [InlineData("inflated-scope", false)]
    [InlineData("omitted-gap", false)]
    [InlineData("missing-topology-claim", false)]
    public async Task Archive_SelectsOnlyFreshPassedBundlesForTheExactRelease(string mutation, bool accepted)
    {
        var root = Path.Combine(Path.GetTempPath(), $"etlsql_release_evidence_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var destination = Path.Combine(root, "candidate.zip");
        await File.WriteAllTextAsync(destination, "previous output");
        try
        {
            var result = await ReleasePolicyHelperTests.RunHelper($$"""
                $root = '{{root.Replace("'", "''")}}'
                $now = [DateTimeOffset]::UtcNow.ToString('O')
                $candidate = if ('{{mutation}}' -eq 'short-candidate') { 'candidate-sha' } else { 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' }
                $fingerprint = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($candidate))).ToLowerInvariant()
                $bundle = Join-Path $root candidate
                New-Item -ItemType Directory -Path $bundle | Out-Null
                Set-Content -LiteralPath (Join-Path $bundle proof.log) -Value 'Passed 12 tests'
                $phase = @{phase='fixture'; status='Passed'; exitCode=0; command='dotnet test fixture'; startedUtc=$now; completedUtc=$now; log='proof.log'}
                $topology = @{lane='SharedSaaS'; topology='Shared application state and deterministic adapters'; claim='Application isolation'; claimScope='ApplicationAndPolicyContracts'; sharedSaaS='NotCertified'; uncovered=@('Live hardened runtime not certified')}
                $report = @{schemaVersion='etl-sql.deployment-profile-certification/v1'; commit=$candidate; sourceFingerprint=$fingerprint; dirty=$false; releaseEligible=$true; result='Passed'; kind='Profile'; lanes=@('SharedSaaS'); phases=@($phase); topologyClaims=@($topology)}
                $claim = $topology.Clone()
                $claim.kind = 'Profile'; $claim.result = 'Passed'; $claim.releaseEligible = $true; $claim.commit = $candidate; $claim.evidence = 'candidate/certification.json'
                $index = @{schemaVersion='etl-sql.deployment-profile-release-claims/v1'; releaseVersion='0.20.0'; generatedUtc=$now; claims=@($claim)}
                $old = Join-Path $root historical
                New-Item -ItemType Directory -Path $old | Out-Null
                Set-Content -LiteralPath (Join-Path $old old-report.txt) -Value 'Not this release'
                if ('{{mutation}}' -in @('valid-nested', 'nested-missing-fingerprint', 'nested-wrong-fingerprint')) {
                    $nested = Join-Path $bundle nested
                    New-Item -ItemType Directory -Path $nested | Out-Null
                    $nestedReport = @{commit=$candidate; sourceFingerprint=$fingerprint; dirty=$false; result='Passed'}
                    if ('{{mutation}}' -eq 'nested-missing-fingerprint') { $nestedReport.Remove('sourceFingerprint') }
                    if ('{{mutation}}' -eq 'nested-wrong-fingerprint') { $nestedReport.sourceFingerprint = 'dirty' }
                    $nestedReport | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $nested certification.json)
                }
                switch ('{{mutation}}') {
                    'valid-default-scope' {
                        $topology.Remove('claimScope'); $topology.Remove('sharedSaaS'); $topology.Remove('uncovered')
                        $claim.claimScope = 'ProfileAndTransitionContracts'; $claim.sharedSaaS = 'N/A'; $claim.uncovered = @()
                    }
                    'mixed-claim' { $claim.commit = 'old-sha' }
                    'mixed-report' { $report.commit = 'old-sha' }
                    'mixed-nested-report' {
                        $nested = Join-Path $bundle nested
                        New-Item -ItemType Directory -Path $nested | Out-Null
                        @{commit='old-sha'; dirty=$false; result='Passed'} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $nested certification.json)
                    }
                    'wrong-version' { $index.releaseVersion = '0.19.0' }
                    'dirty' { $report.dirty = $true }
                    'missing-fingerprint' { $report.Remove('sourceFingerprint') }
                    'wrong-fingerprint' { $report.sourceFingerprint = 'dirty' }
                    'skipped' { $phase.status = 'Skipped' }
                    'stale' { $phase.startedUtc = [DateTimeOffset]::UtcNow.AddDays(-30).ToString('O') }
                    'missing-log' { Remove-Item -LiteralPath (Join-Path $bundle proof.log) }
                    'escaping-path' { $claim.evidence = '../old/certification.json' }
                    'inflated-claim' { $claim.claim = 'Hosted production certified'; $claim.sharedSaaS = 'Certified' }
                    'inflated-scope' { $claim.claimScope = 'LiveHardenedRuntime' }
                    'omitted-gap' { $claim.uncovered = @() }
                    'missing-topology-claim' { $report.topologyClaims = @() }
                }
                $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $bundle certification.json)
                $index | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $root claims-index.json)
                New-ReleaseCertificationArchive -EvidenceRoot $root -ReleaseVersion '0.20.0' -CandidateCommit $candidate -DestinationPath (Join-Path $root candidate.zip)
                """, helperName: "ReleaseEvidence.Helpers.ps1");

            Assert.Equal(accepted, result.ExitCode == 0);
            if (accepted)
            {
                using var archive = ZipFile.OpenRead(destination);
                var expectedEntries = new List<string> { "archive-manifest.json", "candidate/certification.json", "candidate/proof.log", "claims-index.json" };
                if (mutation == "valid-nested") expectedEntries.Add("candidate/nested/certification.json");
                Assert.Equal(expectedEntries.Order(StringComparer.Ordinal),
                    archive.Entries.Select(entry => entry.FullName).Order(StringComparer.Ordinal));
                using var reader = new StreamReader(archive.GetEntry("archive-manifest.json")!.Open());
                var manifest = await reader.ReadToEndAsync();
                Assert.Contains("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", manifest);
                Assert.Contains("0.20.0", manifest);
            }
            else
            {
                Assert.Equal("previous output", await File.ReadAllTextAsync(destination));
                Assert.False(string.IsNullOrWhiteSpace(result.Output));
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
