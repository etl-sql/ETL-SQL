using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ETL_SQL.Orchestrator.Execution
{
    /// <summary>
    /// Enforces a configurable cap on concurrently running jobs.
    ///
    /// Callers acquire a slot before spawning a job and release it when the job
    /// exits. Jobs that exceed the cap are queued and will proceed as slots become
    /// available — they are NOT rejected.
    ///
    /// Thread-safety: backed by <see cref="SemaphoreSlim"/>; safe to call from
    /// multiple concurrent scheduler threads.
    /// </summary>
    public class JobThrottle : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private readonly int _maxConcurrent;
        private readonly ILogger<JobThrottle> _logger;

        // Counters for metrics
        private int _activeJobs;
        private int _queuedJobs;

        public JobThrottle(IOptions<JobThrottleOptions> options, ILogger<JobThrottle> logger)
        {
            _maxConcurrent = options.Value.MaxConcurrentJobs > 0
                ? options.Value.MaxConcurrentJobs
                : Math.Max(1, Environment.ProcessorCount / 2);

            _semaphore = new SemaphoreSlim(_maxConcurrent, _maxConcurrent);
            _logger    = logger;

            _logger.LogInformation("JobThrottle initialized: max_concurrent_jobs={Max}", _maxConcurrent);
        }

        /// <summary>
        /// Acquires a concurrency slot, waiting if the cap is already reached.
        /// Returns a disposable that releases the slot on disposal.
        /// </summary>
        public async Task<IDisposable> AcquireAsync(string jobName, CancellationToken ct = default)
        {
            var queued = Interlocked.Increment(ref _queuedJobs);
            _logger.LogDebug("Job {JobName} queued for slot (active={Active}, queued={Queued}, cap={Cap})",
                jobName, _activeJobs, queued, _maxConcurrent);

            await _semaphore.WaitAsync(ct);

            Interlocked.Decrement(ref _queuedJobs);
            var active = Interlocked.Increment(ref _activeJobs);

            _logger.LogInformation("Job {JobName} started (active={Active}/{Cap})", jobName, active, _maxConcurrent);

            return new Slot(this, jobName);
        }

        private void Release(string jobName)
        {
            var active = Interlocked.Decrement(ref _activeJobs);
            _semaphore.Release();
            _logger.LogInformation("Job {JobName} released slot (active={Active}/{Cap})", jobName, active, _maxConcurrent);
        }

        /// <summary>Current snapshot of resource utilization.</summary>
        public JobThrottleMetrics GetMetrics() => new(
            ActiveJobs:  _activeJobs,
            QueuedJobs:  _queuedJobs,
            MaxJobs:     _maxConcurrent,
            AvailableSlots: _semaphore.CurrentCount);

        public void Dispose() => _semaphore.Dispose();

        private sealed class Slot : IDisposable
        {
            private readonly JobThrottle _owner;
            private readonly string _jobName;
            private bool _disposed;

            public Slot(JobThrottle owner, string jobName) { _owner = owner; _jobName = jobName; }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _owner.Release(_jobName);
            }
        }
    }

    public class JobThrottleOptions
    {
        /// <summary>
        /// Maximum number of jobs that may run concurrently.
        /// 0 = auto (ProcessorCount / 2, minimum 1).
        /// </summary>
        public int MaxConcurrentJobs { get; set; } = 0;
    }

    public record JobThrottleMetrics(int ActiveJobs, int QueuedJobs, int MaxJobs, int AvailableSlots);
}
