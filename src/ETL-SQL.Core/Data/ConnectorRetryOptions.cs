using System;

namespace ETL_SQL.Data
{
    /// <summary>
    /// Configuration options for the Polly-based retry policy used by SQL connectors.
    /// </summary>
    public class ConnectorRetryOptions
    {
        /// <summary>
        /// Maximum number of retry attempts for transient errors. Default is 3.
        /// </summary>
        public int MaxAttempts { get; set; } = 3;

        /// <summary>
        /// The base delay (in seconds) for exponential backoff. Default is 1.
        /// </summary>
        public double BaseDelaySeconds { get; set; } = 1.0;

        /// <summary>
        /// Returns the base delay as a TimeSpan.
        /// </summary>
        public TimeSpan BaseDelay => TimeSpan.FromSeconds(BaseDelaySeconds);
    }
}
