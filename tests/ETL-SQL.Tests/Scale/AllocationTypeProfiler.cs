using System.Collections.Concurrent;
using System.Diagnostics.Tracing;

namespace ETL_SQL.Tests.Scale;

internal sealed record AllocationTypeSample(string TypeName, long SampledBytes, int Ticks);

/// <summary>
/// Samples managed allocations by type via the runtime's <c>GCAllocationTick</c> event, which fires
/// roughly once per 100 KB allocated per type. The per-type byte totals are therefore a statistical
/// sample (proportions are representative; absolute bytes are approximately the true allocation for
/// hot types), not an exact census. For call-site attribution, capture stacks out-of-process with
/// <c>dotnet-trace collect -p &lt;pid&gt; --profile gc-verbose</c> and inspect in PerfView/speedscope —
/// in-process listeners do not receive stacks.
/// </summary>
internal sealed class AllocationTypeProfiler : EventListener
{
    private const string RuntimeEventSourceName = "Microsoft-Windows-DotNETRuntime";
    private const int GcAllocationTickEventId = 10;
    private const EventKeywords GcKeyword = (EventKeywords)0x1;

    // Base EventListener constructor can dispatch OnEventSourceCreated/OnEventWritten before this
    // type's field initializers run, so every field access below must tolerate that window.
    private ConcurrentDictionary<string, StatBox>? _byType;
    private volatile bool _collecting;
    private int _amountIndex = -1;
    private int _typeNameIndex = -1;

    private sealed class StatBox
    {
        public long Bytes;
        public int Ticks;
    }

    private ConcurrentDictionary<string, StatBox> ByType =>
        LazyInitializer.EnsureInitialized(ref _byType)!;

    /// <summary>Begin attributing allocation ticks (constructing the listener does not collect).</summary>
    public void Start()
    {
        ByType.Clear();
        _collecting = true;
    }

    public void Stop() => _collecting = false;

    public IReadOnlyList<AllocationTypeSample> Snapshot(int top = 25) =>
        ByType
            .Select(pair => new AllocationTypeSample(pair.Key,
                Interlocked.Read(ref pair.Value.Bytes), pair.Value.Ticks))
            .OrderByDescending(sample => sample.SampledBytes)
            .Take(top)
            .ToArray();

    public long TotalSampledBytes =>
        ByType.Sum(pair => Interlocked.Read(ref pair.Value.Bytes));

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        if (eventSource.Name == RuntimeEventSourceName)
            EnableEvents(eventSource, EventLevel.Verbose, GcKeyword);
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        if (!_collecting || eventData.EventId != GcAllocationTickEventId
            || eventData.Payload is null || eventData.PayloadNames is null)
            return;

        // Resolve payload indices once; AllocationTick versions keep these names stable.
        if (_amountIndex < 0 || _typeNameIndex < 0)
        {
            _amountIndex = IndexOf(eventData.PayloadNames, "AllocationAmount64");
            _typeNameIndex = IndexOf(eventData.PayloadNames, "TypeName");
            if (_amountIndex < 0 || _typeNameIndex < 0) return;
        }
        if (_amountIndex >= eventData.Payload.Count || _typeNameIndex >= eventData.Payload.Count)
            return;

        var amount = eventData.Payload[_amountIndex] switch
        {
            ulong u64 => unchecked((long)u64),
            long i64 => i64,
            uint u32 => u32,
            int i32 => i32,
            _ => 0L
        };
        if (amount <= 0) return;
        var typeName = eventData.Payload[_typeNameIndex] as string;
        if (string.IsNullOrEmpty(typeName)) return;

        var box = ByType.GetOrAdd(typeName, static _ => new StatBox());
        Interlocked.Add(ref box.Bytes, amount);
        Interlocked.Increment(ref box.Ticks);
    }

    private static int IndexOf(IReadOnlyList<string> names, string name)
    {
        for (var i = 0; i < names.Count; i++)
            if (string.Equals(names[i], name, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }
}
