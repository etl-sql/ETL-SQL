using System;

namespace ETL_SQL.Core.Common
{
    /// <summary>
    /// Represents the current execution state of a statement in the ETL pipeline.
    /// </summary>
    public enum ExecutionStatus
    {
        /// <summary>Statement is queued or waiting for a parent/dependency to finish.</summary>
        Waiting,

        /// <summary>Statement is currently executing.</summary>
        Running,

        /// <summary>Statement finished successfully.</summary>
        Completed,

        /// <summary>Statement failed due to an error.</summary>
        Faulted
    }
}
