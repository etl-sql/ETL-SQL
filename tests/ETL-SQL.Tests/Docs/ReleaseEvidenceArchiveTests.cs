using System.IO.Compression;

namespace ETL_SQL.Tests.Docs;

public sealed class ReleaseEvidenceArchiveTests
{
    [Theory]
    [InlineData("valid", true)]
    [InlineData("mixed-claim", false)]
    [InlineData("mixed-report", false)]
    [InlineData("mixed-nested-report", false)]
    [InlineData("wrong-version", false)]
    [InlineData("dirty", false)]
    [InlineData("skipped", false)]
    [InlineData("stale", false)]
    [InlineData("missing-log", false)]
    [InlineData("escaping-path", false)]
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
                $bundle = Join-Path $root candidate
                New-Item -ItemType Directory -Path $bundle | Out-Null
                Set-Content -LiteralPath (Join-Path $bundle proof.log) -Value 'Passed 12 tests'
                $phase = @{phase='fixture'; status='Passed'; exitCode=0; command='dotnet test fixture'; startedUtc=$now; completedUtc=$now; log='proof.log'}
                $report = @{schemaVersion='etl-sql.deployment-profile-certification/v1'; commit='candidate-sha'; dirty=$false; releaseEligible=$true; result='Passed'; kind='Profile'; lanes=@('Solo'); phases=@($phase)}
                $claim = @{lane='Solo'; kind='Profile'; result='Passed'; releaseEligible=$true; commit='candidate-sha'; evidence='candidate/certification.json'}
                $index = @{schemaVersion='etl-sql.deployment-profile-release-claims/v1'; releaseVersion='0.20.0'; generatedUtc=$now; claims=@($claim)}
                $old = Join-Path $root historical
                New-Item -ItemType Directory -Path $old | Out-Null
                Set-Content -LiteralPath (Join-Path $old old-report.txt) -Value 'Not this release'
                switch ('{{mutation}}') {
                    'mixed-claim' { $claim.commit = 'old-sha' }
                    'mixed-report' { $report.commit = 'old-sha' }
                    'mixed-nested-report' {
                        $nested = Join-Path $bundle nested
                        New-Item -ItemType Directory -Path $nested | Out-Null
                        @{commit='old-sha'; dirty=$false; result='Passed'} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $nested certification.json)
                    }
                    'wrong-version' { $index.releaseVersion = '0.19.0' }
                    'dirty' { $report.dirty = $true }
                    'skipped' { $phase.status = 'Skipped' }
                    'stale' { $phase.startedUtc = [DateTimeOffset]::UtcNow.AddDays(-30).ToString('O') }
                    'missing-log' { Remove-Item -LiteralPath (Join-Path $bundle proof.log) }
                    'escaping-path' { $claim.evidence = '../old/certification.json' }
                }
                $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $bundle certification.json)
                $index | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $root claims-index.json)
                New-ReleaseCertificationArchive -EvidenceRoot $root -ReleaseVersion '0.20.0' -CandidateCommit 'candidate-sha' -DestinationPath (Join-Path $root candidate.zip)
                """, helperName: "ReleaseEvidence.Helpers.ps1");

            Assert.Equal(accepted, result.ExitCode == 0);
            if (accepted)
            {
                using var archive = ZipFile.OpenRead(destination);
                Assert.Equal(new[] { "archive-manifest.json", "candidate/certification.json", "candidate/proof.log", "claims-index.json" },
                    archive.Entries.Select(entry => entry.FullName).Order(StringComparer.Ordinal));
                using var reader = new StreamReader(archive.GetEntry("archive-manifest.json")!.Open());
                var manifest = await reader.ReadToEndAsync();
                Assert.Contains("candidate-sha", manifest);
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
