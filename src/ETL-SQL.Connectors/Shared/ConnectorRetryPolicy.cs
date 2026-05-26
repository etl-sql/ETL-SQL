using System;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Npgsql;
using Oracle.ManagedDataAccess.Client;
using System.Data.Odbc;
using Snowflake.Data.Client;
using MySqlConnector;
using Polly;
using Polly.Retry;
using ETL_SQL.Common;
using ETL_SQL.Data;


namespace ETL_SQL.Connectors.Shared
{
    /// <summary>
    /// Provides pre-built Polly resilience pipelines for SQL connector open/execute operations.
    /// Retries are configurable via appsettings.json.
    /// </summary>
    public static class ConnectorRetryPolicy

    {
        private static ConnectorRetryOptions _options = new();

        /// <summary>
        /// Initializes the retry policy with custom options from configuration.
        /// </summary>
        public static void Initialize(ConnectorRetryOptions options)
        {
            _options = options ?? new();
        }

        private static int MaxAttempts => _options.MaxAttempts;
        private static TimeSpan BaseDelay => _options.BaseDelay;


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

        // ── Snowflake ────────────────────────────────────────────────────────

        public static ResiliencePipeline ForSnowflake(ILogger logger) =>
            new ResiliencePipelineBuilder()
                .AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = MaxAttempts,
                    Delay             = BaseDelay,
                    BackoffType       = DelayBackoffType.Exponential,
                    UseJitter         = true,
                    ShouldHandle      = new PredicateBuilder()
                        .Handle<SnowflakeDbException>(IsTransientSnowflake)
                        .Handle<TimeoutException>(),
                    OnRetry = args =>
                    {
                        logger.Warning(
                            "Snowflake transient error on attempt {Attempt}/{Max}: {Message}. Retrying in {Delay}ms.",
                            args.AttemptNumber + 1, MaxAttempts, args.Outcome.Exception?.Message, (int)args.RetryDelay.TotalMilliseconds);
                        return ValueTask.CompletedTask;
                    }
                })
                .Build();

        /// <summary>
        /// Returns true for SnowflakeDbException error codes that represent transient conditions.
        /// 390100=network, 390110=timeout, 250001=no connection available.
        /// </summary>
        private static bool IsTransientSnowflake(SnowflakeDbException ex) =>
            ex.ErrorCode switch
            {
                390100 => true,  // Network error
                390110 => true,  // Connection timeout
                250001 => true,  // No connection available
                _      => false
            };

        // ── BigQuery ─────────────────────────────────────────────────────────

        public static ResiliencePipeline ForBigQuery(ILogger logger) =>
            new ResiliencePipelineBuilder()
                .AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = MaxAttempts,
                    Delay             = BaseDelay,
                    BackoffType       = DelayBackoffType.Exponential,
                    UseJitter         = true,
                    ShouldHandle      = new PredicateBuilder()
                        .Handle<Google.GoogleApiException>(IsTransientBigQuery)
                        .Handle<TimeoutException>(),
                    OnRetry = args =>
                    {
                        logger.Warning(
                            "BigQuery transient error on attempt {Attempt}/{Max}: {Message}. Retrying in {Delay}ms.",
                            args.AttemptNumber + 1, MaxAttempts, args.Outcome.Exception?.Message, (int)args.RetryDelay.TotalMilliseconds);
                        return ValueTask.CompletedTask;
                    }
                })
                .Build();

        /// <summary>
        /// Returns true for GoogleApiException HTTP status codes that represent transient conditions.
        /// 429=rate limit, 500=internal (sometimes transient), 503=service unavailable, 408=request timeout.
        /// </summary>
        private static bool IsTransientBigQuery(Google.GoogleApiException ex) =>
            (int)ex.HttpStatusCode switch
            {
                408 => true,  // Request timeout
                429 => true,  // Rate limit / quota exceeded
                500 => true,  // Internal server error (often transient in cloud)
                503 => true,  // Service unavailable
                _   => false
            };

        // ── ODBC ─────────────────────────────────────────────────────────────

        public static ResiliencePipeline ForOdbc(ILogger logger) =>
            new ResiliencePipelineBuilder()
                .AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = MaxAttempts,
                    Delay             = BaseDelay,
                    BackoffType       = DelayBackoffType.Exponential,
                    UseJitter         = true,
                    ShouldHandle      = new PredicateBuilder()
                        .Handle<OdbcException>(IsTransientOdbc)
                        .Handle<TimeoutException>(),
                    OnRetry = args =>
                    {
                        logger.Warning(
                            "ODBC transient error on attempt {Attempt}/{Max}: {Message}. Retrying in {Delay}ms.",
                            args.AttemptNumber + 1, MaxAttempts, args.Outcome.Exception?.Message, (int)args.RetryDelay.TotalMilliseconds);
                        return ValueTask.CompletedTask;
                    }
                })
                .Build();

        /// <summary>
        /// Returns true for OdbcException codes that represent transient conditions.
        /// Uses SQLState prefixes (08 is connection class, 40 is serializable class).
        /// </summary>
        private static bool IsTransientOdbc(OdbcException ex)
        {
            foreach (OdbcError error in ex.Errors)
            {
                var state = error.SQLState ?? "";
                if (state.StartsWith("08") || // Connection Error
                    state.StartsWith("40") || // Transaction Rollback/Deadlock
                    state == "HYT00" ||       // Timeout expired
                    state == "HYT01")         // Connection timeout expired
                    return true;
            }
            return false;
        }

        // ── MySql ────────────────────────────────────────────────────────────

        public static ResiliencePipeline ForMySql(ILogger logger) =>
            new ResiliencePipelineBuilder()
                .AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = MaxAttempts,
                    Delay             = BaseDelay,
                    BackoffType       = DelayBackoffType.Exponential,
                    UseJitter         = true,
                    ShouldHandle      = new PredicateBuilder()
                        .Handle<MySqlException>(IsTransientMySql)
                        .Handle<TimeoutException>(),
                    OnRetry = args =>
                    {
                        logger.Warning(
                            "MySql transient error on attempt {Attempt}/{Max}: {Message}. Retrying in {Delay}ms.",
                            args.AttemptNumber + 1, MaxAttempts, args.Outcome.Exception?.Message, (int)args.RetryDelay.TotalMilliseconds);
                        return ValueTask.CompletedTask;
                    }
                })
                .Build();

        /// <summary>
        /// Returns true for MySqlException codes that are transient.
        /// </summary>
        private static bool IsTransientMySql(MySqlException ex)
        {
            switch (ex.Number)
            {
                case 1042: // Unable to connect to any of the specified MySQL hosts
                case 1043: // Bad handshake
                case 1152: // Aborted connection to db
                case 1159: // Aborted connection timeout
                case 1160: // Aborted connection write
                case 1205: // Lock wait timeout exceeded; try restarting transaction
                case 1213: // Deadlock found when trying to get lock; try restarting transaction
                    return true;
                default:
                    return false;
            }
        }
    }
}
