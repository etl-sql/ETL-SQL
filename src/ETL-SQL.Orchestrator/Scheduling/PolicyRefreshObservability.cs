using System.Diagnostics;
using System.Diagnostics.Metrics;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Observability;

namespace ETL_SQL.Orchestrator.Scheduling;

internal static class PolicyRefreshObservability
{
    public const string ActivitySourceName = "ETL-SQL.Orchestrator.Policy";
    public const string MeterName = "ETL-SQL.Orchestrator.Policy";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    public static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> RefreshCompletedCounter =
        Meter.CreateCounter<long>("etlsql.orchestrator.policy_refresh.completed");
    private static readonly Histogram<double> RefreshDurationMs =
        Meter.CreateHistogram<double>("etlsql.orchestrator.policy_refresh.duration_ms");

    public static Activity? StartRefreshActivity()
    {
        var activity = ActivitySource.StartActivity("orchestrator.policy_refresh", ActivityKind.Internal);
        if (activity is null)
            return null;

        activity.SetTag(ObservabilityConventions.Tags.Environment, Environment.GetEnvironmentVariable("ETLSQL_ENV") ?? "default");
        activity.SetTag(ObservabilityConventions.Tags.Node, Environment.MachineName);
        activity.SetTag(ObservabilityConventions.Tags.Component, "orchestrator");
        activity.SetTag(ObservabilityConventions.Tags.WorkloadKind, "policy-refresh");
        return activity;
    }

    public static void CompleteRefreshActivity(Activity? activity, EffectiveEnterprisePolicy? policy,
        string status, long durationMs)
    {
        if (activity is not null)
        {
            activity.SetTag(ObservabilityConventions.Tags.Status, status);
            if (!string.IsNullOrWhiteSpace(policy?.PolicyVersion))
                activity.SetTag(ObservabilityConventions.Tags.PolicyVersion, policy.PolicyVersion);
            var policyHash = policy is null
                ? null
                : ExecutionPolicySnapshot.Capture(policy, "orchestrator", ScriptExecutionMode.Batch, "policy-refresh")
                    .PolicyHash;
            if (!string.IsNullOrWhiteSpace(policyHash))
                activity.SetTag(ObservabilityConventions.Tags.PolicyHash, policyHash);
            activity.SetStatus(status is "success" ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
        }

        var tags = new TagList
        {
            { ObservabilityConventions.Tags.Environment, Environment.GetEnvironmentVariable("ETLSQL_ENV") ?? "default" },
            { ObservabilityConventions.Tags.Node, Environment.MachineName },
            { ObservabilityConventions.Tags.Component, "orchestrator" },
            { ObservabilityConventions.Tags.WorkloadKind, "policy-refresh" },
            { ObservabilityConventions.Tags.Status, status }
        };

        RefreshCompletedCounter.Add(1, tags);
        RefreshDurationMs.Record(Math.Max(0, durationMs), tags);
    }
}
