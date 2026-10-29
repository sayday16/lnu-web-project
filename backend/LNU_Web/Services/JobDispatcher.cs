using System.Text.Json;
using LNU_Web.Data;
using LNU_Web.Models;
using LNU_Web.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LNU_Web.Services
{
    public class JobDispatcher : BackgroundService
    {
        private static readonly TimeSpan LoopInterval = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan NodeLostTimeout = TimeSpan.FromSeconds(10);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly NodeRegistry _nodes;
        private readonly NodeClient _client;
        private readonly JobSignal _signal;
        private readonly JobQueuePlanner _planner;
        private readonly int _maxComputeSeconds;
        private readonly ILogger<JobDispatcher> _logger;
        private readonly Dictionary<Guid, DateTime> _unreachableSince = new();

        public JobDispatcher(
            IServiceScopeFactory scopeFactory,
            NodeRegistry nodes,
            NodeClient client,
            JobSignal signal,
            JobQueuePlanner planner,
            IOptions<JobLimitsOptions> limits,
            ILogger<JobDispatcher> logger)
        {
            _scopeFactory = scopeFactory;
            _nodes = nodes;
            _client = client;
            _signal = signal;
            _planner = planner;
            _maxComputeSeconds = limits.Value.MaxComputeSeconds;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                    await TrackRunningJobsAsync(db, stoppingToken);
                    await DispatchPendingJobsAsync(db, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Job dispatching failed, retrying");
                }

                await _signal.WaitAsync(LoopInterval, stoppingToken);
            }
        }

        private async Task TrackRunningJobsAsync(AppDbContext db, CancellationToken cancellationToken)
        {
            var runningJobs = await db.Jobs
                .AsNoTracking()
                .Where(j => j.Status == JobStatus.Running)
                .Select(j => new { j.Id, j.NodeId, j.CancelRequested })
                .ToListAsync(cancellationToken);

            foreach (var lostId in _unreachableSince.Keys.Except(runningJobs.Select(j => j.Id)).ToList())
            {
                _unreachableSince.Remove(lostId);
            }

            foreach (var job in runningJobs)
            {
                var node = _nodes.Find(job.NodeId);
                if (node == null)
                {
                    await FailAsync(db, job.Id, "The compute node of this job is unknown.", cancellationToken);
                    continue;
                }

                if (job.CancelRequested)
                {
                    await _client.CancelAsync(node.Url, job.Id, cancellationToken);
                }

                var lookup = await _client.GetTaskAsync(node.Url, job.Id, cancellationToken);
                if (!lookup.Reachable)
                {
                    var since = _unreachableSince.TryGetValue(job.Id, out var first) ? first : DateTime.UtcNow;
                    _unreachableSince[job.Id] = since;
                    if (DateTime.UtcNow - since > NodeLostTimeout)
                    {
                        await FailAsync(db, job.Id, $"Compute node {node.Id} is unavailable.", cancellationToken);
                    }
                    continue;
                }

                _unreachableSince.Remove(job.Id);

                if (lookup.State == null)
                {
                    await FailAsync(db, job.Id, $"Compute node {node.Id} lost the job.", cancellationToken);
                    continue;
                }

                await ApplyNodeStateAsync(db, job.Id, lookup.State, cancellationToken);
            }
        }

        private async Task ApplyNodeStateAsync(AppDbContext db, Guid jobId, NodeTaskState state, CancellationToken cancellationToken)
        {
            var running = db.Jobs.Where(j => j.Id == jobId && j.Status == JobStatus.Running);

            switch (state.Status)
            {
                case "Running":
                    await running
                        .Where(j => !j.CancelRequested)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(j => j.Progress, state.Progress)
                            .SetProperty(j => j.Stage, state.Stage), cancellationToken);
                    break;

                case "Completed":
                    var resultJson = JsonSerializer.Serialize(new
                    {
                        firstValues = state.Result?.FirstValues ?? Array.Empty<double>(),
                        maxError = state.Result?.MaxError ?? 0,
                        elapsedMs = state.Result?.ElapsedMs ?? 0
                    });
                    await running.ExecuteUpdateAsync(s => s
                        .SetProperty(j => j.Status, JobStatus.Completed)
                        .SetProperty(j => j.Progress, 100)
                        .SetProperty(j => j.Stage, "Done")
                        .SetProperty(j => j.ResultJson, resultJson)
                        .SetProperty(j => j.FinishedAt, DateTime.UtcNow), cancellationToken);
                    break;

                case "Cancelled":
                    await running.ExecuteUpdateAsync(s => s
                        .SetProperty(j => j.Status, JobStatus.Cancelled)
                        .SetProperty(j => j.Stage, "Cancelled")
                        .SetProperty(j => j.FinishedAt, DateTime.UtcNow), cancellationToken);
                    break;

                case "Failed":
                    await FailAsync(db, jobId, state.ErrorMessage ?? "The compute node failed the job.", cancellationToken, state.Stage);
                    break;
            }
        }

        private async Task DispatchPendingJobsAsync(AppDbContext db, CancellationToken cancellationToken)
        {
            var freeSlots = _nodes.GetAll().Sum(n => n.FreeSlots);
            if (freeSlots == 0)
            {
                return;
            }

            var snapshot = await _planner.BuildAsync(db, cancellationToken);
            var pendingJobs = snapshot.Queue.Take(freeSlots).ToList();

            foreach (var job in pendingJobs)
            {
                var node = _nodes.PickLeastLoaded();
                if (node == null)
                {
                    return;
                }

                var outcome = await _client.StartAsync(node.Url, job.Id, job.Size, job.Seed, _maxComputeSeconds, cancellationToken);

                switch (outcome)
                {
                    case NodeStartOutcome.Started:
                        _nodes.MarkStarted(node.Id);
                        var assigned = await db.Jobs
                            .Where(j => j.Id == job.Id && j.Status == JobStatus.Pending)
                            .ExecuteUpdateAsync(s => s
                                .SetProperty(j => j.Status, JobStatus.Running)
                                .SetProperty(j => j.NodeId, node.Id)
                                .SetProperty(j => j.Stage, "Starting")
                                .SetProperty(j => j.StartedAt, DateTime.UtcNow), cancellationToken);
                        if (assigned == 0)
                        {
                            await _client.CancelAsync(node.Url, job.Id, cancellationToken);
                        }
                        break;

                    case NodeStartOutcome.Busy:
                        _nodes.MarkFull(node.Id);
                        break;

                    case NodeStartOutcome.Unreachable:
                        _nodes.MarkUnreachable(node.Id);
                        break;

                    case NodeStartOutcome.Rejected:
                        await db.Jobs
                            .Where(j => j.Id == job.Id && j.Status == JobStatus.Pending)
                            .ExecuteUpdateAsync(s => s
                                .SetProperty(j => j.Status, JobStatus.Failed)
                                .SetProperty(j => j.Stage, "Failed")
                                .SetProperty(j => j.ErrorMessage, $"Compute node {node.Id} rejected the job.")
                                .SetProperty(j => j.FinishedAt, DateTime.UtcNow), cancellationToken);
                        break;
                }
            }
        }

        private static Task FailAsync(AppDbContext db, Guid jobId, string message, CancellationToken cancellationToken, string stage = "Failed")
        {
            return db.Jobs
                .Where(j => j.Id == jobId && j.Status == JobStatus.Running)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(j => j.Status, JobStatus.Failed)
                    .SetProperty(j => j.Stage, stage)
                    .SetProperty(j => j.ErrorMessage, message)
                    .SetProperty(j => j.FinishedAt, DateTime.UtcNow), cancellationToken);
        }
    }
}
