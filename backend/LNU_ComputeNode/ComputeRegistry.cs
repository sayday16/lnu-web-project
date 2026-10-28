using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace LNU_ComputeNode
{
    public enum StartResult
    {
        Started,
        AlreadyExists,
        Busy
    }

    public class ComputeRegistry
    {
        private static readonly TimeSpan Retention = TimeSpan.FromMinutes(10);

        private readonly ConcurrentDictionary<Guid, ComputeTask> _tasks = new();
        private readonly object _startLock = new();
        private readonly GaussSolver _solver;
        private readonly NodeOptions _options;
        private readonly ILogger<ComputeRegistry> _logger;
        private int _runningJobs;

        public ComputeRegistry(GaussSolver solver, IOptions<NodeOptions> options, ILogger<ComputeRegistry> logger)
        {
            _solver = solver;
            _options = options.Value;
            _logger = logger;
        }

        public int RunningJobs => Volatile.Read(ref _runningJobs);

        public StartResult TryStart(Guid id, int size, int seed, int? requestedTimeLimitSeconds)
        {
            var timeLimitSeconds = requestedTimeLimitSeconds is > 0
                ? Math.Min(requestedTimeLimitSeconds.Value, _options.MaxComputeSeconds)
                : _options.MaxComputeSeconds;

            RemoveExpired();

            lock (_startLock)
            {
                if (_tasks.ContainsKey(id))
                {
                    return StartResult.AlreadyExists;
                }

                if (RunningJobs >= _options.MaxConcurrentJobs)
                {
                    return StartResult.Busy;
                }

                var task = new ComputeTask(id, size, seed, timeLimitSeconds);
                _tasks[id] = task;
                Interlocked.Increment(ref _runningJobs);

                Task.Factory.StartNew(
                    () => Run(task),
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);

                return StartResult.Started;
            }
        }

        public ComputeTask? Find(Guid id)
        {
            return _tasks.GetValueOrDefault(id);
        }

        public ComputeTask? Cancel(Guid id)
        {
            var task = Find(id);
            if (task != null && task.Status == ComputeStatus.Running)
            {
                task.Cancellation.Cancel();
            }
            return task;
        }

        private void Run(ComputeTask task)
        {
            var stopwatch = Stopwatch.StartNew();
            using var timeLimit = new CancellationTokenSource(TimeSpan.FromSeconds(task.TimeLimitSeconds));
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(task.Cancellation.Token, timeLimit.Token);

            try
            {
                var result = _solver.Solve(task.Size, task.Seed, task.ReportProgress, cancellation.Token);
                task.Complete(result, stopwatch.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (timeLimit.IsCancellationRequested && !task.Cancellation.IsCancellationRequested)
            {
                task.Fail($"Time limit of {task.TimeLimitSeconds} s exceeded.", "Time limit exceeded");
            }
            catch (OperationCanceledException)
            {
                task.MarkCancelled();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Compute task {TaskId} failed", task.Id);
                task.Fail(ex.Message);
            }
            finally
            {
                Interlocked.Decrement(ref _runningJobs);
            }
        }

        private void RemoveExpired()
        {
            var threshold = DateTime.UtcNow - Retention;
            foreach (var task in _tasks.Values)
            {
                if (task.FinishedAt.HasValue && task.FinishedAt.Value < threshold)
                {
                    _tasks.TryRemove(task.Id, out _);
                }
            }
        }
    }
}
