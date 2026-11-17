using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Execution;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ETL_SQL.Orchestrator.Execution
{
    /// <summary>
    /// Centralized coordinator for global engine resources (RAM, Streaming Cursors, and synchronous
    /// operator memory grants). Consolidates <see cref="BufferManagerOptions"/> limits, hysteresis,
    /// and system-memory floor with the <see cref="IMemoryGrantArbiter"/> contract so all execution
    /// paths reserve against one shared pool.
    /// </summary>
    public class BufferManager : IBufferManager, IMemoryGrantArbiter, IDisposable
    {
        private readonly ILogger<BufferManager> _logger;
        private readonly BufferManagerOptions _options;
        private readonly ISystemResources _systemResources;
        private readonly SemaphoreSlim _cursorSemaphore;
        private readonly long _effectiveMaxMemoryBytes;

        private long _currentMemoryBytes;
        private int _isMemoryExhausted; // 0=false, 1=true
        private int _isSystemMemoryExhausted; // 0=false, 1=true
        private long _lastSystemCheckTicks;
        private long _cachedSystemAvailableBytes;
        private readonly object _memLock = new();
        private readonly ConcurrentQueue<MemoryRequest> _memoryQueue = new();
        private readonly ConcurrentDictionary<ISpillable, byte> _spillables = new();

        private int _activeCursors;
        private int _queuedCursors;

        internal int ActiveCursors => Volatile.Read(ref _activeCursors);
        internal int QueuedCursors => Volatile.Read(ref _queuedCursors);
        internal int AvailableCursorSlots => _cursorSemaphore.CurrentCount;
        internal bool IsMemoryExhausted => Volatile.Read(ref _isMemoryExhausted) == 1;
        internal bool IsSystemMemoryExhausted => Volatile.Read(ref _isSystemMemoryExhausted) == 1;
        internal TimeSpan? SystemCheckIntervalOverride { get; set; }

        /// <summary>Total global budget in bytes enforced across both async reservations and operator leases.</summary>
        public long TotalBudgetBytes => _effectiveMaxMemoryBytes;

        /// <summary>Currently reserved bytes across active async reservations and synchronous operator leases.</summary>
        public long ReservedBytes => Interlocked.Read(ref _currentMemoryBytes);

        /// <summary>Tracks active reservations per session to prevent 'Zombie References'.</summary>
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, IResourceReservation>> _sessionReservations = new();

        private interface IResourceReservation : IDisposable
        {
            Guid Id { get; }
            string SessionId { get; }
            WeakReference? Owner { get; }
        }

        private readonly Timer? _zombieSweepTimer;

        [ActivatorUtilitiesConstructor]
        public BufferManager(
            IOptions<BufferManagerOptions> options,
            ILogger<BufferManager> logger,
            ISystemResources systemResources,
            IConfiguration? configuration = null)
        {
            _options = options.Value;
            _logger = logger;
            _systemResources = systemResources;

            int effectiveMaxMB;
            if (configuration != null)
            {
                var engineGrant = configuration.GetValue<int?>("Engine:TotalMemoryGrantMB");
                effectiveMaxMB = MemoryCoordinatorPolicy.ResolveEffectiveGlobalMemoryMB(
                    engineGrant,
                    _options.MaxGlobalMemoryMB,
                    systemResources.GetTotalMemoryBytes());
            }
            else
            {
                effectiveMaxMB = Math.Max(0, _options.MaxGlobalMemoryMB);
            }

            _effectiveMaxMemoryBytes = (long)effectiveMaxMB * 1024L * 1024L;

            var maxCursors = _options.MaxStreamingCursors > 0 ? _options.MaxStreamingCursors : 50;
            _cursorSemaphore = new SemaphoreSlim(maxCursors, maxCursors);

            _logger.LogInformation(
                "BufferManager initialized: MaxMemory={MaxMem}MB (Configured={ConfiguredMax}MB), Hysteresis={Hyst}MB, SystemFloor={Floor}MB, MaxCursors={MaxCursors}, Timeout={Timeout}s",
                effectiveMaxMB, _options.MaxGlobalMemoryMB, _options.HysteresisMemoryMB, _options.SystemMemoryFloorMB, maxCursors, _options.ResourceWaitTimeoutSeconds);

            _zombieSweepTimer = new Timer(PruneZombies, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        #region Memory Management & IMemoryGrantArbiter

        public IMemoryGrantLease AcquireLease() => AcquireMemoryLease(sessionId: null, owner: null);

        public IMemoryGrantLease AcquireMemoryLease(string? sessionId = null, object? owner = null)
        {
            var lease = new ArbiterLease(this, sessionId ?? string.Empty, owner);
            if (!string.IsNullOrEmpty(sessionId) || owner != null)
                RegisterReservation(lease);
            return lease;
        }

        public Task<IDisposable> ReserveMemoryAsync(string sessionId, long bytes, bool isOverride = false, object? owner = null)
            => ReserveMemoryAsync(sessionId, bytes, CancellationToken.None, isOverride, owner);

        public async Task<IDisposable> ReserveMemoryAsync(string sessionId, long bytes, CancellationToken cancellationToken, bool isOverride = false, object? owner = null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (isOverride)
            {
                _logger.LogWarning("[POLICY_OVERRIDE] Session {SessionId} bypasses global limit to reserve {Bytes} bytes. User responsibility assumed.", sessionId, bytes);
                Interlocked.Add(ref _currentMemoryBytes, bytes);
                return RegisterReservation(new MemoryRelease(this, sessionId, bytes, ownerRef: owner));
            }

            long maxBytes = _effectiveMaxMemoryBytes;
            MemoryRequest? request = null;
            TaskCompletionSource<bool>? tcs = null;

            lock (_memLock)
            {
                long spillAmount = 0;
                if (_memoryQueue.IsEmpty && CanAcquireMemoryLocked(bytes, maxBytes, out spillAmount))
                {
                    Interlocked.Add(ref _currentMemoryBytes, bytes);
                    return RegisterReservation(new MemoryRelease(this, sessionId, bytes, ownerRef: owner));
                }
                else if (_memoryQueue.IsEmpty && spillAmount > 0)
                {
                    _ = Task.Run(() => TriggerSpillsUnderPressureAsync(spillAmount));
                }

                tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                request = new MemoryRequest(bytes, tcs);
                _memoryQueue.Enqueue(request);
            }

            _logger.LogInformation("Session {SessionId} queued for {Bytes} bytes of memory.", sessionId, bytes);

            using var cancellationRegistration = cancellationToken.Register(() =>
            {
                lock (_memLock)
                {
                    if (request.TryTransitionToAbandoned())
                    {
                        request.Tcs.TrySetCanceled(cancellationToken);
                        ProcessMemoryQueueLocked();
                    }
                }
            });

            try
            {
                if (await WaitWithFeedback(sessionId, "Memory", tcs.Task, cancellationToken, pollMemoryRecovery: true))
                {
                    return RegisterReservation(new MemoryRelease(this, sessionId, bytes, ownerRef: owner));
                }

                AbandonOrReleaseConcurrentMemoryGrant(request);
            }
            catch
            {
                AbandonOrReleaseConcurrentMemoryGrant(request);
                throw;
            }

            throw new TimeoutException($"Resource request for memory ({bytes} bytes) timed out after {_options.ResourceWaitTimeoutSeconds} seconds.");
        }

        private void AbandonOrReleaseConcurrentMemoryGrant(MemoryRequest request)
        {
            lock (_memLock)
            {
                if (request.TryTransitionToAbandoned())
                {
                    request.Tcs.TrySetCanceled();
                    ProcessMemoryQueueLocked();
                    return;
                }

                if (request.IsGranted)
                    ReleaseMemoryLocked(request.Bytes);
            }
        }

        private bool CheckSystemMemoryFloorLocked()
        {
            long floorBytes = (long)_options.SystemMemoryFloorMB * 1024 * 1024;
            if (floorBytes <= 0)
            {
                if (Interlocked.CompareExchange(ref _isSystemMemoryExhausted, 0, 1) == 1)
                    _logger.LogInformation("[SYSTEM_MEMORY_PRESSURE_RELIEF] System memory floor disabled.");
                return true;
            }

            long now = DateTime.UtcNow.Ticks;
            long lastCheck = Interlocked.Read(ref _lastSystemCheckTicks);
            long intervalTicks = SystemCheckIntervalOverride?.Ticks
                ?? (Volatile.Read(ref _isSystemMemoryExhausted) == 1
                    ? TimeSpan.FromMilliseconds(10).Ticks
                    : TimeSpan.FromMilliseconds(20).Ticks);

            long systemAvailable;
            if (intervalTicks <= 0 || now - lastCheck >= intervalTicks)
            {
                systemAvailable = _systemResources.GetAvailableMemoryBytes();
                Interlocked.Exchange(ref _cachedSystemAvailableBytes, systemAvailable);
                Interlocked.Exchange(ref _lastSystemCheckTicks, now);
            }
            else
            {
                systemAvailable = Interlocked.Read(ref _cachedSystemAvailableBytes);
            }

            if (systemAvailable < floorBytes)
            {
                if (Interlocked.CompareExchange(ref _isSystemMemoryExhausted, 1, 0) == 0)
                {
                    _logger.LogWarning(
                        "[SYSTEM_MEMORY_PRESSURE] System available RAM ({Current} MB) is below safe floor ({Floor} MB). Suspending requests.",
                        systemAvailable / 1024 / 1024, _options.SystemMemoryFloorMB);
                }
                return false;
            }

            if (Interlocked.CompareExchange(ref _isSystemMemoryExhausted, 0, 1) == 1)
            {
                _logger.LogInformation("[SYSTEM_MEMORY_PRESSURE_RELIEF] System memory recovered above safe floor. Resuming requests.");
            }

            return true;
        }

        private long ComputeSafeLevel(long maxBytes)
        {
            long hysteresisBytes = (long)Math.Max(0, _options.HysteresisMemoryMB) * 1024 * 1024;
            long safeLevel = maxBytes - hysteresisBytes;
            if (safeLevel < maxBytes / 2) safeLevel = maxBytes / 2;
            return safeLevel;
        }

        private bool CanAcquireMemoryLocked(long bytes, long maxBytes, out long spillTriggerBytes)
        {
            spillTriggerBytes = 0;

            if (!CheckSystemMemoryFloorLocked())
                return false;

            if (maxBytes <= 0)
                return true;

            long current = Interlocked.Read(ref _currentMemoryBytes);

            if (Volatile.Read(ref _isMemoryExhausted) == 1)
            {
                long safeLevel = ComputeSafeLevel(maxBytes);
                if (current + bytes > safeLevel)
                    return false;

                _logger.LogInformation("[HYSTERESIS] Safe memory level reached. Resuming queue processing.");
                Interlocked.Exchange(ref _isMemoryExhausted, 0);
            }

            if (current + bytes <= maxBytes)
                return true;

            if (Interlocked.CompareExchange(ref _isMemoryExhausted, 1, 0) == 0)
            {
                _logger.LogWarning(
                    "[RESOURCE_EXHAUSTION] Global memory limit reached ({Max}MB). Suspending requests until hysteresis cooldown.",
                    _effectiveMaxMemoryBytes / (1024 * 1024));
                spillTriggerBytes = bytes;
            }

            return false;
        }

        private bool TryReserveDeltaForLease(long deltaBytes)
        {
            if (deltaBytes <= 0) return true;

            long spillBytes = 0;
            bool admitted;
            lock (_memLock)
            {
                admitted = CanAcquireMemoryLocked(deltaBytes, _effectiveMaxMemoryBytes, out spillBytes);
                if (admitted)
                    Interlocked.Add(ref _currentMemoryBytes, deltaBytes);
            }

            if (!admitted && spillBytes > 0)
                _ = Task.Run(() => TriggerSpillsUnderPressureAsync(spillBytes));

            return admitted;
        }

        public void RegisterSpillable(ISpillable spillable) => _spillables.TryAdd(spillable, 0);

        public void UnregisterSpillable(ISpillable spillable) => _spillables.TryRemove(spillable, out _);

        public async Task<long> TriggerSpillsUnderPressureAsync(long requiredBytes)
        {
            var candidates = _spillables.Keys.ToList()
                .OrderByDescending(s => s.MemoryUsageBytes)
                .ToList();

            long reclaimed = 0;
            foreach (var spillable in candidates)
            {
                if (reclaimed >= requiredBytes) break;

                long sizeBefore = spillable.MemoryUsageBytes;
                if (sizeBefore <= 0) continue;

                try
                {
                    _logger.LogWarning(
                        "[MEMORY_PRESSURE_RELIEF] Proactively spilling {Token} to disk ({Size} MB) to alleviate global memory pressure.",
                        spillable.SpillToken, sizeBefore / 1024 / 1024);

                    if (await spillable.SpillAsync())
                        reclaimed += sizeBefore;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during proactive spill of {Token}", spillable.SpillToken);
                }
            }

            if (reclaimed > 0)
            {
                _logger.LogInformation(
                    "[MEMORY_PRESSURE_RELIEF] Proactive spill complete. Reclaimed {Size} MB from {Count} sources.",
                    reclaimed / 1024 / 1024, candidates.Count(c => c.MemoryUsageBytes == 0));
            }

            return reclaimed;
        }

        private void ReleaseMemory(long bytes)
        {
            if (bytes <= 0) return;
            lock (_memLock)
            {
                ReleaseMemoryLocked(bytes);
            }
        }

        private void ReleaseMemoryLocked(long bytes)
        {
            var newTotal = Interlocked.Add(ref _currentMemoryBytes, -bytes);
            if (newTotal < 0)
                Interlocked.Exchange(ref _currentMemoryBytes, 0);

            if (Interlocked.Read(ref _currentMemoryBytes) == 0)
            {
                if (Interlocked.CompareExchange(ref _isMemoryExhausted, 0, 1) == 1)
                    _logger.LogInformation("[HYSTERESIS] All reserved memory released; exhaustion cleared.");
            }

            if (!_memoryQueue.IsEmpty)
                ProcessMemoryQueueLocked();
        }

        private void ProcessMemoryQueueLocked()
        {
            long maxBytes = _effectiveMaxMemoryBytes;
            while (_memoryQueue.TryPeek(out var next))
            {
                if (next.IsAbandoned)
                {
                    _memoryQueue.TryDequeue(out _);
                    continue;
                }

                if (CanAcquireMemoryLocked(next.Bytes, maxBytes, out _))
                {
                    if (_memoryQueue.TryDequeue(out _) && next.TryTransitionToGranted())
                    {
                        Interlocked.Add(ref _currentMemoryBytes, next.Bytes);
                        next.Tcs.TrySetResult(true);
                    }
                }
                else
                {
                    break;
                }
            }
        }

        private IDisposable RegisterReservation(IResourceReservation reservation)
        {
            var dict = _sessionReservations.GetOrAdd(reservation.SessionId, _ => new ConcurrentDictionary<Guid, IResourceReservation>());
            dict.TryAdd(reservation.Id, reservation);
            return reservation;
        }

        private void UnregisterReservation(IResourceReservation reservation)
        {
            if (string.IsNullOrEmpty(reservation.SessionId) && reservation.Owner == null) return;
            if (_sessionReservations.TryGetValue(reservation.SessionId, out var dict))
            {
                dict.TryRemove(reservation.Id, out _);
                if (dict.IsEmpty) _sessionReservations.TryRemove(reservation.SessionId, out _);
            }
        }

        public void ReleaseAllForSession(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId)) return;

            if (_sessionReservations.TryRemove(sessionId, out var dict))
            {
                int count = dict.Count;
                if (count > 0)
                {
                    _logger.LogWarning("[ZOMBIE_PROTECTION] Session {SessionId} finished with {Count} unreleased resource reservations. Forcefully reclaiming...", sessionId, count);
                    foreach (var reservation in dict.Values)
                    {
                        try { reservation.Dispose(); }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error during forced reclamation of reservation {Id} for session {SessionId}", reservation.Id, sessionId);
                        }
                    }
                }
            }
        }

        private sealed class ArbiterLease(BufferManager owner, string sessionId, object? ownerRef) : IMemoryGrantLease, IResourceReservation
        {
            private readonly object _leaseGate = new();
            private long _reported;
            private bool _disposed;

            public Guid Id { get; } = Guid.NewGuid();
            public string SessionId { get; } = sessionId;
            public WeakReference? Owner { get; } = ownerRef != null ? new WeakReference(ownerRef) : null;

            public bool RegisterAndCheckSpill(long bytes)
            {
                lock (_leaseGate)
                {
                    if (_disposed) return true;
                    if (bytes <= _reported) return false;

                    long delta = bytes - _reported;
                    if (!owner.TryReserveDeltaForLease(delta))
                        return true;

                    _reported = bytes;
                    return false;
                }
            }

            public void Dispose()
            {
                long toRelease;
                lock (_leaseGate)
                {
                    if (_disposed) return;
                    _disposed = true;
                    toRelease = _reported;
                    _reported = 0;
                }

                owner.UnregisterReservation(this);
                if (toRelease > 0)
                    owner.ReleaseMemory(toRelease);
            }
        }

        private sealed class MemoryRequest(long bytes, TaskCompletionSource<bool> tcs)
        {
            private const int StatePending = 0;
            private const int StateGranted = 1;
            private const int StateAbandoned = 2;

            private int _state = StatePending;

            public long Bytes { get; } = bytes;
            public TaskCompletionSource<bool> Tcs { get; } = tcs;

            public bool IsGranted => Volatile.Read(ref _state) == StateGranted;
            public bool IsAbandoned => Volatile.Read(ref _state) == StateAbandoned;
            public bool TryTransitionToGranted() => Interlocked.CompareExchange(ref _state, StateGranted, StatePending) == StatePending;
            public bool TryTransitionToAbandoned() => Interlocked.CompareExchange(ref _state, StateAbandoned, StatePending) == StatePending;
        }

        private class MemoryRelease : IResourceReservation
        {
            private readonly BufferManager _owner;
            private readonly long _bytes;
            private int _disposed;

            public Guid Id { get; } = Guid.NewGuid();
            public string SessionId { get; }
            public WeakReference? Owner { get; }

            public MemoryRelease(BufferManager owner, string sessionId, long bytes, object? ownerRef = null)
            {
                _owner = owner;
                SessionId = sessionId;
                _bytes = bytes;
                if (ownerRef != null) Owner = new WeakReference(ownerRef);
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
                _owner.UnregisterReservation(this);
                _owner.ReleaseMemory(_bytes);
            }
        }

        #endregion

        #region Cursor Management

        public Task<IDisposable> AcquireCursorAsync(string sessionId, bool isOverride = false, object? owner = null)
            => AcquireCursorAsync(sessionId, CancellationToken.None, isOverride, owner);

        public async Task<IDisposable> AcquireCursorAsync(string sessionId, CancellationToken cancellationToken, bool isOverride = false, object? owner = null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (isOverride)
            {
                _logger.LogWarning("[POLICY_OVERRIDE] Session {SessionId} bypasses cursor limit. Total active cursors may exceed {_Max}.", sessionId, _options.MaxStreamingCursors);
                Interlocked.Increment(ref _activeCursors);
                return RegisterReservation(new CursorRelease(this, sessionId, owner));
            }

            Interlocked.Increment(ref _queuedCursors);
            try
            {
                using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var waitTask = _cursorSemaphore.WaitAsync(waitCts.Token);

                try
                {
                    if (await WaitWithFeedback(sessionId, "Streaming Cursor", waitTask, cancellationToken, pollMemoryRecovery: false))
                    {
                        Interlocked.Increment(ref _activeCursors);
                        return RegisterReservation(new CursorRelease(this, sessionId, owner));
                    }
                }
                catch
                {
                    await CancelAndReleaseOrphanedCursorWaitAsync(waitCts, waitTask);
                    throw;
                }

                await CancelAndReleaseOrphanedCursorWaitAsync(waitCts, waitTask);
            }
            finally
            {
                Interlocked.Decrement(ref _queuedCursors);
            }

            throw new TimeoutException($"Resource request for streaming cursor timed out after {_options.ResourceWaitTimeoutSeconds} seconds.");
        }

        private async Task CancelAndReleaseOrphanedCursorWaitAsync(CancellationTokenSource waitCts, Task waitTask)
        {
            waitCts.Cancel();
            try
            {
                await waitTask;
                _cursorSemaphore.Release();
            }
            catch (OperationCanceledException) when (waitCts.IsCancellationRequested)
            {
            }
        }

        private void ReleaseCursor()
        {
            Interlocked.Decrement(ref _activeCursors);
            _cursorSemaphore.Release();
        }

        private class CursorRelease : IResourceReservation
        {
            private readonly BufferManager _owner;
            private int _disposed;

            public Guid Id { get; } = Guid.NewGuid();
            public string SessionId { get; }
            public WeakReference? Owner { get; }

            public CursorRelease(BufferManager owner, string sessionId, object? ownerRef = null)
            {
                _owner = owner;
                SessionId = sessionId;
                if (ownerRef != null) Owner = new WeakReference(ownerRef);
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
                _owner.UnregisterReservation(this);
                _owner.ReleaseCursor();
            }
        }

        #endregion

        internal Task<bool> WaitWithFeedbackForTestAsync(string sessionId, string resourceName, Task waitTask, CancellationToken cancellationToken = default)
            => WaitWithFeedback(sessionId, resourceName, waitTask, cancellationToken, pollMemoryRecovery: false);

        private async Task<bool> WaitWithFeedback(
            string sessionId,
            string resourceName,
            Task waitTask,
            CancellationToken cancellationToken = default,
            bool pollMemoryRecovery = false)
        {
            long timeoutMs = _options.ResourceWaitTimeoutSeconds > 0
                ? (long)_options.ResourceWaitTimeoutSeconds * 1000L
                : long.MaxValue;
            long elapsedMs = 0;
            long nextLogMs = 1000;

            while (elapsedMs < timeoutMs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (pollMemoryRecovery && Volatile.Read(ref _isSystemMemoryExhausted) == 1)
                {
                    lock (_memLock)
                    {
                        ProcessMemoryQueueLocked();
                    }
                    if (waitTask.IsCompleted)
                    {
                        await waitTask;
                        return true;
                    }
                }

                long stepLimit = pollMemoryRecovery && Volatile.Read(ref _isSystemMemoryExhausted) == 1 ? 100L : 1000L;
                int nextWaitMs = (int)Math.Min(stepLimit, timeoutMs - elapsedMs);
                var delayTask = Task.Delay(nextWaitMs, cancellationToken);
                var completed = await Task.WhenAny(waitTask, delayTask);

                if (completed == waitTask)
                {
                    await waitTask;
                    return true;
                }

                await delayTask;
                elapsedMs += nextWaitMs;

                if (elapsedMs >= nextLogMs)
                {
                    nextLogMs += 1000;
                    int remainingMinutes = timeoutMs == long.MaxValue ? 0 : (int)((timeoutMs - elapsedMs) / 60000L);
                    _logger.LogInformation("Session {SessionId} still waiting for {Resource}... ({Remaining} min remaining)",
                        sessionId, resourceName, remainingMinutes > 0 ? remainingMinutes : 0);
                }
            }

            return false;
        }

        public void Dispose()
        {
            _zombieSweepTimer?.Dispose();
            _cursorSemaphore.Dispose();
        }

        internal void PruneZombies(object? state = null)
        {
            try
            {
                var sessions = _sessionReservations.Keys.ToArray();
                foreach (var sessionId in sessions)
                {
                    if (_sessionReservations.TryGetValue(sessionId, out var dict))
                    {
                        var toReclaim = new List<IResourceReservation>();
                        foreach (var reservation in dict.Values.ToArray())
                        {
                            if (reservation.Owner != null && !reservation.Owner.IsAlive)
                                toReclaim.Add(reservation);
                        }

                        if (toReclaim.Count > 0)
                        {
                            _logger.LogWarning("[ZOMBIE_RECLAMATION] Detected {Count} orphaned reservations for session {SessionId} whose owner has been GCed. Reclaiming...", toReclaim.Count, sessionId);
                            foreach (var res in toReclaim)
                            {
                                try { res.Dispose(); } catch { }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[ZOMBIE_RECLAMATION] Error during zombie reservation pruning.");
            }
        }
    }
}
