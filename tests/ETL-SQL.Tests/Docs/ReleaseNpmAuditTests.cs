namespace ETL_SQL.Tests.Docs;

public sealed class ReleaseNpmAuditTests
{
    [Theory]
    [InlineData("{\"metadata\":{\"vulnerabilities\":{\"total\":0}},\"vulnerabilities\":{}}", 0, 0)]
    [InlineData("{\"metadata\":{\"vulnerabilities\":{\"total\":1,\"high\":1}},\"vulnerabilities\":{\"brace-expansion\":{\"severity\":\"high\"}}}", 1, 1)]
    public async Task AuthoritativeReport_PreservesFindings(string json, int exitCode, int expectedTotal)
    {
        var result = await RunAudit(json, exitCode);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Equal(expectedTotal.ToString(), result.Output.Trim());
    }

    [Theory]
    [InlineData("null", 0)]
    [InlineData("{}", 0)]
    [InlineData("{\"error\":{\"code\":\"ENOAUDIT\"}}", 1)]
    [InlineData("{\"metadata\":{\"vulnerabilities\":{\"total\":0}},\"vulnerabilities\":{}}", 7)]
    public async Task MissingOrFailedReport_DoesNotBecomeACleanAudit(string json, int exitCode)
    {
        var result = await RunAudit(json, exitCode);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("npm audit", result.Output);
    }

    private static Task<(int ExitCode, string Output)> RunAudit(string json, int exitCode) =>
        ReleasePolicyHelperTests.RunHelper($$"""
            $tokens = $null
            $errors = $null
            $ast = [Management.Automation.Language.Parser]::ParseFile(
                (Join-Path $PWD 'scripts/Test-PreRelease.ps1'), [ref]$tokens, [ref]$errors)
            if ($errors.Count) { throw 'Release script parse failed' }
            $function = $ast.Find({ param($node)
                $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
                $node.Name -eq 'Get-NpmAuditFindings'
            }, $true)
            if (-not $function) { throw 'Audit function missing' }
            . ([scriptblock]::Create($function.Extent.Text))
            $report = Get-NpmAuditFindings -AuditResult @{
                exitCode = {{exitCode}}
                json = '{{json}}' | ConvertFrom-Json
            }
            $report.total
            """);
}
