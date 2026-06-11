using ETL_SQL.ReportPortal.Services;

namespace ETL_SQL.ReportPortal.Tests;

public class ExecutionJobTrustTests
{
    [Fact]
    public void InteractiveExecution_UsesRealUserDatasetContext()
    {
        var job = new ExecutionJob("interactive", ReportId: 10, UserId: 42);

        Assert.False(job.TrustedDatasetExecution);
        Assert.Equal("UserId=42", job.DatasetCallerContext);
    }

    [Fact]
    public void ScheduledRefresh_RequiresExplicitTrustedFlag()
    {
        var untrustedSystemUser = new ExecutionJob("untrusted", ReportId: 10, UserId: 0);
        var scheduled = new ExecutionJob(
            "scheduled",
            ReportId: 10,
            UserId: 0,
            TrustedDatasetExecution: true);

        Assert.Equal("UserId=0", untrustedSystemUser.DatasetCallerContext);
        Assert.Equal("IsAdmin=true", scheduled.DatasetCallerContext);
    }

    [Fact]
    public void InteractiveAdministrator_PreservesIdentityAndAdminRole()
    {
        var job = new ExecutionJob(
            "admin",
            ReportId: 10,
            UserId: 42,
            IsAdministrator: true);

        Assert.Equal("UserId=42;IsAdmin=true", job.DatasetCallerContext);
    }
}
