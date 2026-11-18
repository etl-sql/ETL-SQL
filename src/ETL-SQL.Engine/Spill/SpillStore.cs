using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Apache.Arrow;
using Apache.Arrow.Arrays;
using Apache.Arrow.Ipc;
using Apache.Arrow.Types;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Spill;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Spill;

/// <summary>
/// Implements encrypted, compressed storage for spilling large data sets to disk.
/// This implementation is session-aware and dynamically reacts to changes in
/// SessionId or SessionRoot after initialization.
/// </summary>
public partial class SpillStore : ISpillStore
{
    private const string DateOnlyPrefix = "\x1Edateonly:";
    private static bool IsDecimalLogicalType(string? logicalType) =>
        logicalType?.StartsWith("DECIMAL", StringComparison.OrdinalIgnoreCase) == true
        || logicalType?.StartsWith("NUMERIC", StringComparison.OrdinalIgnoreCase) == true
        || string.Equals(logicalType, "MONEY", StringComparison.OrdinalIgnoreCase);

    private static object? UnwrapJsonValue(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.Number => element.TryGetDecimal(out var d) ? d : (object?)element.GetDouble(),
            JsonValueKind.String => decimal.TryParse(element.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d) ? (object?)d : (DateTime.TryParse(element.GetString() ?? "", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var dt) ? dt : (object?)element.GetString()),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => element,
            JsonValueKind.Object => element,
            _ => element.GetRawText()
        };
    private string? _cachedRootPath;
    // Held open while this store owns a disposable temp root; see SpillRootOwnership.
    private IDisposable? _ownerClaim;
    private byte[]? _cachedSessionKey;
    private string? _cachedSessionId;
    private bool _usingTemporaryFallback;
    private volatile bool _cacheInitialized;
    private readonly object _initLock = new();
    private readonly object _spillWriteGate = new();
    private readonly Queue<TaskCompletionSource<bool>> _spillWriteWaiters = new();
    private readonly IExecutionContext _context;
    private bool _disposed;
    private const double HighCpuCompressionThreshold = 0.70;
    private readonly object _cpuSampleLock = new();
    private TimeSpan _lastSampledCpuTime;
    private long _lastCpuSampleTimestamp;
    private double _cachedCpuUtilization;
    private int _activeSpillWrites;
    private int _waitingSpillWrites;
    private int _openSpillWriters;
    private long _adaptiveFastestSelections;
    private long _adaptiveOptimalSelections;
    private long _bytesSinceReserveCheck;

    public bool IsPersistent { get; set; }

    internal CompressionLevel? CompressionLevelOverride { get; set; }
    internal Func<double>? CpuUtilizationProvider { get; set; }
    internal Func<string, long?>? AvailableBytesProvider { get; set; }
    internal CompressionLevel LastResolvedCompressionLevel { get; private set; } = CompressionLevel.Optimal;
    internal long AdaptiveFastestSelections => Volatile.Read(ref _adaptiveFastestSelections);
    internal long AdaptiveOptimalSelections => Volatile.Read(ref _adaptiveOptimalSelections);

    public string RootPath
    {
        get
        {
            EnsureInitialized();
            return _cachedRootPath!;
        }
    }

    public SpillStore(IExecutionContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Ensures that the spill directory and encryption keys are correctly initialized
    /// based on the current context state.
    /// </summary>
    private void EnsureInitialized()
    {
        // If already initialized and context hasn't changed its persistent/session state, skip.
        if (CacheMatchesContext())
        {
            return;
        }

        lock (_initLock)
        {
            if (CacheMatchesContext())
            {
                return;
            }

            // Invalidate fast-path readers while updating root directory and key state.
            _cacheInitialized = false;
            IsPersistent = _context.IsPersistentSession;
            _cachedSessionId = _context.SessionId;

            if (IsPersistent)
            {
                // Stable path in the session directory.
                //
                // SessionRoot is the root that *contains* session directories, not one session's
                // own: every production caller passes a parent (EngineRunner passes
                // sessionManager.SessionRoot; the Orchestrator and DashboardService pass
                // storageAuthority.CheckpointRoot), and both SessionStateManager and
                // SqliteSessionMetadataStore append the session id to it. This did not, so every
                // session's spill files landed together in <root>/spill while
                // SessionStateManager.CleanupSession looked under <root>/<id>/spill and found
                // nothing — concurrent sessions sharing a directory, and spill data surviving the
                // session that produced it.
                _cachedRootPath = _context.StorageCapability?.TryGetGrantRoot(
                    "checkpoint",
                    ETL_SQL.Core.Multitenancy.TenantStorageAccess.Write,
                    out var checkpointRoot) == true
                    ? Path.Combine(checkpointRoot!, "spill")
                    : Path.Combine(SessionDirectory(_context), "spill");

                // Deterministic Key based on MachineKey + SessionId (Centralized)
                _cachedSessionKey = NormalizeAesKey(
                    _context.SessionStateManager.GetSpillKey(_context.SessionId ?? "DEFAULT"),
                    _context.SessionId ?? "DEFAULT");
                _usingTemporaryFallback = false;
            }
            else if (_cachedRootPath == null || IsPersistent != _context.IsPersistentSession)
            {
                // Disposable temp path
                _cachedRootPath = _context.StorageCapability?.TryGetGrantRoot(
                    "scratch",
                    ETL_SQL.Core.Multitenancy.TenantStorageAccess.Write,
                    out var scratchRoot) == true
                    ? scratchRoot
                    : Path.Combine(Path.GetTempPath(), "ETL-SQL-Spill", Guid.NewGuid().ToString("N"));
                _cachedSessionKey = RandomNumberGenerator.GetBytes(32);
                _usingTemporaryFallback = false;
            }

            try
            {
                if (!Directory.Exists(_cachedRootPath))
                {
                    Directory.CreateDirectory(_cachedRootPath!);
                }
            }
            catch (UnauthorizedAccessException ex) when (IsPersistent)
            {
                _context.Logger.Warning("Persistent spill directory is not writable for session {SessionId}: {Message}. Falling back to temporary spill storage.", _context.SessionId, ex.Message);
                IsPersistent = false;
                _usingTemporaryFallback = true;
                _cachedRootPath = Path.Combine(Path.GetTempPath(), "ETL-SQL-Spill", Guid.NewGuid().ToString("N"));
                _cachedSessionKey = RandomNumberGenerator.GetBytes(32);
                Directory.CreateDirectory(_cachedRootPath);
            }

            // A crashed process cannot delete its temp root; the marker lets the Orchestrator's
            // sweep remove it at once instead of after the 24-hour age-out.
            if (!IsPersistent && IsSweptTempRoot(_cachedRootPath!))
            {
                try
                {
                    _ownerClaim?.Dispose();
                    _ownerClaim = SpillRootOwnership.Claim(_cachedRootPath!);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _context.Logger.Warning("Could not record the owner of spill directory {Path}: {Message}", _cachedRootPath, ex.Message);
                }
            }

            _cacheInitialized = true;
        }
    }

    private static bool IsSweptTempRoot(string root) => string.Equals(
        Path.GetDirectoryName(Path.GetFullPath(root)),
        Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ETL-SQL-Spill")),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private bool CacheMatchesContext()
    {
        if (!_cacheInitialized || _cachedRootPath == null) return false;

        if (_usingTemporaryFallback)
        {
            return _context.IsPersistentSession && _cachedSessionId == _context.SessionId;
        }

        return IsPersistent == _context.IsPersistentSession &&
            (!IsPersistent || _cachedSessionId == _context.SessionId);
    }

    /// <summary>
    /// The directory belonging to this session, matching what <c>SessionStateManager</c> resolves
    /// so that cleanup finds what spill wrote. Falls back to the root itself when there is no
    /// session id to scope by, which is the pre-existing behaviour for an unnamed session.
    /// </summary>
    private static string SessionDirectory(IExecutionContext context) =>
        string.IsNullOrWhiteSpace(context.SessionId)
            ? context.SessionRoot
            : Path.Combine(context.SessionRoot, context.SessionId);

    private static byte[] NormalizeAesKey(byte[]? key, string _)
    {
        if (key is { Length: 16 or 24 or 32 }) return key;

        if (key is { Length: > 0 })
            return SHA256.HashData(key);

        throw new ExecutionException("Persistent spill encryption key is missing or empty.");
    }

    public Task<ISpillWriter> CreateWriterAsync(string chunkName) =>
        CreateWriterAsync(chunkName, _context.EffectiveBatchSize, _context.EffectiveBatchTargetBytes);

    public Task<ISpillWriter> CreateWriterAsync(string chunkName, int flushBatchRows) =>
        CreateWriterAsync(chunkName, flushBatchRows, _context.EffectiveBatchTargetBytes);

    public async Task<ISpillWriter> CreateWriterAsync(string chunkName, int flushBatchRows, long flushBatchBytes)
    {
        EnsureInitialized();
        AdmitNewExtent();
        var path = Path.Combine(_cachedRootPath!, chunkName);
        var encrypt = _context.SpillEncryptionEnabled;
        var compress = _context.SpillCompressionEnabled;
        var openWriters = Interlocked.Increment(ref _openSpillWriters);
        ISpillWriter writer;
        try
        {
            var compressionLevel = ResolveCompressionLevel(openWriters);
            LastResolvedCompressionLevel = compressionLevel;

            if (_context.SpillFormat == "Json")
                writer = new SecureSpillWriter(path, chunkName, _cachedSessionKey!, _context, encrypt, compress, compressionLevel);
            else
                writer = new ArrowSpillWriter(path, chunkName, _cachedSessionKey!, _context, encrypt, compress, flushBatchRows, compressionLevel, flushBatchBytes);
        }
        catch
        {
            Interlocked.Decrement(ref _openSpillWriters);
            throw;
        }

        var telemetry = _context.Telemetry;
        if (telemetry != null)
        {
            lock (telemetry)
                telemetry.SpillExtentCount++;
        }
        writer = writer is IColumnarSpillWriter
            ? new GatedColumnarSpillWriter(writer, this, _context)
            : new GatedSpillWriter(writer, this, _context);
        return await Task.FromResult(writer);
    }

    internal CompressionLevel ResolveCompressionLevel(int openWriters)
    {
        if (CompressionLevelOverride.HasValue)
            return CompressionLevelOverride.Value;

        var configured = _context.SpillCompressionLevel?.Trim();
        if (string.Equals(configured, "Fastest", StringComparison.OrdinalIgnoreCase))
            return CompressionLevel.Fastest;
        if (string.Equals(configured, "Optimal", StringComparison.OrdinalIgnoreCase))
            return CompressionLevel.Optimal;
        if (string.Equals(configured, "SmallestSize", StringComparison.OrdinalIgnoreCase))
            return CompressionLevel.SmallestSize;

        // Adaptive mode: conserve disk reserve when tight; otherwise drop to Fastest under high CPU or concurrent writer pressure.
        var reserve = _context.SpillMinimumFreeBytes;
        if (reserve > 0 && _cachedRootPath != null)
        {
            var available = AvailableBytesProvider != null
                ? AvailableBytesProvider(_cachedRootPath)
                : TryGetAvailableBytes(_cachedRootPath);
            if (available is long freeBytes && freeBytes < reserve * 4L)
            {
                Interlocked.Increment(ref _adaptiveOptimalSelections);
                return CompressionLevel.Optimal;
            }
        }

        var cpuUtilization = CpuUtilizationProvider != null
            ? CpuUtilizationProvider()
            : SampleProcessCpuUtilization();

        if (cpuUtilization >= HighCpuCompressionThreshold || openWriters >= 2)
        {
            Interlocked.Increment(ref _adaptiveFastestSelections);
            return CompressionLevel.Fastest;
        }

        Interlocked.Increment(ref _adaptiveOptimalSelections);
        return CompressionLevel.Optimal;
    }

    private double SampleProcessCpuUtilization()
    {
        lock (_cpuSampleLock)
        {
            var nowTicks = Stopwatch.GetTimestamp();
            if (_lastCpuSampleTimestamp == 0)
            {
                _lastSampledCpuTime = Process.GetCurrentProcess().TotalProcessorTime;
                _lastCpuSampleTimestamp = nowTicks;
                return _cachedCpuUtilization;
            }

            var elapsedTicks = nowTicks - _lastCpuSampleTimestamp;
            var elapsedMs = elapsedTicks * 1000.0 / Stopwatch.Frequency;
            if (elapsedMs < 250.0)
                return _cachedCpuUtilization;

            var currentCpu = Process.GetCurrentProcess().TotalProcessorTime;
            var cpuSeconds = (currentCpu - _lastSampledCpuTime).TotalSeconds;
            var elapsedSeconds = elapsedMs / 1000.0;
            _cachedCpuUtilization = elapsedSeconds > 0
                ? Math.Clamp(cpuSeconds / elapsedSeconds / Math.Max(1, Environment.ProcessorCount), 0.0, 1.0)
                : 0.0;
            _lastSampledCpuTime = currentCpu;
            _lastCpuSampleTimestamp = nowTicks;
            return _cachedCpuUtilization;
        }
    }

    internal void ReleaseOpenWriter() => Interlocked.Decrement(ref _openSpillWriters);

    // Refuse a new extent while the spill volume is below its reserve, before any byte is written.
    // The message names the setting, not the path: it reaches script authors and job history.
    private void AdmitNewExtent()
    {
        var reserve = _context.SpillMinimumFreeBytes;
        if (reserve <= 0 || TryGetAvailableBytes(_cachedRootPath!) is not long available || available >= reserve)
            return;
        const long mb = 1024 * 1024;
        throw new ExecutionException(
            $"Spill storage is low on space: {available / mb:N0} MB free, below the {reserve / mb:N0} MB reserve " +
            "(Engine:SpillMinimumFreeDiskMB). Free space on the spill volume or move the spill root to a larger one.");
    }

    private static long? TryGetAvailableBytes(string directory)
    {
        try
        {
            var full = Path.GetFullPath(directory);
            // On Unix DriveInfo reports the file system holding any path; Windows needs the root.
            var drive = new DriveInfo(OperatingSystem.IsWindows() ? Path.GetPathRoot(full)! : full);
            return drive.IsReady ? drive.AvailableFreeSpace : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null; // Unknown free space never blocks a spill; the write itself still fails on a full disk.
        }
    }

    private void TrackBytesWrittenAndCheckReserve(long bytesDelta)
    {
        if (bytesDelta <= 0 || _context.SpillMinimumFreeBytes <= 0 || _cachedRootPath == null)
            return;

        const long checkIntervalBytes = 4L * 1024 * 1024;
        var accumulated = Interlocked.Add(ref _bytesSinceReserveCheck, bytesDelta);
        if (accumulated >= checkIntervalBytes)
        {
            Interlocked.Exchange(ref _bytesSinceReserveCheck, 0);
            AdmitNewExtent();
        }
    }

    private ValueTask<SpillWriteSlot> AcquireWriteSlotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_spillWriteGate)
        {
            var limit = Math.Max(1, _context.EffectiveSpillWriteConcurrency);
            if (_activeSpillWrites < limit)
            {
                _activeSpillWrites++;
                return ValueTask.FromResult(new SpillWriteSlot(this));
            }

            _waitingSpillWrites++;
            if (_context.AdaptiveExecutionEnabled)
                _context.AdaptiveMetrics.ReportQueueDepth(_waitingSpillWrites);
        }

        return WaitForWriteSlotAsync(cancellationToken);
    }

    private async ValueTask<SpillWriteSlot> WaitForWriteSlotAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource<bool>? waiter = null;
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                lock (_spillWriteGate)
                {
                    var limit = Math.Max(1, _context.EffectiveSpillWriteConcurrency);
                    if (_activeSpillWrites < limit)
                    {
                        _activeSpillWrites++;
                        return new SpillWriteSlot(this);
                    }

                    waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    _spillWriteWaiters.Enqueue(waiter);
                }

                await waiter.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            waiter?.TrySetCanceled(cancellationToken);
            WakeNextWriteWaiter();
            throw;
        }
        finally
        {
            lock (_spillWriteGate)
            {
                _waitingSpillWrites--;
                if (_context.AdaptiveExecutionEnabled)
                    _context.AdaptiveMetrics.ReportQueueDepth(_waitingSpillWrites);
            }
        }
    }

    private void ReleaseWriteSlot()
    {
        lock (_spillWriteGate)
        {
            if (_activeSpillWrites > 0)
                _activeSpillWrites--;
        }

        WakeNextWriteWaiter();
    }

    private void WakeNextWriteWaiter()
    {
        while (true)
        {
            TaskCompletionSource<bool>? waiter;
            lock (_spillWriteGate)
            {
                if (!_spillWriteWaiters.TryDequeue(out waiter))
                    return;
            }

            if (waiter.TrySetResult(true))
                return;
        }
    }

    private readonly struct SpillWriteSlot : IDisposable
    {
        private readonly SpillStore _owner;

        public SpillWriteSlot(SpillStore owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            _owner.ReleaseWriteSlot();
        }
    }

    private interface IFaultableSpillWriter
    {
        void MarkFaulted();
    }

    private class GatedSpillWriter : ISpillWriter
    {
        protected readonly ISpillWriter Inner;
        protected readonly SpillStore Store;
        protected readonly IExecutionContext Context;

        public GatedSpillWriter(ISpillWriter inner, SpillStore store, IExecutionContext context)
        {
            Inner = inner;
            Store = store;
            Context = context;
        }

        public string ChunkName => Inner.ChunkName;
        public long BytesWritten => Inner.BytesWritten;

        public async Task WriteRowAsync(Row row)
        {
            try
            {
                using var slot = await Store.AcquireWriteSlotAsync(Context.CancellationToken);
                // Timed whether or not adaptive execution is on: the profile reports spill write cost.
                var before = Inner.BytesWritten;
                var started = Stopwatch.GetTimestamp();
                await Inner.WriteRowAsync(row);
                var delta = Inner.BytesWritten - before;
                Context.AdaptiveMetrics.ReportSpillWrite(
                    delta,
                    Stopwatch.GetElapsedTime(started));
                Store.TrackBytesWrittenAndCheckReserve(delta);
            }
            catch
            {
                (Inner as IFaultableSpillWriter)?.MarkFaulted();
                throw;
            }
        }

        public async Task WriteRowsAsync(IEnumerable<Row> rows)
        {
            try
            {
                using var slot = await Store.AcquireWriteSlotAsync(Context.CancellationToken);
                // Timed whether or not adaptive execution is on: the profile reports spill write cost.
                var before = Inner.BytesWritten;
                var started = Stopwatch.GetTimestamp();
                // Keep reserve checks inside enumeration: a caller may stream an entire external
                // operator through this single call.
                foreach (var row in rows)
                {
                    var rowBefore = Inner.BytesWritten;
                    await Inner.WriteRowAsync(row);
                    Store.TrackBytesWrittenAndCheckReserve(Inner.BytesWritten - rowBefore);
                }
                var delta = Inner.BytesWritten - before;
                Context.AdaptiveMetrics.ReportSpillWrite(
                    delta,
                    Stopwatch.GetElapsedTime(started));
            }
            catch
            {
                (Inner as IFaultableSpillWriter)?.MarkFaulted();
                throw;
            }
        }

        private int _releasedOpenWriter;

        public async ValueTask DisposeAsync()
        {
            try
            {
                await Inner.DisposeAsync();
            }
            finally
            {
                if (Interlocked.Exchange(ref _releasedOpenWriter, 1) == 0)
                    Store.ReleaseOpenWriter();
            }
        }
    }

    private sealed class GatedColumnarSpillWriter : GatedSpillWriter, IColumnarSpillWriter
    {
        public GatedColumnarSpillWriter(ISpillWriter inner, SpillStore store, IExecutionContext context)
            : base(inner, store, context)
        {
        }

        public async Task WriteBatchAsync(ColumnBatch batch)
        {
            if (Inner is not IColumnarSpillWriter columnar)
                throw new NotSupportedException("The underlying spill writer does not support columnar batches.");

            try
            {
                using var slot = await Store.AcquireWriteSlotAsync(Context.CancellationToken);
                // Timed whether or not adaptive execution is on: the profile reports spill write cost.
                var before = Inner.BytesWritten;
                var started = Stopwatch.GetTimestamp();
                await columnar.WriteBatchAsync(batch);
                var delta = Inner.BytesWritten - before;
                Context.AdaptiveMetrics.ReportSpillWrite(
                    delta,
                    Stopwatch.GetElapsedTime(started));
                Store.TrackBytesWrittenAndCheckReserve(delta);
            }
            catch
            {
                (Inner as IFaultableSpillWriter)?.MarkFaulted();
                throw;
            }
        }
    }

    public async Task<ISpillReader> CreateReaderAsync(string chunkName)
    {
        EnsureInitialized();
        var path = Path.Combine(_cachedRootPath!, chunkName);
        var encrypt = _context.SpillEncryptionEnabled;
        var compress = _context.SpillCompressionEnabled;

        ISpillReader reader;
        if (_context.SpillFormat == "Json")
        {
            var jsonReader = new SecureSpillReader(path, chunkName, _cachedSessionKey!, encrypt, compress, _context.CancellationToken);
            await jsonReader.InitializeAsync();
            reader = jsonReader;
        }
        else
        {
            var arrowReader = new ArrowSpillReader(path, chunkName, _cachedSessionKey!, encrypt, compress, _context.Logger, _context.CancellationToken);
            await arrowReader.InitializeAsync();
            reader = arrowReader;
        }

        if (File.Exists(path))
        {
            var telemetry = _context.Telemetry;
            if (telemetry != null)
            {
                lock (telemetry)
                    telemetry.SpillReadBytes += new FileInfo(path).Length;
            }
        }
        return reader;
    }

    public void DeleteChunk(string chunkName)
    {
        EnsureInitialized();
        var path = Path.Combine(_cachedRootPath!, chunkName);
        if (File.Exists(path))
        {
            try { File.Delete(path); }
            catch (Exception ex)
            {
                _context.Logger.Warning("Failed to delete spill chunk {ChunkName}: {Message}", chunkName, ex.Message);
            }
        }
    }

    public void Cleanup()
    {
        EnsureInitialized();
        if (Directory.Exists(_cachedRootPath))
        {
            try
            {
                Directory.Delete(_cachedRootPath!, true);
            }
            catch (Exception ex)
            {
                _context.Logger.Warning("Failed to cleanup spill directory {Path}: {Message}", _cachedRootPath, ex.Message);
            }
        }
        if (!Directory.Exists(_cachedRootPath)) ReleaseOwnership(_cachedRootPath!);
    }

    private void ReleaseOwnership(string root)
    {
        _ownerClaim?.Dispose();
        _ownerClaim = null;
        SpillRootOwnership.Release(root);
    }

    public void Dispose()
    {
        if (_disposed) return;

        // Clean up non-persistent spill directories
        if (!IsPersistent && _cachedRootPath != null && Directory.Exists(_cachedRootPath))
        {
            try
            {
                Directory.Delete(_cachedRootPath, true);
            }
            catch (Exception ex)
            {
                _context.Logger.Warning("Failed to cleanup spill directory on dispose {Path}: {Message}", _cachedRootPath, ex.Message);
            }
        }
        if (!IsPersistent && _cachedRootPath != null && !Directory.Exists(_cachedRootPath))
            ReleaseOwnership(_cachedRootPath);
        _ownerClaim?.Dispose();
        _ownerClaim = null;

        if (_cachedSessionKey != null)
        {
            CryptographicOperations.ZeroMemory(_cachedSessionKey);
            _cachedSessionKey = null;
        }
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    // ── Typed JSON ───────────────────────────────────────────────────────────

    private class SecureSpillWriter : ISpillWriter, IFaultableSpillWriter
    {
        private readonly FileStream _fileStream;
        private readonly AuthenticatedSpillEncryption.FramedEncryptionWriter? _encryptionWriter;
        private bool _disposed;
        private bool _faulted;
        public void MarkFaulted() => _faulted = true;
        private readonly GZipStream? _gzipStream;
        private readonly StreamWriter _writer;
        private readonly IExecutionContext _context;
        private readonly string _chunkName;
        private bool _headerWritten;

        public string ChunkName => _chunkName;
        public long BytesWritten { get; private set; }

        public SecureSpillWriter(string path, string chunkName, byte[] key, IExecutionContext context, bool encrypt, bool compress, CompressionLevel compressionLevel = CompressionLevel.Optimal)
        {
            _chunkName = chunkName;
            _context = context;
            _fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);

            try
            {
                Stream current = _fileStream;

                if (encrypt)
                {
                    _encryptionWriter = AuthenticatedSpillEncryption.CreateWriter(_fileStream, key, chunkName, 1, compress, context.CancellationToken);
                    current = _encryptionWriter;
                }

                if (compress)
                {
                    _gzipStream = new GZipStream(current, compressionLevel, leaveOpen: true);
                    current = _gzipStream;
                }

                _writer = new StreamWriter(current, Encoding.UTF8, 1024, leaveOpen: true);
            }
            catch
            {
                _gzipStream?.Dispose();
                _encryptionWriter?.Dispose();
                _fileStream.Dispose();
                throw;
            }
        }

        public async Task WriteRowAsync(Row row)
        {
            try
            {
                await WriteHeaderAsync();
                var dict = row.Columns;
                // Expand schema aliases into real entries so qualified names survive JSON round-trip.
                if (row.Schema != null)
                {
                    for (int i = 0; i < row.Schema.ColumnCount; i++)
                    {
                        var canon = row.Schema.GetName(i);
                        foreach (var alias in row.Schema.EnumerateAliasesOf(canon))
                            dict.TryAdd(alias, row[i]);
                    }
                }
                var json = JsonRowCodec.Encode(dict);
                // Spill accounting runs whatever SET TELEMETRY says: it backs the enterprise
                // Security:MaxSpillBytesPerScript ceiling, which a script must not be able to switch off.
                if (_context.Telemetry != null)
                {
                    // Fast approximation (+2 for newline)
                    long inc = json.Length + 2;
                    _context.Telemetry.TotalSpilledBytes += inc;
                    BytesWritten += inc;
                    ETL_SQL.Core.Governance.OperationPolicyBoundary.EnforceSpillCeiling(
                        _context, _context.Telemetry.TotalSpilledBytes);
                }
                await _writer.WriteLineAsync(json.AsMemory(), _context.CancellationToken);
            }
            catch
            {
                _faulted = true;
                throw;
            }
        }

        public async Task WriteRowsAsync(IEnumerable<Row> rows)
        {
            try
            {
                foreach (var r in rows) await WriteRowAsync(r);
            }
            catch
            {
                _faulted = true;
                throw;
            }
        }

        private async Task WriteHeaderAsync()
        {
            if (_headerWritten) return;
            await _writer.WriteLineAsync(JsonRowCodec.Header.AsMemory(), _context.CancellationToken);
            _headerWritten = true;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                if (!_faulted)
                {
                    await WriteHeaderAsync();
                    await _writer.DisposeAsync();
                    if (_gzipStream != null) await _gzipStream.DisposeAsync();
                    if (_encryptionWriter != null) await _encryptionWriter.CompleteAsync();
                }
            }
            finally
            {
                try
                {
                    if (_gzipStream != null) await _gzipStream.DisposeAsync();
                }
                finally
                {
                    try { if (_encryptionWriter != null) await _encryptionWriter.DisposeAsync(); }
                    finally
                    {
                        await _fileStream.DisposeAsync();
                    }
                }
            }
        }
    }

    private class SecureSpillReader : ISpillReader
    {
        private readonly string _path;
        private readonly byte[] _key;
        private readonly bool _encrypt;
        private readonly bool _compress;
        private readonly CancellationToken _cancellationToken;
        private readonly string _chunkName;
        private FileStream? _fileStream;
        private Stream? _cryptoStream;
        private GZipStream? _gzipStream;
        private StreamReader? _reader;
        private bool _headerRead;

        public string ChunkName => _chunkName;

        public SecureSpillReader(string path, string chunkName, byte[] key, bool encrypt, bool compress, CancellationToken cancellationToken)
        {
            _path = path;
            _chunkName = chunkName;
            _key = key;
            _encrypt = encrypt;
            _compress = compress;
            _cancellationToken = cancellationToken;
        }

        public async Task InitializeAsync()
        {
            if (!File.Exists(_path))
            {
                // COMPAT_BREAK: 0.22 — a missing spill chunk must not silently remove rows.
                throw new ExecutionException("Spill chunk is missing. Rerun the statement or recreate the saved session.");
            }

            _fileStream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            try
            {
                Stream current = _fileStream;

                if (_encrypt)
                {
                    _cryptoStream = await AuthenticatedSpillEncryption.CreateReaderAsync(_fileStream, _key, _chunkName, 1, _compress, _cancellationToken);
                    current = _cryptoStream;
                }

                if (_compress)
                {
                    _gzipStream = new GZipStream(current, CompressionMode.Decompress);
                    current = _gzipStream;
                }

                _reader = new StreamReader(current, Encoding.UTF8);
            }
            catch
            {
                _reader?.Dispose();
                _reader = null;
                if (_gzipStream != null) { await _gzipStream.DisposeAsync(); _gzipStream = null; }
                if (_cryptoStream != null) { await _cryptoStream.DisposeAsync(); _cryptoStream = null; }
                if (_fileStream != null) { await _fileStream.DisposeAsync(); _fileStream = null; }
                throw;
            }
        }

        public async Task<Row?> ReadRowAsync()
        {
            if (_reader == null) return null;
            if (!_headerRead)
            {
                if (await _reader.ReadLineAsync(_cancellationToken) != JsonRowCodec.Header)
                    throw new ExecutionException("Unsupported JSON spill format. Rerun the statement or recreate the saved session.");
                _headerRead = true;
            }
            var line = await _reader.ReadLineAsync(_cancellationToken);
            if (line == null) return null;

            var row = new Row();
            foreach (var kvp in JsonRowCodec.Decode(line)) row[kvp.Key] = kvp.Value;
            return row;
        }

        public async IAsyncEnumerable<Row> AsEnumerableAsync()
        {
            while (true)
            {
                var row = await ReadRowAsync();
                if (row == null) break;
                yield return row;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_reader != null) _reader.Dispose();
            if (_gzipStream != null) await _gzipStream.DisposeAsync();
            if (_cryptoStream != null) await _cryptoStream.DisposeAsync();
            if (_fileStream != null) await _fileStream.DisposeAsync();
        }
    }

    // ── Arrow IPC ────────────────────────────────────────────────────────────

    private class ArrowSpillWriter : ISpillWriter, IColumnarSpillWriter, IFaultableSpillWriter
    {
        private const string SchemaVersionKey = "etlsql.spill.schema_version";
        private const string LogicalTypeKeyPrefix = "etlsql.spill.column.";
        private const string SchemaVersion = "1";
        private static readonly FixedSizeBinaryType BinaryDecimalType = new(16);
        private readonly FileStream _fileStream;
        private readonly AuthenticatedSpillEncryption.FramedEncryptionWriter? _encryptionWriter;
        private bool _disposed;
        private bool _faulted;
        public void MarkFaulted() => _faulted = true;
        private readonly GZipStream? _gzipStream;
        private readonly Stream _payloadStream;
        private readonly IExecutionContext _context;
        private readonly string _chunkName;
        private readonly int _flushBatchSize;
        private readonly long _flushBatchBytes;

        private const string JsonPrefix = "\x1Ejson:";

        private Schema? _schema;
        private ArrowStreamWriter? _arrowWriter;
        private readonly List<Row> _buffer = new();
        private long _bufferedBytes;
        private long _acceptedRowCount;

        public string ChunkName => _chunkName;
        public long BytesWritten { get; private set; }

        public ArrowSpillWriter(
            string path,
            string chunkName,
            byte[] key,
            IExecutionContext context,
            bool encrypt,
            bool compress,
            int flushBatchSize,
            CompressionLevel compressionLevel = CompressionLevel.Optimal,
            long flushBatchBytes = SpillFanOut.DefaultTargetBatchBytes)
        {
            _chunkName = chunkName;
            _context = context;
            _flushBatchSize = Math.Max(1, flushBatchSize);
            _flushBatchBytes = flushBatchBytes > 0 ? Math.Max(4096L, flushBatchBytes) : 0;
            _fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);

            try
            {
                Stream current = _fileStream;

                if (encrypt)
                {
                    int maxFrameBytes = _flushBatchBytes > 0
                        ? (int)Math.Clamp(_flushBatchBytes, 16 * 1024L, AuthenticatedSpillEncryption.DefaultMaxFramePlaintextBytes)
                        : AuthenticatedSpillEncryption.DefaultMaxFramePlaintextBytes;
                    _encryptionWriter = AuthenticatedSpillEncryption.CreateWriter(_fileStream, key, chunkName, 2, compress, context.CancellationToken, maxFrameBytes);
                    current = _encryptionWriter;
                }

                if (compress)
                {
                    _gzipStream = new GZipStream(current, compressionLevel, leaveOpen: true);
                    current = _gzipStream;
                }

                _payloadStream = current;
            }
            catch
            {
                _gzipStream?.Dispose();
                _encryptionWriter?.Dispose();
                _fileStream.Dispose();
                throw;
            }
        }

        public async Task WriteRowAsync(Row row)
        {
            try
            {
                // Snapshot columns immediately — callers may mutate the row after WriteRowAsync returns
                // (e.g. PartitionStreamMultiSet stamps __SET_IDX on the same Row object across iterations).
                var snapshot = SnapshotRow(row);

                if (_schema == null)
                {
                    _schema = InferSchema(snapshot);
                    _arrowWriter = new ArrowStreamWriter(_payloadStream, _schema, leaveOpen: true);
                    await _arrowWriter.WriteStartAsync(CancellationToken.None);
                }

                _buffer.Add(snapshot);
                _bufferedBytes += snapshot.EstimateHeapBytes();
                _acceptedRowCount++;

                if (_context.Telemetry != null)
                {
                    // Row.ColumnCount, not Row.Columns.Count — the Columns property materializes a
                    // fresh dictionary per access, which on this per-row path was measurable churn.
                    long inc = row.ColumnCount * 16L;
                    _context.Telemetry.TotalSpilledBytes += inc;
                    BytesWritten += inc;
                    ETL_SQL.Core.Governance.OperationPolicyBoundary.EnforceSpillCeiling(
                        _context, _context.Telemetry.TotalSpilledBytes);
                }

                if (_buffer.Count >= _flushBatchSize || (_flushBatchBytes > 0 && _bufferedBytes >= _flushBatchBytes))
                    await FlushBatchAsync();
            }
            catch
            {
                _faulted = true;
                throw;
            }
        }

        // Snapshot plan, rebuilt when the incoming schema or dynamic-column layout changes. The
        // expanded schema carries canonical columns, aliases, and stable operator marker columns so
        // qualified names survive Arrow without falling back to a dictionary copy per row.
        private ETL_SQL.Data.TableSchema? _snapshotSourceSchema;
        private ETL_SQL.Data.TableSchema? _snapshotExpandedSchema;
        private int[] _snapshotAliasSources = System.Array.Empty<int>();
        private string[] _snapshotDynamicNames = System.Array.Empty<string>();

        private Row SnapshotRow(Row row)
        {
            var schema = row.Schema;
            if (schema == null) return SnapshotRowDynamic(row);

            if (!ReferenceEquals(schema, _snapshotSourceSchema) || !HasSameDynamicLayout(row, schema))
            {
                var names = new List<string>(row.ColumnCount);
                var aliasSources = new List<int>();
                var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < schema.ColumnCount; i++)
                {
                    var name = schema.GetName(i);
                    names.Add(name);
                    seenNames.Add(name);
                }
                for (int i = 0; i < schema.ColumnCount; i++)
                {
                    foreach (var alias in schema.EnumerateAliasesOf(schema.GetName(i)))
                    {
                        if (!seenNames.Add(alias)) continue; // duplicate canonical names re-enumerate aliases
                        names.Add(alias);
                        aliasSources.Add(i);
                    }
                }

                var dynamicNames = row.GetColumnNames()
                    .Skip(schema.ColumnCount)
                    .Where(seenNames.Add)
                    .ToArray();
                names.AddRange(dynamicNames);
                _snapshotExpandedSchema = new ETL_SQL.Data.TableSchema(names);
                _snapshotAliasSources = aliasSources.ToArray();
                _snapshotDynamicNames = dynamicNames;
                _snapshotSourceSchema = schema;
            }

            // One values-array copy per row (the snapshot must survive caller mutation) and no
            // per-row dictionaries — this was ~20% of the Gate F round-trip's total allocation
            // (a dynamic-dictionary copy per spilled row; see certification-results/spill-alloc-profile).
            int canonical = schema.ColumnCount;
            var values = new object?[canonical + _snapshotAliasSources.Length + _snapshotDynamicNames.Length];
            for (int i = 0; i < canonical; i++) values[i] = row[i];
            for (int i = 0; i < _snapshotAliasSources.Length; i++)
                values[canonical + i] = row[_snapshotAliasSources[i]];
            int dynamicOffset = canonical + _snapshotAliasSources.Length;
            for (int i = 0; i < _snapshotDynamicNames.Length; i++)
            {
                row.TryGetValue(_snapshotDynamicNames[i], out var value);
                values[dynamicOffset + i] = value;
            }
            return new Row(_snapshotExpandedSchema!, values);
        }

        private bool HasSameDynamicLayout(Row row, ETL_SQL.Data.TableSchema schema)
            => row.ColumnCount - schema.ColumnCount == _snapshotDynamicNames.Length
                && row.HasDynamicColumnLayout(_snapshotDynamicNames);

        private static Row SnapshotRowDynamic(Row row)
        {
            var copy = new Row();
            foreach (var kvp in row.Columns) copy[kvp.Key] = kvp.Value;
            // Expand schema aliases (e.g. "R.Id" → slot 0) into real dynamic entries so they
            // survive Arrow serialization. row.Columns only returns canonical names; aliases are
            // lost on round-trip, breaking join conditions that reference qualified names.
            if (row.Schema != null)
            {
                for (int i = 0; i < row.Schema.ColumnCount; i++)
                {
                    var canon = row.Schema.GetName(i);
                    foreach (var alias in row.Schema.EnumerateAliasesOf(canon))
                        if (!copy.HasColumn(alias))
                            copy[alias] = row[i];
                }
            }
            return copy;
        }

        public async Task WriteRowsAsync(IEnumerable<Row> rows)
        {
            try
            {
                long telemetryBytes = 0;
                foreach (var row in rows)
                {
                    var snapshot = SnapshotRow(row);
                    if (_schema == null)
                    {
                        _schema = InferSchema(snapshot);
                        _arrowWriter = new ArrowStreamWriter(_payloadStream, _schema, leaveOpen: true);
                        await _arrowWriter.WriteStartAsync(CancellationToken.None);
                    }

                    _buffer.Add(snapshot);
                    _bufferedBytes += snapshot.EstimateHeapBytes();
                    _acceptedRowCount++;
                    telemetryBytes += row.ColumnCount * 16L; // allocation-free; see WriteRowAsync
                    if (_buffer.Count >= _flushBatchSize || (_flushBatchBytes > 0 && _bufferedBytes >= _flushBatchBytes))
                        await FlushBatchAsync();
                }

                if (_context.Telemetry != null && telemetryBytes > 0)
                {
                    _context.Telemetry.TotalSpilledBytes += telemetryBytes;
                    BytesWritten += telemetryBytes;
                    ETL_SQL.Core.Governance.OperationPolicyBoundary.EnforceSpillCeiling(
                        _context, _context.Telemetry.TotalSpilledBytes);
                }
            }
            catch
            {
                _faulted = true;
                throw;
            }
        }

        public async Task WriteBatchAsync(ColumnBatch batch)
        {
            ArgumentNullException.ThrowIfNull(batch);
            try
            {
                await FlushBatchAsync();
                if (_schema == null)
                {
                    _schema = InferSchema(batch);
                    _arrowWriter = new ArrowStreamWriter(_payloadStream, _schema, leaveOpen: true);
                    await _arrowWriter.WriteStartAsync(CancellationToken.None);
                }
                else
                {
                    ValidateSchema(batch);
                }

                if (_flushBatchBytes > 0 && batch.RowCount > 1 && batch.AllocatedBytes > _flushBatchBytes)
                {
                    int sliceRows = (int)Math.Clamp(
                        (long)batch.RowCount * _flushBatchBytes / Math.Max(1L, batch.AllocatedBytes),
                        1L,
                        _flushBatchSize);
                    for (int offset = 0; offset < batch.RowCount; offset += sliceRows)
                    {
                        int count = Math.Min(sliceRows, batch.RowCount - offset);
                        using var recordBatch = BuildBatch(batch, offset, count);
                        await _arrowWriter!.WriteRecordBatchAsync(recordBatch);
                    }
                }
                else
                {
                    using var recordBatch = BuildBatch(batch, 0, batch.RowCount);
                    await _arrowWriter!.WriteRecordBatchAsync(recordBatch);
                }
                if (_context.Telemetry != null)
                {
                    var increment = batch.AllocatedBytes;
                    _context.Telemetry.TotalSpilledBytes += increment;
                    BytesWritten += increment;
                    ETL_SQL.Core.Governance.OperationPolicyBoundary.EnforceSpillCeiling(
                        _context, _context.Telemetry.TotalSpilledBytes);
                }
            }
            catch
            {
                _faulted = true;
                throw;
            }
        }

        private async Task FlushBatchAsync()
        {
            if (_buffer.Count == 0 || _arrowWriter == null) return;
            using var batch = BuildBatch(_buffer);
            await _arrowWriter.WriteRecordBatchAsync(batch);
            _buffer.Clear();
            _bufferedBytes = 0;
        }

        private static Schema InferSchema(Row row)
        {
            var columns = row.Columns.ToList();
            var fields = columns
                .Select(kvp => new Field(kvp.Key, GetArrowType(kvp.Value), nullable: true))
                .ToList();
            var metadata = new Dictionary<string, string>
            {
                [SchemaVersionKey] = SchemaVersion
            };
            for (var i = 0; i < columns.Count; i++)
                metadata[$"{LogicalTypeKeyPrefix}{i}.logical_type"] = GetLogicalType(columns[i].Value);
            return new Schema(fields, metadata);
        }

        private static Schema InferSchema(ColumnBatch batch)
        {
            var fields = batch.Schema.Fields
                .Select(field => new Field(field.Name, GetArrowType(field.ElementType), field.IsNullable))
                .ToList();
            var metadata = new Dictionary<string, string>
            {
                [SchemaVersionKey] = SchemaVersion
            };
            for (var i = 0; i < batch.Schema.Count; i++)
                metadata[$"{LogicalTypeKeyPrefix}{i}.logical_type"] = batch.Schema.Fields[i].LogicalType;
            return new Schema(fields, metadata);
        }

        private static IArrowType GetArrowType(Type elementType)
        {
            if (elementType == typeof(byte) || elementType == typeof(short)
                || elementType == typeof(int) || elementType == typeof(long)) return Int64Type.Default;
            // COMPAT_BREAK: 0.21.0 — preserve per-value decimal precision and scale cleanly via 16-byte binary CLR representation.
            if (elementType == typeof(decimal)) return BinaryDecimalType;
            if (elementType == typeof(double) || elementType == typeof(float)) return DoubleType.Default;
            if (elementType == typeof(bool)) return BooleanType.Default;
            if (elementType == typeof(DateOnly)) return StringType.Default;
            if (elementType == typeof(DateTime)) return new TimestampType(TimeUnit.Microsecond, "UTC");
            // Arrow timestamps preserve an instant but not the original DateTimeOffset offset. Store
            // the round-trip representation as UTF-8 so spill/reload retains both pieces of information.
            if (elementType == typeof(DateTimeOffset)) return StringType.Default;
            if (elementType == typeof(Guid)) return StringType.Default;
            // A TIME column is a TimeSpan, which may be negative or exceed 24 hours; Arrow's time
            // types model a time of day and can represent neither.
            if (elementType == typeof(TimeSpan)) return StringType.Default;
            if (elementType == typeof(string)) return StringType.Default;
            throw new NotSupportedException($"Native spill writing does not support '{elementType.Name}' columns.");
        }

        private void ValidateSchema(ColumnBatch batch)
        {
            if (_schema!.FieldsList.Count != batch.Schema.Count)
                throw new InvalidOperationException("Column batch does not match the spill writer schema.");
            for (var i = 0; i < batch.Schema.Count; i++)
            {
                var field = batch.Schema.Fields[i];
                var expectedType = GetArrowType(field.ElementType);
                if (!_schema.FieldsList[i].Name.Equals(field.Name, StringComparison.OrdinalIgnoreCase)
                    || _schema.FieldsList[i].DataType.TypeId != expectedType.TypeId)
                    throw new InvalidOperationException(
                        $"Column batch field {i} ('{field.Name}', {expectedType.Name}) does not match " +
                        $"spill field '{_schema.FieldsList[i].Name}' ({_schema.FieldsList[i].DataType.Name}).");
            }
        }

        private static string GetLogicalType(object? value) => value switch
        {
            null => "Dynamic",
            string => "String",
            int or long or short or byte or uint or ulong or ushort or sbyte => "Integer",
            decimal => "Decimal",
            double or float => "Double",
            bool => "Boolean",
            DateOnly => "DateOnly",
            DateTime => "Timestamp",
            DateTimeOffset => "DateTimeOffset",
            TimeSpan => "TIME",
            Guid => "UUID",
            _ => "Json"
        };

        private static IArrowType GetArrowType(object? value) => value switch
        {
            int or long or short or byte or uint or ulong or ushort or sbyte => Int64Type.Default,
            decimal => BinaryDecimalType,
            double or float => DoubleType.Default,
            bool => BooleanType.Default,
            DateOnly => StringType.Default,
            DateTime => new TimestampType(TimeUnit.Microsecond, "UTC"),
            _ => StringType.Default
        };

        private RecordBatch BuildBatch(List<Row> rows)
        {
            var firstRowNumber = _acceptedRowCount - rows.Count + 1;
            var arrays = _schema!.FieldsList
                .Select((f, i) => BuildArray(f, rows, firstRowNumber,
                    _schema.Metadata[$"{LogicalTypeKeyPrefix}{i}.logical_type"]))
                .ToList();
            return new RecordBatch(_schema, arrays, rows.Count);
        }

        private RecordBatch BuildBatch(ColumnBatch batch, int startRow, int length)
        {
            var arrays = new List<IArrowArray>(batch.Columns.Count);
            for (var i = 0; i < batch.Columns.Count; i++)
                arrays.Add(BuildArray(batch.Columns[i], startRow, length));
            return new RecordBatch(_schema!, arrays, length);
        }

        private static IArrowArray BuildArray(IColumnBuffer column, int startRow, int length)
        {
            if (column is ColumnBuffer<byte> bytes) return BuildInt64(bytes, startRow, length);
            if (column is ColumnBuffer<short> shorts) return BuildInt64(shorts, startRow, length);
            if (column is ColumnBuffer<int> integers) return BuildInt64(integers, startRow, length);
            if (column is ColumnBuffer<long> longs) return BuildInt64(longs, startRow, length);
            if (column is ColumnBuffer<decimal> decimals)
                return BuildBinaryDecimalArray(decimals, startRow, length);
            if (column is ColumnBuffer<double> doubles)
            {
                var builder = new DoubleArray.Builder();
                var span = doubles.Values.Span;
                for (var i = 0; i < length; i++)
                {
                    var idx = startRow + i;
                    if (doubles.IsNull(idx)) builder.AppendNull(); else builder.Append(span[idx]);
                }
                return builder.Build();
            }
            if (column is ColumnBuffer<bool> booleans)
            {
                var builder = new BooleanArray.Builder();
                var span = booleans.Values.Span;
                for (var i = 0; i < length; i++)
                {
                    var idx = startRow + i;
                    if (booleans.IsNull(idx)) builder.AppendNull(); else builder.Append(span[idx]);
                }
                return builder.Build();
            }
            if (column is ColumnBuffer<DateOnly> dateOnlyValues)
            {
                var builder = new StringArray.Builder();
                var span = dateOnlyValues.Values.Span;
                for (var i = 0; i < length; i++)
                {
                    var idx = startRow + i;
                    if (dateOnlyValues.IsNull(idx)) builder.AppendNull();
                    else builder.Append(span[idx].ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
                }
                return builder.Build();
            }
            if (column is ColumnBuffer<DateTime> dates)
            {
                var builder = new TimestampArray.Builder(TimeUnit.Microsecond, "UTC");
                var span = dates.Values.Span;
                for (var i = 0; i < length; i++)
                {
                    var idx = startRow + i;
                    if (dates.IsNull(idx)) builder.AppendNull();
                    else
                    {
                        var value = span[idx];
                        var offset = value.Kind == DateTimeKind.Unspecified
                            ? new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc))
                            : new DateTimeOffset(value);
                        builder.Append(offset);
                    }
                }
                return builder.Build();
            }
            if (column is ColumnBuffer<DateTimeOffset> offsets)
            {
                var builder = new StringArray.Builder();
                var span = offsets.Values.Span;
                for (var i = 0; i < length; i++)
                {
                    var idx = startRow + i;
                    if (offsets.IsNull(idx)) builder.AppendNull();
                    else builder.Append(span[idx].ToString("O", System.Globalization.CultureInfo.InvariantCulture));
                }
                return builder.Build();
            }
            if (column is ColumnBuffer<Guid> guids)
            {
                var builder = new StringArray.Builder();
                var span = guids.Values.Span;
                for (var i = 0; i < length; i++)
                {
                    var idx = startRow + i;
                    if (guids.IsNull(idx)) builder.AppendNull();
                    else builder.Append(span[idx].ToString("D"));
                }
                return builder.Build();
            }
            if (column is ColumnBuffer<TimeSpan> times)
            {
                // "c" is the round-trip format, and it is why this is text rather than an Arrow
                // time type: a TIME column here holds a TimeSpan, which may be negative or exceed
                // 24 hours, and neither is representable as a time-of-day.
                var builder = new StringArray.Builder();
                var span = times.Values.Span;
                for (var i = 0; i < length; i++)
                {
                    var idx = startRow + i;
                    if (times.IsNull(idx)) builder.AppendNull();
                    else builder.Append(span[idx].ToString("c", System.Globalization.CultureInfo.InvariantCulture));
                }
                return builder.Build();
            }
            if (column is Utf8ColumnBuffer strings)
            {
                var offsetsSpan = strings.Offsets.Span;
                var baseByteOffset = offsetsSpan[startRow];
                var totalByteLength = offsetsSpan[startRow + length] - baseByteOffset;
                var offsetBuilder = new ArrowBuffer.Builder<int>();
                offsetBuilder.Reserve(length + 1);
                for (var i = 0; i <= length; i++)
                    offsetBuilder.Append(offsetsSpan[startRow + i] - baseByteOffset);

                var valueBuilder = new ArrowBuffer.Builder<byte>();
                if (totalByteLength > 0)
                {
                    valueBuilder.Reserve(totalByteLength);
                    valueBuilder.Append(strings.Utf8Data.Span.Slice(baseByteOffset, totalByteLength));
                }

                var nullCount = 0;
                ArrowBuffer validityBuffer;
                if (strings.NullBitmap.Span.IndexOfAnyExcept((byte)0) < 0)
                {
                    validityBuffer = ArrowBuffer.Empty;
                }
                else
                {
                    var bitmapBuilder = new ArrowBuffer.BitmapBuilder(length);
                    for (var i = 0; i < length; i++)
                    {
                        var valid = !strings.IsNull(startRow + i);
                        bitmapBuilder.Append(valid);
                        if (!valid) nullCount++;
                    }
                    validityBuffer = nullCount == 0 ? ArrowBuffer.Empty : bitmapBuilder.Build();
                }

                return new StringArray(length, offsetBuilder.Build(), valueBuilder.Build(), validityBuffer, nullCount);
            }
            throw new NotSupportedException($"Native spill writing does not support '{column.ElementType.Name}' columns.");
        }

        private static FixedSizeBinaryArray BuildBinaryDecimalArray(ColumnBuffer<decimal> decimals, int startRow, int length)
        {
            if (length == 0)
            {
                return new FixedSizeBinaryArray(
                    new ArrayData(BinaryDecimalType, 0, 0, 0, [ArrowBuffer.Empty, ArrowBuffer.Empty]));
            }

            var valueBuilder = new ArrowBuffer.Builder<byte>();
            valueBuilder.Reserve(checked(length * 16));
            valueBuilder.Append(MemoryMarshal.AsBytes(decimals.Values.Span.Slice(startRow, length)));
            var valueBuffer = valueBuilder.Build();

            var nullCount = 0;
            ArrowBuffer validityBuffer;
            if (decimals.NullBitmap.Span.IndexOfAnyExcept((byte)0) < 0)
            {
                validityBuffer = ArrowBuffer.Empty;
            }
            else
            {
                var bitmapBuilder = new ArrowBuffer.BitmapBuilder(length);
                for (var i = 0; i < length; i++)
                {
                    var valid = !decimals.IsNull(startRow + i);
                    bitmapBuilder.Append(valid);
                    if (!valid) nullCount++;
                }
                validityBuffer = nullCount == 0 ? ArrowBuffer.Empty : bitmapBuilder.Build();
            }

            return new FixedSizeBinaryArray(
                new ArrayData(BinaryDecimalType, length, nullCount, 0, [validityBuffer, valueBuffer]));
        }

        private static Int64Array BuildInt64<T>(ColumnBuffer<T> column, int startRow, int length) where T : unmanaged
        {
            var builder = new Int64Array.Builder();
            var span = column.Values.Span;
            for (var i = 0; i < length; i++)
            {
                var idx = startRow + i;
                if (column.IsNull(idx)) builder.AppendNull(); else builder.Append(Convert.ToInt64(span[idx]));
            }
            return builder.Build();
        }

        private IArrowArray BuildArray(Field field, List<Row> rows, long firstRowNumber, string logicalType)
        {
            if (IsDecimalLogicalType(logicalType))
            {
                var count = rows.Count;
                var valueBuilder = new ArrowBuffer.Builder<byte>();
                valueBuilder.Reserve(checked(count * 16));
                var bitmapBuilder = new ArrowBuffer.BitmapBuilder(count);
                var nullCount = 0;
                Span<byte> zeroDecimal = stackalloc byte[16];
                zeroDecimal.Clear();
                for (var i = 0; i < count; i++)
                {
                    var value = rows[i].TryGetValue(field.Name, out var found) ? found : null;
                    if (value is null)
                    {
                        valueBuilder.Append(zeroDecimal);
                        bitmapBuilder.Append(false);
                        nullCount++;
                    }
                    else
                    {
                        var dec = ConvertSpillValue(field, value, firstRowNumber + i, Convert.ToDecimal);
                        valueBuilder.Append(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref dec, 1)));
                        bitmapBuilder.Append(true);
                    }
                }
                var validityBuffer = nullCount == 0 ? ArrowBuffer.Empty : bitmapBuilder.Build();
                return new FixedSizeBinaryArray(
                    new ArrayData(BinaryDecimalType, count, nullCount, 0, [validityBuffer, valueBuilder.Build()]));
            }
            switch (field.DataType)
            {
                case Int64Type:
                    {
                        var b = new Int64Array.Builder();
                        b.Reserve(rows.Count);
                        for (var i = 0; i < rows.Count; i++)
                        {
                            var row = rows[i];
                            var v = row.TryGetValue(field.Name, out var val) ? val : null;
                            if (v == null) b.AppendNull();
                            else b.Append(ConvertSpillValue(field, v, firstRowNumber + i, Convert.ToInt64));
                        }
                        return b.Build();
                    }
                case Decimal128Type dt:
                    {
                        var b = new Decimal128Array.Builder(dt);
                        b.Reserve(rows.Count);
                        for (var i = 0; i < rows.Count; i++)
                        {
                            var row = rows[i];
                            var v = row.TryGetValue(field.Name, out var val) ? val : null;
                            if (v == null) b.AppendNull();
                            else b.Append(Math.Round(
                                ConvertSpillValue(field, v, firstRowNumber + i, Convert.ToDecimal), dt.Scale));
                        }
                        return b.Build();
                    }
                case DoubleType:
                    {
                        var b = new DoubleArray.Builder();
                        b.Reserve(rows.Count);
                        for (var i = 0; i < rows.Count; i++)
                        {
                            var row = rows[i];
                            var v = row.TryGetValue(field.Name, out var val) ? val : null;
                            if (v == null) b.AppendNull();
                            else b.Append(ConvertSpillValue(field, v, firstRowNumber + i, Convert.ToDouble));
                        }
                        return b.Build();
                    }
                case BooleanType:
                    {
                        var b = new BooleanArray.Builder();
                        b.Reserve(rows.Count);
                        for (var i = 0; i < rows.Count; i++)
                        {
                            var row = rows[i];
                            var v = row.TryGetValue(field.Name, out var val) ? val : null;
                            if (v == null) b.AppendNull();
                            else b.Append(ConvertSpillValue(field, v, firstRowNumber + i, Convert.ToBoolean));
                        }
                        return b.Build();
                    }
                case TimestampType tt:
                    {
                        var b = new TimestampArray.Builder(tt.Unit, tt.Timezone);
                        b.Reserve(rows.Count);
                        for (var i = 0; i < rows.Count; i++)
                        {
                            var row = rows[i];
                            var v = row.TryGetValue(field.Name, out var val) ? val : null;
                            if (v == null) b.AppendNull();
                            else
                            {
                                var dt = ConvertSpillValue(field, v, firstRowNumber + i, CoerceSpillDateTime);
                                var dto = dt.Kind == DateTimeKind.Unspecified
                                    ? new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc))
                                    : new DateTimeOffset(dt);
                                b.Append(dto);
                            }
                        }
                        return b.Build();
                    }
                default:
                    {
                        var b = new StringArray.Builder();
                        b.Reserve(rows.Count);
                        foreach (var row in rows)
                        {
                            var v = row.TryGetValue(field.Name, out var val) ? val : null;
                            if (v == null) b.AppendNull();
                            else if (v is DateOnly dateOnly) b.Append(DateOnlyPrefix + dateOnly.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
                            else if (v is string s) b.Append(s);
                            else b.Append(JsonPrefix + JsonSerializer.Serialize(v));
                        }
                        return b.Build();
                    }
            }
        }

        private static DateTime CoerceSpillDateTime(object value) => value switch
        {
            DateTime dt => dt,
            DateOnly d => d.ToDateTime(TimeOnly.MinValue),
            DateTimeOffset dto => dto.DateTime,
            _ => Convert.ToDateTime(value)
        };

        private T ConvertSpillValue<T>(Field field, object value, long rowNumber, Func<object, T> converter)
        {
            try
            {
                return converter(value);
            }
            catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException or ArgumentException)
            {
                throw new ExecutionException(
                    $"Spill persistence failed for chunk '{_chunkName}', column '{field.Name}', row {rowNumber}: " +
                    $"the inferred schema expects {typeof(T).Name}, but the value has runtime type " +
                    $"{value.GetType().Name}. The column contains incompatible heterogeneous values; " +
                    "correct or explicitly widen it in the statement that produced the table before persistence.");
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                if (!_faulted)
                {
                    await FlushBatchAsync();
                    if (_arrowWriter == null)
                    {
                        _schema = new Schema.Builder().Build();
                        _arrowWriter = new ArrowStreamWriter(_payloadStream, _schema, leaveOpen: true);
                        await _arrowWriter.WriteStartAsync(_context.CancellationToken);
                    }
                    if (_arrowWriter != null)
                        await _arrowWriter.WriteEndAsync(CancellationToken.None);
                    _arrowWriter?.Dispose();
                    _arrowWriter = null;
                    if (_gzipStream != null) await _gzipStream.DisposeAsync();
                    if (_encryptionWriter != null) await _encryptionWriter.CompleteAsync();
                }
            }
            finally
            {
                _buffer.Clear();
                _bufferedBytes = 0;
                _arrowWriter?.Dispose();
                try
                {
                    if (_gzipStream != null) await _gzipStream.DisposeAsync();
                }
                finally
                {
                    try { if (_encryptionWriter != null) await _encryptionWriter.DisposeAsync(); }
                    finally
                    {
                        await _fileStream.DisposeAsync();
                    }
                }
            }

        }
    }

    private class ArrowSpillReader : ISpillReader, IColumnarSpillReader
    {
        private const string SchemaVersionKey = "etlsql.spill.schema_version";
        private const string LogicalTypeKeyPrefix = "etlsql.spill.column.";
        private readonly string _path;
        private readonly byte[] _key;
        private readonly bool _encrypt;
        private readonly bool _compress;
        private readonly CancellationToken _cancellationToken;
        private readonly string _chunkName;
        private FileStream? _fileStream;
        private Stream? _cryptoStream;
        private GZipStream? _gzipStream;
        private ArrowStreamReader? _arrowReader;

        private RecordBatch? _currentBatch;
        private int _currentBatchRow;
        private SpillReadMode _readMode;

        // Row-extraction caches, built once from the stream's (single, fixed) Arrow schema: a shared
        // TableSchema so rehydrated rows are array-backed instead of per-row dynamic dictionaries,
        // and pre-resolved logical types so metadata keys are not re-interpolated per row per column.
        private ETL_SQL.Data.TableSchema? _rowSchema;
        private string?[]? _logicalTypes;

        public string ChunkName => _chunkName;
        private readonly ILogger? _logger;

        public ArrowSpillReader(string path, string chunkName, byte[] key, bool encrypt, bool compress, ILogger? logger = null, CancellationToken cancellationToken = default)
        {
            _path = path;
            _chunkName = chunkName;
            _key = key;
            _encrypt = encrypt;
            _compress = compress;
            _cancellationToken = cancellationToken;
            _logger = logger;
        }

        public async Task InitializeAsync()
        {
            if (!File.Exists(_path))
            {
                // COMPAT_BREAK: 0.22 — empty partitions have files; absence is data loss.
                throw new ExecutionException("Spill chunk is missing. Rerun the statement or recreate the saved session.");
            }

            _fileStream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            try
            {
                Stream current = _fileStream;

                if (_encrypt)
                {
                    _cryptoStream = await AuthenticatedSpillEncryption.CreateReaderAsync(_fileStream, _key, _chunkName, 2, _compress, _cancellationToken);
                    current = _cryptoStream;
                }

                if (_compress)
                {
                    _gzipStream = new GZipStream(current, CompressionMode.Decompress);
                    current = _gzipStream;
                }

                _arrowReader = new ArrowStreamReader(current);
            }
            catch
            {
                _arrowReader?.Dispose();
                _arrowReader = null;
                if (_gzipStream != null) { await _gzipStream.DisposeAsync(); _gzipStream = null; }
                if (_cryptoStream != null) { await _cryptoStream.DisposeAsync(); _cryptoStream = null; }
                if (_fileStream != null) { await _fileStream.DisposeAsync(); _fileStream = null; }
                throw;
            }
        }

        public async Task<Row?> ReadRowAsync()
        {
            EnterReadMode(SpillReadMode.Rows);
            if (_arrowReader == null) return null;

            while (true)
            {
                if (_currentBatch != null && _currentBatchRow < _currentBatch.Length)
                    return ExtractRow(_currentBatch, _currentBatchRow++);

                _currentBatch?.Dispose();
                _currentBatch = await _arrowReader.ReadNextRecordBatchAsync();
                if (_currentBatch == null) return null;
                _currentBatchRow = 0;
            }
        }

        public async IAsyncEnumerable<Row> AsEnumerableAsync()
        {
            while (true)
            {
                var row = await ReadRowAsync();
                if (row == null) break;
                yield return row;
            }
        }

        private Row ExtractRow(RecordBatch batch, int rowIndex)
        {
            // The Arrow stream format carries one schema for the whole file, so the shared
            // TableSchema and per-column logical types are built once and reused for every row.
            // Array-backed rows here removed ~20% of the Gate F round-trip's total allocation
            // (a Dictionary<string,object> + entry array per rehydrated row) plus the per-row
            // metadata-key string interpolation — see certification-results/spill-alloc-profile.
            if (_rowSchema == null || _logicalTypes == null)
            {
                var fields = batch.Schema.FieldsList;
                var names = new string[fields.Count];
                _logicalTypes = new string?[fields.Count];
                for (int i = 0; i < fields.Count; i++)
                {
                    names[i] = fields[i].Name;
                    _logicalTypes[i] = GetLogicalType(batch.Schema, i);
                }
                _rowSchema = new ETL_SQL.Data.TableSchema(names);
            }

            var row = new Row(_rowSchema);
            for (int i = 0; i < _logicalTypes.Length; i++)
                row[i] = ExtractValue(batch.Column(i), rowIndex, _logicalTypes[i]);
            return row;
        }

        public async IAsyncEnumerable<ColumnBatch> AsColumnBatchesAsync()
        {
            EnterReadMode(SpillReadMode.Columns);
            if (_arrowReader == null) yield break;

            while (await _arrowReader.ReadNextRecordBatchAsync() is { } batch)
            {
                ColumnBatch? columnBatch = null;
                try
                {
                    columnBatch = ConvertBatch(batch);
                    columnBatch.OnFinalRelease(() => ClearAndDisposeRecordBatch(batch));
                }
                catch
                {
                    columnBatch?.Dispose();
                    ClearAndDisposeRecordBatch(batch);
                    throw;
                }
                yield return columnBatch;
            }
        }

        private static void ClearAndDisposeRecordBatch(RecordBatch batch)
        {
            try
            {
                for (var i = 0; i < batch.ColumnCount; i++)
                {
                    var data = batch.Column(i).Data;
                    if (data?.Buffers == null) continue;
                    foreach (var buf in data.Buffers)
                    {
                        if (!buf.IsEmpty)
                            CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(buf.Memory).Span);
                    }
                }
            }
            finally
            {
                batch.Dispose();
            }
        }

        private void EnterReadMode(SpillReadMode mode)
        {
            if (_readMode != SpillReadMode.None && _readMode != mode)
                throw new InvalidOperationException("A spill reader cannot mix row and column-batch consumption.");
            _readMode = mode;
        }

        private static ColumnBatch ConvertBatch(RecordBatch batch)
        {
            var fields = new List<ColumnBatchField>(batch.Schema.FieldsList.Count);
            var columns = new List<IColumnBuffer>(batch.Schema.FieldsList.Count);
            try
            {
                for (var i = 0; i < batch.Schema.FieldsList.Count; i++)
                {
                    var field = batch.Schema.FieldsList[i];
                    var logicalType = GetLogicalType(batch.Schema, i) ?? field.DataType.Name;
                    var column = ConvertArray(batch.Column(i), batch.Length, logicalType);
                    fields.Add(new ColumnBatchField(field.Name, column.ElementType, logicalType, field.IsNullable));
                    columns.Add(column);
                }
                return new ColumnBatch(new ColumnBatchSchema(fields), columns, batch.Length);
            }
            catch
            {
                foreach (var column in columns) column.Dispose();
                throw;
            }
        }

        private static IColumnBuffer ConvertArray(IArrowArray array, int count, string logicalType) => array switch
        {
            Int64Array values => WrapFixed<long>(values.ValueBuffer.Memory, values.Offset, count, values.NullBitmapBuffer.Memory, values.NullCount),
            FixedSizeBinaryArray values when values.Data.DataType is FixedSizeBinaryType { ByteWidth: 16 } && IsDecimalLogicalType(logicalType)
                => WrapFixed<decimal>(values.ValueBuffer.Memory, values.Offset, count, values.NullBitmapBuffer.Memory, values.NullCount),
            Decimal128Array values => CopyFixed<decimal>(count, values.IsNull, i => values.GetValue(i) ?? default),
            DoubleArray values => WrapFixed<double>(values.ValueBuffer.Memory, values.Offset, count, values.NullBitmapBuffer.Memory, values.NullCount),
            BooleanArray values => CopyFixed<bool>(count, values.IsNull, i => values.GetValue(i) ?? default),
            TimestampArray values => CopyFixed<DateTime>(count, values.IsNull,
                i => values.GetTimestamp(i)?.UtcDateTime ?? default),
            StringArray values when IsDecimalLogicalType(logicalType)
                => CopyFixed<decimal>(count, values.IsNull,
                    i => decimal.Parse(values.GetString(i)!, System.Globalization.CultureInfo.InvariantCulture)),
            StringArray values when IsDateOnlyLogicalType(logicalType)
                => CopyFixed<DateOnly>(count, values.IsNull,
                    i => ParseDateOnlyString(values.GetString(i)!)),
            StringArray values when IsDateTimeOffsetLogicalType(logicalType)
                => CopyFixed<DateTimeOffset>(count, values.IsNull,
                    i => ParseDateTimeOffsetString(values.GetString(i)!)),
            StringArray values when IsGuidLogicalType(logicalType)
                => CopyFixed<Guid>(count, values.IsNull,
                    i => Guid.Parse(values.GetString(i)!)),
            StringArray values when values.Offset == 0
                && count > 0
                && values.ValueOffsets[0] == 0
                && values.ValueOffsetsBuffer.Memory.Length >= (count + 1) * sizeof(int)
                && values.ValueOffsets[count] <= values.Values.Length
                => Utf8ColumnBuffer.WrapExternal(
                    values.ValueOffsetsBuffer.Memory,
                    values.ValueBuffer.Memory,
                    count,
                    values.ValueOffsets[count],
                    values.NullBitmapBuffer.Memory,
                    values.Offset,
                    values.NullCount),
            StringArray values => Utf8ColumnBuffer.CopyEncoded(
                values.ValueOffsets,
                values.Values,
                values.NullBitmapBuffer.Span,
                values.Offset),
            _ => throw new NotSupportedException($"Arrow spill column type '{array.Data.DataType.Name}' is not supported by native batches.")
        };

        private static ColumnBuffer<T> WrapFixed<T>(
            ReadOnlyMemory<byte> rawBuffer,
            int offset,
            int count,
            ReadOnlyMemory<byte> validityBitmap,
            int nullCount) where T : unmanaged
        {
            if (count == 0) return ColumnBuffer<T>.Rent(0);
            var byteOffset = checked(offset * Unsafe.SizeOf<T>());
            var byteCount = checked(count * Unsafe.SizeOf<T>());
            return ColumnBuffer<T>.WrapExternal(
                rawBuffer.Slice(byteOffset, byteCount),
                count,
                validityBitmap,
                offset,
                nullCount);
        }

        private static ColumnBuffer<T> CopyFixed<T>(
            int count,
            Func<int, bool> isNull,
            Func<int, T> getValue) where T : unmanaged
        {
            var buffer = ColumnBuffer<T>.Rent(count);
            try
            {
                var values = buffer.Values.Span;
                for (var i = 0; i < count; i++)
                {
                    if (isNull(i)) buffer.SetNull(i);
                    else values[i] = getValue(i);
                }
                return buffer;
            }
            catch
            {
                buffer.Dispose();
                throw;
            }
        }

        private static string? GetLogicalType(Schema schema, int columnIndex)
        {
            if (schema.Metadata == null
                || !schema.Metadata.TryGetValue(SchemaVersionKey, out var version)
                || version != "1")
                return null;
            return schema.Metadata.TryGetValue(
                $"{LogicalTypeKeyPrefix}{columnIndex}.logical_type", out var logicalType)
                ? logicalType
                : null;
        }

        private object? ExtractValue(IArrowArray array, int index, string? logicalType)
        {
            if (array.IsNull(index)) return null;

            return array switch
            {
                Int64Array a => (object?)(decimal?)a.GetValue(index),
                FixedSizeBinaryArray a when a.Data.DataType is FixedSizeBinaryType { ByteWidth: 16 } && IsDecimalLogicalType(logicalType)
                    => (object?)MemoryMarshal.Read<decimal>(a.GetBytes(index)),
                Decimal128Array a => (object?)a.GetValue(index),
                DoubleArray a => ToDecimalOrDouble(a.GetValue(index)),
                BooleanArray a => (object?)a.GetValue(index),
                TimestampArray a => (object?)a.GetTimestamp(index)?.UtcDateTime,
                StringArray a when IsDateOnlyLogicalType(logicalType)
                    => ParseDateOnlyString(a.GetString(index)!),
                StringArray a when IsDateTimeOffsetLogicalType(logicalType)
                    => ParseDateTimeOffsetString(a.GetString(index)!),
                StringArray a when IsGuidLogicalType(logicalType)
                    => Guid.Parse(a.GetString(index)!),
                StringArray a => (object?)DecodeArrowString(a.GetString(index), logicalType, _logger),
                _ => null
            };
        }

        private const string JsonPrefix = "\x1Ejson:";

        private static bool IsTextLogicalType(string? logicalType)
        {
            if (logicalType == null) return false;
            var paren = logicalType.IndexOf('(');
            var baseType = (paren < 0 ? logicalType : logicalType[..paren]).Trim().ToUpperInvariant();
            return baseType is "VARCHAR" or "NVARCHAR" or "CHAR" or "NCHAR" or "TEXT" or "NTEXT" or "VARCHAR2" or "STRING";
        }

        private static object? DecodeArrowString(string? s, string? logicalType, ILogger? logger = null)
        {
            if (s == null) return null;
            if (logicalType == "String") return s;
            if (IsDecimalLogicalType(logicalType))
                return decimal.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
            if (IsDateOnlyLogicalType(logicalType))
                return ParseDateOnlyString(s);
            if (s.StartsWith(DateOnlyPrefix, StringComparison.Ordinal))
                return ParseDateOnlyString(s);
            if (IsDateTimeOffsetLogicalType(logicalType))
                return ParseDateTimeOffsetString(s);
            if (s.StartsWith(JsonPrefix, StringComparison.Ordinal))
            {
                try
                {
                    var element = JsonSerializer.Deserialize<JsonElement>(s.AsSpan(JsonPrefix.Length));
                    return UnwrapJsonValue(element);
                }
                catch (Exception ex)
                {
                    logger?.Warning("Failed to deserialize spilled JSON value for logical type {LogicalType}: {Message}", logicalType, ex.Message);
                }
            }
            // A column the writer knew to be text is text. Parsing it turned '00123' into 123 and
            // '2026-01-05' into a date when a temp table spilled.
            if (IsTextLogicalType(logicalType)) return s;
            // Match JSON reader: try numeric then date parse on plain strings
            if (decimal.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d)) return d;
            if (DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var dt)) return dt;
            return s;
        }

        private static bool IsDateOnlyLogicalType(string? logicalType)
            => logicalType?.Equals("DATE", StringComparison.OrdinalIgnoreCase) == true
                || logicalType?.Equals("DateOnly", StringComparison.OrdinalIgnoreCase) == true;

        private static bool IsDateTimeOffsetLogicalType(string? logicalType)
            => logicalType?.StartsWith("DATETIMEOFFSET", StringComparison.OrdinalIgnoreCase) == true
                || logicalType?.Equals("DateTimeOffset", StringComparison.OrdinalIgnoreCase) == true;

        private static bool IsGuidLogicalType(string? logicalType)
            => logicalType?.Equals("GUID", StringComparison.OrdinalIgnoreCase) == true
                || logicalType?.Equals("UUID", StringComparison.OrdinalIgnoreCase) == true
                || logicalType?.Equals("UNIQUEIDENTIFIER", StringComparison.OrdinalIgnoreCase) == true;

        private static DateOnly ParseDateOnlyString(string value)
        {
            var payload = value;
            if (payload.StartsWith(DateOnlyPrefix, StringComparison.Ordinal))
                payload = payload[DateOnlyPrefix.Length..];
            else if (payload.StartsWith(JsonPrefix, StringComparison.Ordinal))
                payload = payload[JsonPrefix.Length..];
            if (payload.Length >= 2 && payload[0] == '"' && payload[^1] == '"')
                payload = JsonSerializer.Deserialize<string>(payload) ?? payload;
            return DateOnly.ParseExact(payload, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static DateTimeOffset ParseDateTimeOffsetString(string value)
        {
            var payload = value.StartsWith(JsonPrefix, StringComparison.Ordinal)
                ? value[JsonPrefix.Length..]
                : value;
            if (payload.Length >= 2 && payload[0] == '"' && payload[^1] == '"')
                payload = JsonSerializer.Deserialize<string>(payload) ?? payload;
            return DateTimeOffset.Parse(payload, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind);
        }

        private static object? ToDecimalOrDouble(double? val)
        {
            if (val == null) return null;
            try { return (decimal)val.Value; }
            catch { return (object?)val.Value; }
        }

        public async ValueTask DisposeAsync()
        {
            _currentBatch?.Dispose();
            _arrowReader?.Dispose();
            if (_gzipStream != null) await _gzipStream.DisposeAsync();
            if (_cryptoStream != null) await _cryptoStream.DisposeAsync();
            if (_fileStream != null) await _fileStream.DisposeAsync();
        }

        private enum SpillReadMode { None, Rows, Columns }
    }
}


