using System;
using System.Linq;
using ETL_SQL.Analysis.Services;
using Xunit;

namespace ETL_SQL.Tests.Analysis;

/// <summary>
/// The handoff from a document that runs to a job that recurs.
///
/// <para>The refusals are the important half. A job names a path on a server, so scheduling an
/// unsaved buffer produces a job that fails on its first tick with a missing file — hours later, to
/// somebody else. And two jobs sharing a cadence should share the schedule that names it, or
/// changing the cadence means finding every copy.</para>
/// </summary>
public class ScriptScheduleHandoffTests
{
    private readonly ScriptScheduleHandoffService _service = new();

    private const string Script = """
        CREATE CONNECTION corp AS MOCKDB();
        SELECT region, total INTO #sales FROM corp.orders;
        """;

    // ── Reading ──────────────────────────────────────────────────────────────

    [Fact]
    public void An_unsaved_document_cannot_be_scheduled_and_says_why()
    {
        var handoff = _service.Read(Script, null);

        Assert.True(handoff.Parsed);
        Assert.False(handoff.CanSchedule);
        Assert.Contains("Save this document", handoff.Reason);
    }

    [Fact]
    public void A_saved_document_suggests_itself_as_the_target()
    {
        var handoff = _service.Read(Script, "reports/sales.etlsql");

        Assert.True(handoff.CanSchedule);
        Assert.Equal("reports/sales.etlsql", handoff.SuggestedTarget);
        Assert.Equal("script", handoff.SuggestedTargetKind);
    }

    [Fact]
    public void A_report_document_is_scheduled_as_a_report()
    {
        Assert.Equal("report", _service.Read(Script, "reports/sales.rptsql").SuggestedTargetKind);
    }

    [Fact]
    public void Reports_the_jobs_and_schedules_a_script_already_declares()
    {
        var handoff = _service.Read("""
            CREATE SCHEDULE Nightly ON '0 2 * * *' AT TIME ZONE 'UTC';
            CREATE JOB SalesRefresh FOR SCRIPT 'reports/sales.etlsql';
            ALTER JOB SalesRefresh ADD SCHEDULE Nightly;
            """, "reports/sales.etlsql");

        var schedule = Assert.Single(handoff.Schedules);
        Assert.Equal("Nightly", schedule.Name);
        Assert.Equal("0 2 * * *", schedule.Cron);
        Assert.Equal("UTC", schedule.TimeZone);

        var job = Assert.Single(handoff.Jobs);
        Assert.Equal("SalesRefresh", job.Job);
        Assert.Equal("reports/sales.etlsql", job.Target);
        Assert.Equal(["Nightly"], job.Schedules);
    }

    // ── Writing ──────────────────────────────────────────────────────────────

    [Fact]
    public void Writes_the_schedule_the_job_and_the_link_between_them()
    {
        var result = _service.Schedule(Script, "reports/sales.etlsql", "SalesRefresh", "Nightly", "0 2 * * *", "UTC", null);

        Assert.True(result.Applied, result.Error);
        Assert.Equal("SalesRefresh", result.Job);
        Assert.Contains("CREATE SCHEDULE Nightly ON '0 2 * * *' AT TIME ZONE 'UTC';", result.Script, StringComparison.Ordinal);
        Assert.Contains("CREATE JOB SalesRefresh FOR SCRIPT 'reports/sales.etlsql';", result.Script, StringComparison.Ordinal);
        Assert.Contains("ALTER JOB SalesRefresh ADD SCHEDULE Nightly;", result.Script, StringComparison.Ordinal);

        // And it reads back as one job on one schedule, which is what the panel then shows.
        var handoff = _service.Read(result.Script, "reports/sales.etlsql");
        Assert.Equal(["Nightly"], Assert.Single(handoff.Jobs).Schedules);
    }

    [Fact]
    public void A_report_document_is_written_as_a_report_job()
    {
        var result = _service.Schedule(Script, "reports/sales.rptsql", "SalesReport", "Nightly", "0 2 * * *", null, null);

        Assert.True(result.Applied, result.Error);
        Assert.Contains("CREATE JOB SalesReport FOR REPORT 'reports/sales.rptsql';", result.Script, StringComparison.Ordinal);
    }

    [Fact]
    public void An_existing_schedule_is_reused_rather_than_duplicated()
    {
        var first = _service.Schedule(Script, "reports/sales.etlsql", "SalesRefresh", "Nightly", "0 2 * * *", null, null);
        var second = _service.Schedule(first.Script, "reports/sales.etlsql", "SalesArchive", null, null, null, "Nightly");

        Assert.True(second.Applied, second.Error);
        Assert.Equal(1, second.Script.Split("CREATE SCHEDULE").Length - 1);
        Assert.Contains("ALTER JOB SalesArchive ADD SCHEDULE Nightly;", second.Script, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_to_schedule_an_unsaved_document()
    {
        var result = _service.Schedule(Script, null, "SalesRefresh", "Nightly", "0 2 * * *", null, null);

        Assert.False(result.Applied);
        Assert.Contains("Save this document", result.Error);
        Assert.Equal(Script, result.Script);
    }

    [Fact]
    public void Refuses_a_job_name_the_language_cannot_read_back()
    {
        var result = _service.Schedule(Script, "reports/sales.etlsql", "sales refresh", "Nightly", "0 2 * * *", null, null);

        Assert.False(result.Applied);
        Assert.Contains("usable job name", result.Error);
    }

    [Fact]
    public void Refuses_a_second_job_with_a_name_the_script_already_uses()
    {
        var first = _service.Schedule(Script, "reports/sales.etlsql", "SalesRefresh", "Nightly", "0 2 * * *", null, null);
        var second = _service.Schedule(first.Script, "reports/sales.etlsql", "SalesRefresh", "Hourly", "0 * * * *", null, null);

        Assert.False(second.Applied);
        Assert.Contains("already declares a job", second.Error);
    }

    [Fact]
    public void Refuses_to_reuse_a_schedule_the_script_does_not_declare()
    {
        var result = _service.Schedule(Script, "reports/sales.etlsql", "SalesRefresh", null, null, null, "Nightly");

        Assert.False(result.Applied);
        Assert.Contains("Nightly", result.Error);
    }

    [Fact]
    public void Refuses_a_new_schedule_with_no_cadence()
    {
        var result = _service.Schedule(Script, "reports/sales.etlsql", "SalesRefresh", "Nightly", null, null, null);

        Assert.False(result.Applied);
        Assert.Contains("cadence", result.Error);
    }

    [Fact]
    public void Leaves_the_script_it_appends_to_alone()
    {
        var result = _service.Schedule(Script, "reports/sales.etlsql", "SalesRefresh", "Nightly", "0 2 * * *", null, null);

        Assert.True(result.Applied, result.Error);
        foreach (var line in Script.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Trim().Length > 0))
            Assert.Contains(line, result.Script);
    }
}
