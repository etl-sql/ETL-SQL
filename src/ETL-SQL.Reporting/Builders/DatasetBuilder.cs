using System;
using ETL_SQL.Core;

namespace ETL_SQL.ReportBuilder.Builders
{
    public class DatasetBuilder()
    {
        public DatasetManifest Build(CreateDatasetStatement dsStmt)
        {
            return new DatasetManifest
            {
                TempTableName   = dsStmt.TempTableName,
                RefreshInterval = dsStmt.RefreshInterval,
                Ttl             = dsStmt.Ttl,
                LastRefresh     = DateTime.UtcNow,
                RowCount        = 0 // To be filled by the caller if needed
            };
        }
    }
}
