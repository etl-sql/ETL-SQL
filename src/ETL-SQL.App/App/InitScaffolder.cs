using System;
using System.IO;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;

namespace ETL_SQL.App
{
    /// <summary>
    /// Scaffolds a starter configuration and a first runnable ETL-SQL script for CLI-first onboarding.
    /// Idempotent and safe to re-run: existing files are preserved unless <c>--force</c> is supplied.
    /// </summary>
    internal static class InitScaffolder
    {
        internal static Task<int> RunAsync(CliContext ctx, ILogger logger)
        {
            var targetDir = string.IsNullOrWhiteSpace(ctx.InitDirectory)
                ? Directory.GetCurrentDirectory()
                : Path.GetFullPath(ctx.InitDirectory);

            try
            {
                Directory.CreateDirectory(targetDir);
            }
            catch (Exception ex)
            {
                logger.WriteLine($"Could not create target directory '{targetDir}': {ex.Message}", ConsoleColor.Red);
                return Task.FromResult(1);
            }

            int created = 0, skipped = 0;

            WriteFile(Path.Combine(targetDir, "appsettings.json"), BuildStarterConfig(), ctx.InitForce, logger, ref created, ref skipped);
            WriteFile(Path.Combine(targetDir, "hello.etlsql"), BuildStarterScript(), ctx.InitForce, logger, ref created, ref skipped);

            logger.WriteLine("", ConsoleColor.Gray);
            logger.WriteLine($"Initialized ETL-SQL workspace in {targetDir}", ConsoleColor.Green);
            logger.WriteLine($"  Files created: {created}, skipped (already present): {skipped}", ConsoleColor.Gray);
            if (skipped > 0)
                logger.WriteLine("  Re-run with --force to overwrite existing files.", ConsoleColor.Gray);
            logger.WriteLine("", ConsoleColor.Gray);
            logger.WriteLine("Next steps:", ConsoleColor.Cyan);
            logger.WriteLine("  1. Run your first script:   etl-sql run hello.etlsql", ConsoleColor.Gray);
            logger.WriteLine("  2. Verify your environment: etl-sql admin doctor", ConsoleColor.Gray);
            logger.WriteLine("  3. Read the User Manual:    Docs/User_Manual.md", ConsoleColor.Gray);

            return Task.FromResult(0);
        }

        private static void WriteFile(string path, string content, bool force, ILogger logger, ref int created, ref int skipped)
        {
            if (File.Exists(path) && !force)
            {
                logger.WriteLine($"  skip   {Path.GetFileName(path)} (already exists)", ConsoleColor.DarkGray);
                skipped++;
                return;
            }

            File.WriteAllText(path, content);
            logger.WriteLine($"  create {Path.GetFileName(path)}", ConsoleColor.Green);
            created++;
        }

        private static string GenerateJwtSecret()
        {
            var bytes = new byte[32];
            using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes);
        }

        private static string BuildStarterConfig()
        {
            // A minimal-but-valid starter. The Portal JWT secret is generated up front so the portal
            // can start without a separate `config setup-jwt` step. No connector credentials are emitted.
            var jwt = GenerateJwtSecret();
            return $$"""
            {
              "Logging": {
                "LogLevel": { "Default": "Information", "Microsoft": "Warning" },
                "AppLog": { "Directory": "logs/app", "RetentionDays": 30, "FileSizeLimitMb": 10 },
                "ScriptLog": { "Directory": "logs/scripts", "DefaultRetentionDays": 30, "FileSizeLimitMb": 10 }
              },
              "Security": {
                "PathProtectionMode": "Restricted",
                "AllowedHosts": [ "*" ],
                "ApprovedSafeZones": [],
                "MaxFileOperationsPerScript": 100,
                "MaxRecursiveNestingDepth": 5
              },
              "Engine": {
                "BatchSize": 10000,
                "JoinSpillThreshold": 100000,
                "WindowSpillThreshold": 100000,
                "TempTableSpillThresholdRows": 1000000
              },
              "Orchestrator": {
                "ApiKey": "",
                "HistoryDbPath": "./orchestrator.db",
                "ScriptRoot": "./scripts"
              },
              "Portal": {
                "DatabasePath": "./portal.db",
                "ScriptRootPath": "./Reports",
                "SnapshotDirectory": "./Snapshots",
                "Jwt": {
                  "Secret": "{{jwt}}",
                  "ExpiryMinutes": 60,
                  "RefreshExpiryDays": 7
                }
              }
            }
            """;
        }

        private static string BuildStarterScript()
        {
            return """
            -- hello.etlsql — your first ETL-SQL script.
            -- Run it with:  etl-sql run hello.etlsql
            --
            -- MOCKDB is a built-in in-memory sample connector, so this script needs no external
            -- database. Replace it with a real CREATE CONNECTION when you are ready.

            CREATE CONNECTION sample AS MOCKDB();

            -- Select a few rows from the sample 'Users' table and preview them.
            SELECT UserID, UserName, Email
            FROM sample.Users
            WHERE UserID <= 5;

            -- Next: try writing results to a file, e.g.
            --   SELECT UserID, UserName, Email FROM sample.Users INTO 'users.csv';
            -- See Docs/User_Manual.md for connectors, transforms, and reporting.
            """;
        }
    }
}
