using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Execution;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ETL_SQL.Orchestrator.Execution
{
    /// <summary>
    /// Centralized coordinator for global engine resources (RAM, Database Cursors).
    /// Implements FIFO queuing with timeouts and periodic visual feedback.
    /// </summary>
    public class BufferManager : IBufferManager, IDisposable
    {
        private readonly ILogger<BufferManager> _logger;
        private readonly BufferManagerOptions _options;
        private readonly ISystemResources _systemResources;
        private readonly SemaphoreSlim _cursorSemaphore;

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

        /// <summary>Tracks active reservations per session to prevent 'Zombie References'.</summary>
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, IResourceReservation>> _sessionReservations = new();

        private interface IResourceReservation : IDisposable
        {
            Guid Id { get; }
            string SessionId { get; }
            WeakReference? Owner { get; }
        }

        private readonly Timer? _zombieSweepTimer;

        public BufferManager(IOptions<BufferManagerOptions> options, ILogger<BufferManager> logger, ISystemResources systemResources)
        {
            _options = options.Value;
            _logger = logger;
            _systemResources = systemResources;

            var maxCursors = _options.MaxStreamingCursors > 0 ? _options.MaxStreamingCursors : 50;
            _cursorSemaphore = new SemaphoreSlim(maxCursors, maxCursors);

            _logger.LogInformation("BufferManager initialized: MaxMemory={MaxMem}MB, SystemFloor={Floor}MB, MaxCursors={MaxCursors}, Timeout={Timeout}s",
                _options.MaxGlobalMemoryMB, _options.SystemMemoryFloorMB, maxCursors, _options.ResourceWaitTimeoutSeconds);

            // Start the zombie protection sweep
            _zombieSweepTimer = new Timer(PruneZombies, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        #region Memory Management

        public async Task<IDisposable> ReserveMemoryAsync(string sessionId, long bytes, bool isOverride = false, object? owner = null)
        {
            if (isOverride)
            {
                _logger.LogWarning("[POLICY_OVERRIDE] Session {SessionId} bypasses global limit to reserve {Bytes} bytes. User responsibility assumed.", sessionId, bytes);
                Interlocked.Add(ref _currentMemoryBytes, bytes);
                return RegisterReservation(new MemoryRelease(this, sessionId, bytes, ownerRef: owner));
            }

            long maxBytes = (long)_options.MaxGlobalMemoryMB * 1024 * 1024;

            // FAST PATH: If no one is waiting and we have space, grab it without a lock
            if (_memoryQueue.IsEmpty && CanAcquireMemoryInternal(bytes, maxBytes))
            {
                // Optimistically increment
                var newTotal = Interlocked.Add(ref _currentMemoryBytes, bytes);

                // Re-verify after increment to ensure we didn't just cross the line
                if (newTotal <= maxBytes || maxBytes <= 0)
                {
                    return RegisterReservation(new MemoryRelease(this, sessionId, bytes, ownerRef: owner));
                }

                // Rollback if we exceeded while another thread was doing the same
                Interlocked.Add(ref _currentMemoryBytes, -bytes);
            }

            // SLOW PATH: Use queueing logic
            TaskCompletionSource<bool> tcs = null!;

            lock (_memLock)
            {
                // One more check inside the lock just in case someone released while we were entering
                if (_memoryQueue.IsEmpty && CanAcquireMemoryInternal(bytes, maxBytes))
                {
                    Interlocked.Add(ref _currentMemoryBytes, bytes);
                    return RegisterReservation(new MemoryRelease(this, sessionId, bytes, ownerRef: owner));
                }

                // Queue the request
                tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var request = new MemoryRequest(bytes, tcs);
                _memoryQueue.Enqueue(request);
            }

            _logger.LogInformation("Session {SessionId} queued for {Bytes} bytes of memory.", sessionId, bytes);

            if (await WaitWithFeedback(sessionId, "Memory", tcs.Task))
            {
                // Task was completed (memory freed and counter already incremented by ProcessMemoryQueue)
                return RegisterReservation(new MemoryRelease(this, sessionId, bytes, ownerRef: owner));
            }

            throw new TimeoutException($"Resource request for memory ({bytes} bytes) timed out after {_options.ResourceWaitTimeoutSeconds} seconds.");
        }

        private bool CanAcquireMemoryInternal(long bytes, long maxBytes)
        {
            // 1. System Memory Floor Check (Throttled)
            long now = DateTime.UtcNow.Ticks;
            long lastCheck = Interlocked.Read(ref _lastSystemCheckTicks);
            long systemAvailable;

            if (now - lastCheck > TimeSpan.FromMilliseconds(500).Ticks)
            {
                systemAvailable = _systemResources.GetAvailableMemoryBytes();
                Interlocked.Exchange(ref _cachedSystemAvailableBytes, systemAvailable);
                Interlocked.Exchange(ref _lastSystemCheckTicks, now);
            }
            else
            {
                systemAvailable = Interlocked.Read(ref _cachedSystemAvailableBytes);
            }

            long floorBytes = (long)_options.SystemMemoryFloorMB * 1024 * 1024;

            if (systemAvailable < floorBytes)
            {
                if (Interlocked.CompareExchange(ref _isSystemMemoryExhausted, 1, 0) == 0)
                {
                    _logger.LogWarning("[SYSTEM_MEMORY_PRESSURE] System available RAM ({Current} MB) is below safe floor ({Floor} MB). Suspending requests.",
                        systemAvailable / 1024 / 1024, _options.SystemMemoryFloorMB);
                }
                return false;
            }
            else if (Interlocked.CompareExchange(ref _isSystemMemoryExhausted, 0, 1) == 1)
            {
                _logger.LogInformation("[SYSTEM_MEMORY_PRESSURE_RELIEF] System memory recovered above safe floor. Resuming requests.");
            }

            // 2. Engine Global Limit Check
            if (maxBytes <= 0) return true;

            // If currently exhausted, we must wait until hysteresis threshold is reached
            if (Volatile.Read(ref _isMemoryExhausted) == 1)
            {
                long hysteresisBytes = (long)_options.HysteresisMemoryMB * 1024 * 1024;
                long safeLevel = maxBytes - hysteresisBytes;

                // Sanity check: Ensure safeLevel is at least 50% of capacity if hysteresis is too aggressive
                if (safeLevel < maxBytes / 2) safeLevel = maxBytes / 2;

                if (Interlocked.Read(ref _currentMemoryBytes) + bytes > safeLevel) return false;

                _logger.LogInformation("[HYSTERESIS] Safe memory level reached. Resuming queue processing.");
                Interlocked.Exchange(ref _isMemoryExhausted, 0);
            }

            if (Interlocked.Read(ref _currentMemoryBytes) + bytes <= maxBytes)
            {
                return true;
            }

            if (Interlocked.CompareExchange(ref _isMemoryExhausted, 1, 0) == 0)
            {
                _logger.LogWarning("[RESOURCE_EXHAUSTION] Global memory limit reached ({Max}MB). Suspending requests until hysteresis cooldown.", _options.MaxGlobalMemoryMB);

                // Trigger proactive spill to disk to relieve pressure if possible
                _ = Task.Run(() => TriggerSpillsUnderPressureAsync(bytes));
            }
            return false;
        }

        public void RegisterSpillable(ISpillable spillable)
        {
            _spillables.TryAdd(spillable, 0);
        }

        public void UnregisterSpillable(ISpillable spillable)
        {
            _spillables.TryRemove(spillable, out _);
        }

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
                    _logger.LogWarning("[MEMORY_PRESSURE_RELIEF] Proactively spilling {Token} to disk ({Size} MB) to alleviate global memory pressure.",
                        spillable.SpillToken, sizeBefore / 1024 / 1024);

                    if (await spillable.SpillAsync())
                    {
                        reclaimed += sizeBefore;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during proactive spill of {Token}", spillable.SpillToken);
                }
            }

            if (reclaimed > 0)
            {
                _logger.LogInformation("[MEMORY_PRESSURE_RELIEF] Proactive spill complete. Reclaimed {Size} MB from {Count} sources.",
                    reclaimed / 1024 / 1024, candidates.Count(c => c.MemoryUsageBytes == 0)); // This count is a bit loose but works
            }

            return reclaimed;
        }

        private bool CanAcquireMemory(long bytes, long maxBytes)
        {
            lock (_memLock)
            {
                return CanAcquireMemoryInternal(bytes, maxBytes);
            }
        }

        private void ReleaseMemory(long bytes)
        {
            Interlocked.Add(ref _currentMemoryBytes, -bytes);

            // Only enter the lock if there are actually waiters to process
            if (!_memoryQueue.IsEmpty)
            {
                lock (_memLock)
                {
                    ProcessMemoryQueue();
                }
            }
        }

        private void ProcessMemoryQueue()
        {
            long maxBytes = (long)_options.MaxGlobalMemoryMB * 1024 * 1024;
            while (_memoryQueue.TryPeek(out var next))
            {
                // We use Internal call because we are already holding the lock
                if (CanAcquireMemoryInternal(next.Bytes, maxBytes))
                {
                    if (_memoryQueue.TryDequeue(out _))
                    {
                        // Note: We increment the counter here while holding the lock to ensure CanAcquireMemory
                        // consistency for the next item in the queue.
                        Interlocked.Add(ref _currentMemoryBytes, next.Bytes);
                        next.Tcs.SetResult(true);
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
                        try
                        {
                            reservation.Dispose();
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error during forced reclamation of reservation {Id} for session {SessionId}", reservation.Id, sessionId);
                        }
                    }
                }
            }
        }

        private class MemoryRequest(long bytes, TaskCompletionSource<bool> tcs)
        {
            public long Bytes { get; } = bytes;
            public TaskCompletionSource<bool> Tcs { get; } = tcs;
        }

        private class MemoryRelease : IResourceReservation
        {
            private readonly BufferManager _owner;
            private readonly long _bytes;
            private bool _disposed;

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
                if (_disposed) return;
                _disposed = true;
                _owner.UnregisterReservation(this);
                _owner.ReleaseMemory(_bytes);
            }
        }

        #endregion

        #region Cursor Management

        public async Task<IDisposable> AcquireCursorAsync(string sessionId, bool isOverride = false, object? owner = null)
        {
            if (isOverride)
            {
                _logger.LogWarning("[POLICY_OVERRIDE] Session {SessionId} bypasses cursor limit. Total active cursors may exceed {_Max}.", sessionId, _options.MaxStreamingCursors);
                Interlocked.Increment(ref _activeCursors);
                return RegisterReservation(new CursorRelease(this, sessionId, owner));
            }

            Interlocked.Increment(ref _queuedCursors);
            try
            {
                if (await WaitWithFeedback(sessionId, "Streaming Cursor", _cursorSemaphore.WaitAsync()))
                {
                    Interlocked.Increment(ref _activeCursors);
                    return RegisterReservation(new CursorRelease(this, sessionId, owner));
                }
            }
            finally
            {
                Interlocked.Decrement(ref _queuedCursors);
            }

            throw new TimeoutException($"Resource request for streaming cursor timed out after {_options.ResourceWaitTimeoutSeconds} seconds.");
        }

        private void ReleaseCursor()
        {
            Interlocked.Decrement(ref _activeCursors);
            _cursorSemaphore.Release();
        }

        private class CursorRelease : IResourceReservation
        {
            private readonly BufferManager _owner;
            private bool _disposed;

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
                if (_disposed) return;
                _disposed = true;
                _owner.UnregisterReservation(this);
                _owner.ReleaseCursor();
            }
        }

        #endregion

        private async Task<bool> WaitWithFeedback(string sessionId, string resourceName, Task waitTask)
        {
            int timeoutSeconds = _options.ResourceWaitTimeoutSeconds > 0 ? _options.ResourceWaitTimeoutSeconds : int.MaxValue;
            int elapsed = 0;

            while (elapsed < timeoutSeconds)
            {
                int nextWait = Math.Min(1, timeoutSeconds - elapsed);
                var delayTask = Task.Delay(TimeSpan.FromSeconds(nextWait));
                var completed = await Task.WhenAny(waitTask, delayTask);

                if (completed == waitTask)
                {
                    return true;
                }

                elapsed += nextWait;
                int remainingMinutes = (timeoutSeconds - elapsed) / 60;
                _logger.LogInformation("Session {SessionId} still waiting for {Resource}... ({Remaining} min remaining)",
                    sessionId, resourceName, remainingMinutes > 0 ? remainingMinutes : 0);
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
                var sessions = _sessionReservations.Keys.ToList();
                foreach (var sessionId in sessions)
                {
                    if (_sessionReservations.TryGetValue(sessionId, out var dict))
                    {
                        var toReclaim = new List<IResourceReservation>();
                        foreach (var reservation in dict.Values)
                        {
                            if (reservation.Owner != null && !reservation.Owner.IsAlive)
                            {
                                toReclaim.Add(reservation);
                            }
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
                _logger.LogError(ex, "Error during BufferManager zombie sweep.");
            }
        }
    }
}
