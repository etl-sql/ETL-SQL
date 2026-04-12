using System;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Npgsql;
using Oracle.ManagedDataAccess.Client;
using Polly;
using Polly.Retry;
using ETL_SQL.Common;

namespace ETL_SQL.Connectors.Shared
{
    /// <summary>
    /// Provides pre-built Polly resilience pipelines for SQL connector open/execute operations.
    /// Retries up to 3 times with exponential back-off on transient network and timeout errors.
    /// Non-transient errors (syntax, permissions, constraint violations) are never retried.
    /// </summary>
    internal static class ConnectorRetryPolicy
    {
        private const int MaxAttempts = 3;
        private static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(1);

        // ── SqlServer ────────────────────────────────────────────────────────

        public static ResiliencePipeline ForSqlServer(ILogger logger) =>
            new ResiliencePipelineBuilder()
                .AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = MaxAttempts,
                    Delay             = BaseDelay,
                    BackoffType       = DelayBackoffType.Exponential,
                    UseJitter         = true,
                    ShouldHandle      = new PredicateBuilder()
                        .Handle<SqlException>(IsTransientSql)
                        .Handle<TimeoutException>(),
                    OnRetry = args =>
                    {
                        logger.Warning(
                            "SqlServer transient error on attempt {Attempt}/{Max}: {Message}. Retrying in {Delay}ms.",
                            args.AttemptNumber + 1, MaxAttempts, args.Outcome.Exception?.Message, (int)args.RetryDelay.TotalMilliseconds);
                        return ValueTask.CompletedTask;
                    }
                })
                .Build();

        /// <summary>
        /// Returns true for SqlException codes that represent transient network/cloud conditions.
        /// Syntax errors (102), permission errors (229, 230), constraint violations (2627, 547) are NOT transient.
        /// </summary>
        private static bool IsTransientSql(SqlException ex)
        {
            foreach (SqlError error in ex.Errors)
            {
                switch (error.Number)
                {
                    // Timeout / deadlock
                    case -2:     // Client timeout
                    case 1205:   // Deadlock victim
                    // Azure SQL transient errors
                    case 4060:   // Database unavailable
                    case 40197:  // Error processing request
                    case 40501:  // Service busy
                    case 40613:  // Database unavailable (another code)
                    case 49918:  // Cannot process request
                    case 49919:  // Cannot process create/update
                    case 49920:  // Service busy
                    // Network-level
                    case 233:    // Connection does not exist
                    case 10053:  // Transport-level error
                    case 10054:  // Connection forcibly closed
                    case 10060:  // Network-related error
                    case 20:     // SSL provider error
                        return true;
                }
            }
            return false;
        }

        // ── Postgres ─────────────────────────────────────────────────────────

        public static ResiliencePipeline ForPostgres(ILogger logger) =>
            new ResiliencePipelineBuilder()
                .AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = MaxAttempts,
                    Delay             = BaseDelay,
                    BackoffType       = DelayBackoffType.Exponential,
                    UseJitter         = true,
                    ShouldHandle      = new PredicateBuilder()
                        .Handle<NpgsqlException>(IsTransientPg)
                        .Handle<TimeoutException>(),
                    OnRetry = args =>
                    {
                        logger.Warning(
                            "Postgres transient error on attempt {Attempt}/{Max}: {Message}. Retrying in {Delay}ms.",
                            args.AttemptNumber + 1, MaxAttempts, args.Outcome.Exception?.Message, (int)args.RetryDelay.TotalMilliseconds);
                        return ValueTask.CompletedTask;
                    }
                })
                .Build();

        /// <summary>
        /// Returns true for Npgsql exceptions that represent transient conditions.
        /// </summary>
        private static bool IsTransientPg(NpgsqlException ex)
        {
            // IsTransient is exposed directly on NpgsqlException in Npgsql 6+
            if (ex.IsTransient) return true;
            // Additionally catch connection failures and timeouts
            return ex is Npgsql.NpgsqlException { InnerException: System.Net.Sockets.SocketException };
        }

        // ── Oracle ───────────────────────────────────────────────────────────

        public static ResiliencePipeline ForOracle(ILogger logger) =>
            new ResiliencePipelineBuilder()
                .AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = MaxAttempts,
                    Delay             = BaseDelay,
                    BackoffType       = DelayBackoffType.Exponential,
                    UseJitter         = true,
                    ShouldHandle      = new PredicateBuilder()
                        .Handle<OracleException>(IsTransientOracle)
                        .Handle<TimeoutException>(),
                    OnRetry = args =>
                    {
                        logger.Warning(
                            "Oracle transient error on attempt {Attempt}/{Max}: {Message}. Retrying in {Delay}ms.",
                            args.AttemptNumber + 1, MaxAttempts, args.Outcome.Exception?.Message, (int)args.RetryDelay.TotalMilliseconds);
                        return ValueTask.CompletedTask;
                    }
                })
                .Build();

        /// <summary>
        /// Returns true for OracleException codes that are transient.
        /// Excludes ORA-00942 (table not found), ORA-01017 (invalid credentials), etc.
        /// </summary>
        private static bool IsTransientOracle(OracleException ex) =>
            ex.Number switch
            {
                3113  => true,  // End-of-file on communication channel
                3114  => true,  // Not connected to ORACLE
                12150 => true,  // TNS: unable to send data
                12153 => true,  // TNS: not connected
                12157 => true,  // TNS: internal network communication error
                12170 => true,  // TNS: Connect timeout
                12203 => true,  // TNS: unable to connect to destination
                12224 => true,  // TNS: no listener
                12500 => true,  // TNS: listener failed to start a dedicated server process
                12571 => true,  // TNS: packet writer failure
                17002 => true,  // IO Error: Connection reset
                17008 => true,  // Closed Connection
                17410 => true,  // No more data to read from socket
                _     => false
            };
    }
}
