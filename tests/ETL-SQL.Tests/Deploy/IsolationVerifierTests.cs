using System.Diagnostics;
using System.IO;
using Xunit;

namespace ETL_SQL.Tests.Deploy
{
    /// <summary>
    /// Certifies the departmental-isolation verifier (deploy/verify/Test-Isolation.ps1): it passes
    /// when environments are distinct and fails when any two share a port, key, data root, etc.
    /// Self-skips when PowerShell 7 (pwsh) is not on PATH so it does not fail a pwsh-less CI lane.
    /// </summary>
    public class IsolationVerifierTests
    {
        private static readonly string RepoRoot =
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

        private static string ScriptPath =>
            Path.Combine(RepoRoot, "deploy", "verify", "Test-Isolation.ps1");

        [Fact]
        public void Verifier_PassesForDistinctEnvironments()
        {
            using var dir = new TempDir();
            File.WriteAllText(Path.Combine(dir.Path, "dev.env"),
                "ETLSQL_ENV=dev\nENV_DATA_ROOT=/srv/etl-sql/dev\nPORT_PORTAL=5000\nPORTAL_JWT_SECRET=s-dev\nORCH_API_KEY=k-dev\n");
            File.WriteAllText(Path.Combine(dir.Path, "prod.env"),
                "ETLSQL_ENV=prod\nENV_DATA_ROOT=/srv/etl-sql/prod\nPORT_PORTAL=5010\nPORTAL_JWT_SECRET=s-prod\nORCH_API_KEY=k-prod\n");

            var exit = RunVerifier(Path.Combine(dir.Path, "dev.env"), Path.Combine(dir.Path, "prod.env"));
            if (exit is null) return; // pwsh unavailable — skip

            Assert.Equal(0, exit);
        }

        [Fact]
        public void Verifier_FailsWhenEnvironmentsShareKeyOrPort()
        {
            using var dir = new TempDir();
            File.WriteAllText(Path.Combine(dir.Path, "dev.env"),
                "ETLSQL_ENV=dev\nENV_DATA_ROOT=/srv/etl-sql/dev\nPORT_PORTAL=5000\nPORTAL_JWT_SECRET=shared\nORCH_API_KEY=k-dev\n");
            // prod reuses dev's port and JWT secret — a hard isolation violation.
            File.WriteAllText(Path.Combine(dir.Path, "prod.env"),
                "ETLSQL_ENV=prod\nENV_DATA_ROOT=/srv/etl-sql/prod\nPORT_PORTAL=5000\nPORTAL_JWT_SECRET=shared\nORCH_API_KEY=k-prod\n");

            var exit = RunVerifier(Path.Combine(dir.Path, "dev.env"), Path.Combine(dir.Path, "prod.env"));
            if (exit is null) return; // pwsh unavailable — skip

            Assert.Equal(1, exit);
        }

        [Fact]
        public void Verifier_DoesNotFlagHaNodesOfTheSameEnvironment()
        {
            using var dir = new TempDir();
            // Two descriptors for the SAME environment (HA nodes) legitimately share everything.
            var body = "ETLSQL_ENV=prod\nENV_DATA_ROOT=/srv/etl-sql/prod\nPORT_PORTAL=5010\nPORTAL_JWT_SECRET=s-prod\nORCH_API_KEY=k-prod\n";
            File.WriteAllText(Path.Combine(dir.Path, "prod-node1.env"), body);
            File.WriteAllText(Path.Combine(dir.Path, "prod-node2.env"), body);

            var exit = RunVerifier(Path.Combine(dir.Path, "prod-node1.env"), Path.Combine(dir.Path, "prod-node2.env"));
            if (exit is null) return; // pwsh unavailable — skip

            Assert.Equal(0, exit);
        }

        private static int? RunVerifier(params string[] descriptors)
        {
            var args = new List<string> { "-NoProfile", "-File", ScriptPath };
            args.AddRange(descriptors);

            var psi = new ProcessStartInfo("pwsh")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            foreach (var a in args) psi.ArgumentList.Add(a);

            try
            {
                using var proc = Process.Start(psi);
                if (proc is null) return null;
                proc.StandardOutput.ReadToEnd();
                proc.StandardError.ReadToEnd();
                proc.WaitForExit(60_000);
                return proc.ExitCode;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return null; // pwsh not installed
            }
        }

        private sealed class TempDir : IDisposable
        {
            public string Path { get; } =
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "iso_verify_" + Guid.NewGuid().ToString("N")[..8]);

            public TempDir() => Directory.CreateDirectory(Path);

            public void Dispose()
            {
                try { Directory.Delete(Path, recursive: true); } catch { }
            }
        }
    }
}
