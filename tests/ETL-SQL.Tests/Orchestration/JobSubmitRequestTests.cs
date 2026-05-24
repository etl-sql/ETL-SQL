using System.Collections.Generic;
using ETL_SQL.Orchestrator.Channels;
using Xunit;

namespace ETL_SQL.Tests.Orchestration
{
    public class JobSubmitRequestTests
    {
        [Fact]
        public void GetLineageJobName_ForReport_UsesReportIdentityAndSession()
        {
            var request = new JobSubmitRequest
            {
                ScriptText = "SELECT 1;",
                SessionId = "refresh-session",
                Label = "Report 42 Execution",
                Metadata = new Dictionary<string, string>
                {
                    ["IsReport"] = "true",
                    ["ReportId"] = "42"
                }
            };

            Assert.Equal("report:42:refresh-session", request.GetLineageJobName("fallback"));
        }

        [Fact]
        public void GetLineageJobName_ForNonReport_UsesLabel()
        {
            var request = new JobSubmitRequest
            {
                ScriptText = "SELECT 1;",
                Label = "Adhoc Load"
            };

            Assert.Equal("Adhoc Load", request.GetLineageJobName("fallback"));
        }
    }
}
