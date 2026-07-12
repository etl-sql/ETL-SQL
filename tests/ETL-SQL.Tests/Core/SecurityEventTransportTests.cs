using System.Net;
using System.Text;
using System.Text.Json;
using ETL_SQL.Core.Governance;

namespace ETL_SQL.Tests.Core;

public sealed class SecurityEventTransportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"security_transport_{Guid.NewGuid():N}");
    private static readonly DateTimeOffset Now = new(2026, 7, 12, 19, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DrainOnce_UsesStableIdempotencyKeyAndDeliversAcknowledgedEvents()
    {
        var first = Guid.Parse("cb0866f8-1f1b-498f-b737-a584137f890f");
        var second = Guid.Parse("e957a62b-84d0-4e06-a284-f4118b4b609f");
        var outbox = CreateOutbox();
        outbox.Emit(Event(first));
        outbox.Emit(Event(second));
        var handler = new RecordingHandler(_ => Ack(first, second));
        var transport = CreateTransport(outbox, handler);

        var result = await transport.DrainOnceAsync(Now);

        Assert.Equal(new SecurityEventDeliveryResult(2, 2, 0), result);
        Assert.Equal(SecurityEventTransport.BatchId([first, second]), handler.IdempotencyKey);
        Assert.Equal("1", handler.SchemaVersion);
        Assert.Equal("corp-production", handler.TenantId);
        Assert.Equal("enrollment-1", handler.EnrollmentId);
        Assert.Equal("machine-1", handler.MachineId);
        Assert.Contains(first.ToString(), handler.RequestBody, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, outbox.GetHealth().PendingCount);
        Assert.Equal(Now, outbox.GetHealth().LastDeliveredUtc);
        var diagnostics = SecurityEventRuntime.GetDiagnostics();
        Assert.True(diagnostics.CollectorReachable);
        Assert.Equal(Now, diagnostics.LastCollectorAttemptUtc);
        Assert.Equal(Now, diagnostics.LastCollectorSuccessUtc);
    }

    [Fact]
    public async Task DrainOnce_RetriesOnlyUnacknowledgedEvents()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var outbox = CreateOutbox();
        outbox.Emit(Event(first));
        outbox.Emit(Event(second));
        var transport = CreateTransport(outbox, new RecordingHandler(_ => Ack(first)));

        var result = await transport.DrainOnceAsync(Now);

        Assert.Equal(1, result.Delivered);
        Assert.Equal(1, result.Failed);
        Assert.Empty(outbox.ClaimBatch(10, Now.AddSeconds(29), TimeSpan.FromMinutes(1)));
        var retry = Assert.Single(outbox.ClaimBatch(10, Now.AddSeconds(30), TimeSpan.FromMinutes(1)));
        Assert.Equal(second, retry.Event.EventId);
    }

    [Fact]
    public async Task DrainOnce_AcknowledgementLossRetainsSameEventAndIdempotencyKey()
    {
        var eventId = Guid.NewGuid();
        var outbox = CreateOutbox();
        outbox.Emit(Event(eventId));
        var responses = new Queue<HttpResponseMessage>(
        [
            new(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") },
            Ack(eventId)
        ]);
        var handler = new RecordingHandler(_ => responses.Dequeue());
        var transport = CreateTransport(outbox, handler);

        var first = await transport.DrainOnceAsync(Now);
        var second = await transport.DrainOnceAsync(Now.AddSeconds(30));

        Assert.Equal(1, first.Failed);
        Assert.Equal(1, second.Delivered);
        Assert.Equal(2, handler.IdempotencyKeys.Count);
        Assert.Equal(handler.IdempotencyKeys[0], handler.IdempotencyKeys[1]);
    }

    [Fact]
    public async Task DrainOnce_CollectorOutageSchedulesRetryWithSanitizedError()
    {
        var outbox = CreateOutbox();
        outbox.Emit(Event(Guid.NewGuid()));
        var handler = new RecordingHandler(_ => throw new HttpRequestException(
            "PASSWORD=collector-secret unavailable"));
        var transport = CreateTransport(outbox, handler);

        var result = await transport.DrainOnceAsync(Now);

        Assert.Equal(1, result.Failed);
        Assert.DoesNotContain("collector-secret", result.Error, StringComparison.Ordinal);
        Assert.Equal(1, outbox.GetHealth().PendingCount);
        var diagnostics = SecurityEventRuntime.GetDiagnostics();
        Assert.False(diagnostics.CollectorReachable);
        Assert.Equal(Now, diagnostics.LastCollectorFailureUtc);
        Assert.DoesNotContain("collector-secret", diagnostics.LastCollectorError,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task DrainOnce_RecoversAfterCollectorOutageWithoutLosingEvent()
    {
        var eventId = Guid.NewGuid();
        var outbox = CreateOutbox();
        outbox.Emit(Event(eventId));
        var attempts = 0;
        var handler = new RecordingHandler(_ => ++attempts == 1
            ? throw new HttpRequestException("simulated outage")
            : Ack(eventId));
        var transport = CreateTransport(outbox, handler);

        var failed = await transport.DrainOnceAsync(Now);
        var recovered = await transport.DrainOnceAsync(Now.AddSeconds(30));

        Assert.Equal(1, failed.Failed);
        Assert.Equal(1, recovered.Delivered);
        Assert.Equal(2, handler.IdempotencyKeys.Count);
        Assert.Equal(handler.IdempotencyKeys[0], handler.IdempotencyKeys[1]);
        Assert.Equal(0, outbox.GetHealth().PendingCount);
        Assert.True(SecurityEventRuntime.GetDiagnostics().CollectorReachable);
    }

    [Fact]
    public async Task DrainOnce_ForwardsOnlyEventsMeetingSignedSeverityThreshold()
    {
        var information = Guid.NewGuid();
        var warning = Guid.NewGuid();
        var outbox = CreateOutbox();
        outbox.Emit(Event(information, SecurityEventSeverity.Information));
        outbox.Emit(Event(warning, SecurityEventSeverity.Warning));
        var handler = new RecordingHandler(_ => Ack(warning));
        var transport = CreateTransport(outbox, handler);

        var result = await transport.DrainOnceAsync(Now);

        Assert.Equal(new SecurityEventDeliveryResult(1, 1, 0), result);
        Assert.DoesNotContain(information.ToString(), handler.RequestBody,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(warning.ToString(), handler.RequestBody,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, outbox.GetHealth().PendingCount);
        Assert.Equal(1, outbox.GetHealth().FilteredCount);
        Assert.Equal(2, outbox.PruneDelivered(Now.AddSeconds(1)));
    }

    [Fact]
    public void Constructor_RejectsNonHttpsOrCredentialBearingCollector()
    {
        var outbox = CreateOutbox();
        Assert.Throws<ArgumentException>(() => CreateTransport(outbox, new RecordingHandler(_ => Ack()),
            "http://collector.example.test/events"));
        Assert.Throws<ArgumentException>(() => CreateTransport(outbox, new RecordingHandler(_ => Ack()),
            "https://user:password@collector.example.test/events"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private SecurityEventOutbox CreateOutbox() => new(new SecurityEventOutboxOptions
    {
        DatabasePath = Path.Combine(_root, "events.db"),
        InitialRetryDelay = TimeSpan.FromSeconds(30),
        MaxRetryDelay = TimeSpan.FromMinutes(5)
    }, jitter: () => 0.5);

    private static SecurityEventTransport CreateTransport(
        SecurityEventOutbox outbox,
        HttpMessageHandler handler,
        string endpoint = "https://collector.example.test/security-events") =>
        new(outbox, new HttpClient(handler), new SecurityEventTransportOptions
        {
            CollectorEndpoint = new Uri(endpoint),
            TenantId = "corp-production",
            EnrollmentId = "enrollment-1",
            MachineId = "machine-1",
            BatchSize = 100,
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

    private static HttpResponseMessage Ack(params Guid[] eventIds) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new
        {
            acknowledgedEventIds = eventIds
        }), Encoding.UTF8, "application/json")
    };

    private static SecurityEvent Event(
        Guid id,
        SecurityEventSeverity severity = SecurityEventSeverity.Error) => SecurityEventContract.Create(
        severity, SecurityEventType.OperationDenied,
        "user", "service", "<target>", SecurityEventDecision.Denied, "Denied.", eventId: id);

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public string? IdempotencyKey => IdempotencyKeys.LastOrDefault();
        public List<string> IdempotencyKeys { get; } = [];
        public string? SchemaVersion { get; private set; }
        public string? TenantId { get; private set; }
        public string? EnrollmentId { get; private set; }
        public string? MachineId { get; private set; }
        public string RequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            IdempotencyKeys.Add(request.Headers.GetValues("Idempotency-Key").Single());
            SchemaVersion = request.Headers.GetValues("X-ETL-SQL-Security-Event-Schema").Single();
            TenantId = request.Headers.GetValues(EnterprisePolicyTransport.TenantHeader).Single();
            EnrollmentId = request.Headers.GetValues(EnterprisePolicyTransport.EnrollmentHeader).Single();
            MachineId = request.Headers.GetValues(EnterprisePolicyTransport.MachineHeader).Single();
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return response(request);
        }
    }
}
