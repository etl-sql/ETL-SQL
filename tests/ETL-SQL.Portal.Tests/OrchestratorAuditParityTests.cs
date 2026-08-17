using System.Net;
using System.Net.Http.Json;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Governance;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Audit parity for the Orchestrator's HTTP mutation surface.
///
/// <para>The per-object authorization work made grant and ownership changes emit a
/// <see cref="SecurityEvent"/> naming the real principal. Every other verb that changes a
/// <c>JOB</c>, <c>SCHEDULE</c>, or <c>NOTIFICATION</c> has to do the same, or the audit trail
/// answers "who was given access" while staying silent on "who disabled the nightly load" — which
/// is the question an incident actually starts from.</para>
///
/// <para>Asserted per verb rather than by counting events: a single "some events were emitted"
/// assertion passes while any one verb is missing, which is the exact failure this covers.</para>
/// </summary>
// "Portal", not "Integration": this needs no Docker, and the default gate excludes Integration —
// an audit-parity regression should fail the ordinary test run, not wait for a release lane.
[Trait("Category", "Portal")]
public sealed class OrchestratorAuditParityTests
{
    private const string JobName = "audited_job";

    [Fact]
    public async Task EveryJobMutationVerbEmitsASecurityEventNamingTheRealPrincipal()
    {
        using var factory = new OrchestratorWebFactory(requireFederatedIdentity: true);
        using var client = factory.CreateClient();
        var sink = new RecordingSecurityEventSink();
        using var sinkScope = new SecurityEventSinkScope(sink);

        // An administrator throughout: ownership reassignment is administrator-only, and using one
        // caller for every verb keeps the actor constant so a missing event cannot be mistaken for
        // an authorization failure.
        var admin = new OrchestratorCaller("user", "1", "Ada Lovelace", ["Admin", "OrchestratorManager"], []);

        using (var create = Request(HttpMethod.Post, "/api/scheduled-jobs", admin, new
        {
            name = JobName,
            scriptText = "SELECT 1 AS Value;",
            interval = 100,
            unit = "DAY"
        }))
            Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(create)).StatusCode);

        AssertAudited(sink, $"JOB:{JobName}", "CREATE_JOB");

        // Disable through the definition endpoint: it is both the editor and the on/off switch, so
        // it has to record both facts.
        await UpdateAsync(client, factory, admin, new { isEnabled = false });
        AssertAudited(sink, $"JOB:{JobName}", "ALTER_JOB");
        AssertAudited(sink, $"JOB:{JobName}", "DISABLE_JOB");

        await UpdateAsync(client, factory, admin, new { isEnabled = true });
        AssertAudited(sink, $"JOB:{JobName}", "ENABLE_JOB");

        using (var grant = Request(
            HttpMethod.Put, $"/api/authorization/JOB/{JobName}/USER/2", admin, new { permission = "READ" }))
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(grant)).StatusCode);
        AssertAudited(sink, $"JOB:{JobName}", "ACL_GRANT");

        using (var revoke = Request(HttpMethod.Delete, $"/api/authorization/JOB/{JobName}/USER/2", admin))
            Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(revoke)).StatusCode);
        AssertAudited(sink, $"JOB:{JobName}", "ACL_REVOKE");

        using (var owner = Request(HttpMethod.Put, $"/api/authorization/JOB/{JobName}/owner", admin,
                   new { principalKind = "USER", principalId = "9" }))
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(owner)).StatusCode);
        AssertAudited(sink, $"JOB:{JobName}", "OWNER_SET");

        using (var trigger = Request(HttpMethod.Post, $"/api/scheduled-jobs/{JobName}/trigger", admin))
            Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(trigger)).StatusCode);
        AssertAudited(sink, $"JOB:{JobName}", "TRIGGER_JOB");

        using (var drop = Request(HttpMethod.Delete, $"/api/scheduled-jobs/{JobName}", admin))
        {
            drop.Headers.TryAddWithoutValidation("If-Match", $"\"{await CurrentVersionAsync(factory)}\"");
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(drop)).StatusCode);
        }
        AssertAudited(sink, $"JOB:{JobName}", "DROP_JOB");

        // The point of the slice: the events name the human, not the service that carried the call.
        // Scoped to this job's own mutations — the triggered run emits on the scheduler's threads and
        // under its own identity, which is a different claim from the one under test here.
        var mutations = sink.Events
            .Where(e => e.Type == SecurityEventType.CatalogMutation && e.SanitizedTarget == $"JOB:{JobName}")
            .ToList();
        Assert.NotEmpty(mutations);
        foreach (var recorded in mutations)
        {
            Assert.Contains("Ada Lovelace", recorded.ActorIdentity, StringComparison.Ordinal);
            Assert.Equal("user:1", recorded.EffectiveIdentity);
        }
    }

    /// <summary>
    /// A variable-overridden trigger keeps its own <c>OverrideAttempt</c> event <em>and</em> emits the
    /// plain <c>TRIGGER_JOB</c> record. The two answer different questions — that the job ran off
    /// schedule, and that its inputs were changed on the way in — so one must not replace the other.
    /// </summary>
    [Fact]
    public async Task OverriddenTriggerRecordsBothTheRunAndTheOverride()
    {
        using var factory = new OrchestratorWebFactory(requireFederatedIdentity: true);
        using var client = factory.CreateClient();
        var sink = new RecordingSecurityEventSink();
        using var sinkScope = new SecurityEventSinkScope(sink);
        var admin = new OrchestratorCaller("user", "1", "Ada Lovelace", ["Admin", "OrchestratorManager"], []);

        using (var create = Request(HttpMethod.Post, "/api/scheduled-jobs", admin, new
        {
            name = JobName,
            scriptText = "SELECT 1 AS Value;",
            interval = 100,
            unit = "DAY"
        }))
            Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(create)).StatusCode);

        using (var trigger = Request(HttpMethod.Post, $"/api/scheduled-jobs/{JobName}/trigger", admin,
                   new { variables = new Dictionary<string, string> { ["scope"] = "all" } }))
            Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(trigger)).StatusCode);

        AssertAudited(sink, $"JOB:{JobName}", "TRIGGER_JOB");
        Assert.Contains(sink.Events, e =>
            e.Type == SecurityEventType.OverrideAttempt
            && e.SanitizedTarget == $"JOB:{JobName}"
            && e.Reason.Contains("scope", StringComparison.Ordinal));
    }

    private static async Task UpdateAsync(
        HttpClient client, OrchestratorWebFactory factory, OrchestratorCaller caller, object body)
    {
        using var update = Request(HttpMethod.Put, $"/api/scheduled-jobs/{JobName}", caller, body);
        update.Headers.TryAddWithoutValidation("If-Match", $"\"{await CurrentVersionAsync(factory)}\"");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(update)).StatusCode);
    }

    // Read back rather than counting saves: the optimistic-concurrency version advances on writes the
    // test does not make, and a hard-coded number would fail for a reason unrelated to auditing.
    private static async Task<long> CurrentVersionAsync(OrchestratorWebFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IJobHistoryStore>();
        return (await store.GetJobAsync(JobName))!.Version;
    }

    private static void AssertAudited(RecordingSecurityEventSink sink, string target, string action) =>
        Assert.True(
            sink.Events.Any(e =>
                e.Type == SecurityEventType.CatalogMutation
                && e.SanitizedTarget == target
                && e.Reason.StartsWith(action + ":", StringComparison.Ordinal)),
            $"No CatalogMutation event recorded '{action}' on '{target}'. Recorded: "
            + string.Join(" | ", sink.Events.Select(e => $"{e.SanitizedTarget} {e.Reason}")));

    private static HttpRequestMessage Request(
        HttpMethod method, string path, OrchestratorCaller caller, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Orchestrator-Key", "test-orch-key-12345");
        request.Headers.Add(
            OrchestratorIdentityAssertion.HeaderName,
            OrchestratorIdentityAssertion.Create(caller, OrchestratorWebFactory.IdentitySecret));
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }

    private sealed class SecurityEventSinkScope : IDisposable
    {
        private readonly ISecurityEventSink _previous;

        public SecurityEventSinkScope(ISecurityEventSink sink)
        {
            _previous = SecurityEventRuntime.Sink;
            SecurityEventRuntime.Sink = sink;
        }

        public void Dispose() => SecurityEventRuntime.Sink = _previous;
    }

    private sealed class RecordingSecurityEventSink : ISecurityEventSink
    {
        private readonly object _sync = new();
        private readonly List<SecurityEvent> _events = [];

        // Snapshotted under the lock: the scheduler emits from its own threads once a job is
        // triggered, so enumerating the live list races with it.
        public IReadOnlyList<SecurityEvent> Events
        {
            get { lock (_sync) return [.. _events]; }
        }

        public void Emit(SecurityEvent securityEvent)
        {
            lock (_sync) _events.Add(securityEvent);
        }
    }
}
