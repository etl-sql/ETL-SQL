using System;

namespace ETL_SQL.Common
{
    public interface ILoggerService
    {
        void InitializeAppLogger(string logDirectory, int retentionDays = 30, int fileSizeLimitMb = 10);
        void InitializeScriptLogger(string sourceScript, string logDirectory, int retentionDays = 30, int fileSizeLimitMb = 10);
        void InitializeTestLogger(string logDirectory, int retentionDays = 30, int fileSizeLimitMb = 50);
    }
}
