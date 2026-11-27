using System;
using System.Threading;
using System.Threading.Tasks;

namespace ETL_SQL.Core.Execution;

/// <summary>
/// Coordinates <see cref="IBufferManager"/> streaming cursor permits across a nested operator
/// pipeline so an outer operator does not hold the last cursor slot while waiting on an upstream
/// operator in the same pipeline, while competing pipelines still contend for capacity.
/// </summary>
public static class PipelineCursorScope
{
    private static readonly AsyncLocal<PipelineScopeState?> s_current = new();

    /// <summary>
    /// Installs a deferred cursor scope on the current async flow when no active pipeline scope
    /// exists yet. No permit is acquired until an operator calls <see cref="AcquireCursorAsync"/>.
    /// </summary>
    public static IDisposable EnterDeferred()
    {
        var current = s_current.Value;
        if (current is { IsDisposed: false })
            return NoopDisposable.Instance;

        var state = new PipelineScopeState(current, boundBufferManager: null);
        s_current.Value = state;
        return new DeferredScopeExit(state);
    }

    /// <summary>
    /// Acquires a streaming cursor permit for the active pipeline, reusing the pipeline's existing
    /// permit when called from an inner/upstream operator on the same <paramref name="bufferManager"/>.
    /// </summary>
    public static Task<IDisposable> AcquireCursorAsync(
        IBufferManager? bufferManager,
        string sessionId,
        CancellationToken cancellationToken,
        object? owner = null)
    {
        if (bufferManager == null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IDisposable>(NoopDisposable.Instance);
        }

        var existing = s_current.Value;
        if (existing is { IsDisposed: false } && existing.CanParticipate(bufferManager))
            return existing.AcquireAsync(bufferManager, sessionId, cancellationToken, owner, ownsScopeExit: false);

        // Install synchronously on the caller's ExecutionContext before awaiting so upstream pulls
        // inside the caller inherit this pipeline scope without leaking up to the caller's caller.
        var state = new PipelineScopeState(existing, bufferManager);
        s_current.Value = state;
        return state.AcquireAsync(bufferManager, sessionId, cancellationToken, owner, ownsScopeExit: true);
    }

    /// <summary>
    /// Links two cancellation tokens without allocating a linked token source when only one
    /// token is cancelable or both refer to the same token.
    /// </summary>
    public static CancellationTokenSource? LinkCancellationTokens(
        CancellationToken first,
        CancellationToken second,
        out CancellationToken effectiveToken)
    {
        if (!first.CanBeCanceled)
        {
            effectiveToken = second;
            return null;
        }

        if (!second.CanBeCanceled || first == second)
        {
            effectiveToken = first;
            return null;
        }

        var linked = CancellationTokenSource.CreateLinkedTokenSource(first, second);
        effectiveToken = linked.Token;
        return linked;
    }

    private sealed class PipelineScopeState(PipelineScopeState? previous, IBufferManager? boundBufferManager)
    {
        private readonly PipelineScopeState? _previous = previous;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private IBufferManager? _boundBufferManager = boundBufferManager;
        private IDisposable? _permit;
        private int _refCount;
        private bool _disposed;

        public bool IsDisposed => Volatile.Read(ref _disposed);

        public bool CanParticipate(IBufferManager bufferManager)
            => _boundBufferManager == null || ReferenceEquals(_boundBufferManager, bufferManager);

        public async Task<IDisposable> AcquireAsync(
            IBufferManager bufferManager,
            string sessionId,
            CancellationToken cancellationToken,
            object? owner,
            bool ownsScopeExit)
        {
            var acquiredGate = false;
            var success = false;
            try
            {
                await _gate.WaitAsync(cancellationToken);
                acquiredGate = true;
                cancellationToken.ThrowIfCancellationRequested();

                if (_disposed)
                    throw new ObjectDisposedException(nameof(PipelineCursorScope));

                if (_permit == null)
                {
                    _permit = await bufferManager.AcquireCursorAsync(sessionId, cancellationToken, owner: owner);
                    _boundBufferManager = bufferManager;
                }

                _refCount++;
                success = true;
                return new CursorHandle(this, ownsScopeExit);
            }
            finally
            {
                if (!success && ownsScopeExit)
                    Volatile.Write(ref _disposed, true);
                if (acquiredGate)
                    _gate.Release();
            }
        }

        public void ReleaseHandle(bool ownsScopeExit)
        {
            _gate.Wait();
            try
            {
                if (!_disposed && _refCount > 0)
                {
                    _refCount--;
                    if (_refCount == 0)
                    {
                        _permit?.Dispose();
                        _permit = null;
                        if (ownsScopeExit)
                            Volatile.Write(ref _disposed, true);
                        else
                            _boundBufferManager = null;
                    }
                }
                else if (ownsScopeExit)
                {
                    Volatile.Write(ref _disposed, true);
                }
            }
            finally
            {
                _gate.Release();
            }

            if (ownsScopeExit && ReferenceEquals(s_current.Value, this))
                s_current.Value = _previous;
        }

        public void DisposeScope()
        {
            _gate.Wait();
            try
            {
                Volatile.Write(ref _disposed, true);
                _refCount = 0;
                _permit?.Dispose();
                _permit = null;
                _boundBufferManager = null;
            }
            finally
            {
                _gate.Release();
            }

            if (ReferenceEquals(s_current.Value, this))
                s_current.Value = _previous;
        }
    }

    private sealed class CursorHandle(PipelineScopeState state, bool ownsScopeExit) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            state.ReleaseHandle(ownsScopeExit);
        }
    }

    private sealed class DeferredScopeExit(PipelineScopeState state) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            state.DisposeScope();
        }
    }

    private sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();
        public void Dispose() { }
    }
}
