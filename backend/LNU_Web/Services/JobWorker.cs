using System.Diagnostics;
using System.Text.Json;
using LNU_Web.Data;
using LNU_Web.Models;
using LNU_Web.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LNU_Web.Services
{
    public class JobWorker : BackgroundService
    {
        private static readonly TimeSpan ProgressWriteInterval = TimeSpan.FromMilliseconds(500);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly JobQueue _queue;
        private readonly GaussSolver _solver;
        private readonly SemaphoreSlim _slots;
        private readonly ILogger<JobWorker> _logger;

        public JobWorker(
            IServiceScopeFactory scopeFactory,
            JobQueue queue,
            GaussSolver solver,
            IOptions<JobLimitsOptions> limits,
            ILogger<JobWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _queue = queue;
            _solver = solver;
            _slots = new SemaphoreSlim(limits.Value.MaxConcurrentJobsPerServer);
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await FailInterruptedJobsAsync(stoppingToken);

            await foreach (var jobId in _queue.ReadAllAsync(stoppingToken))
            {
                await _slots.WaitAsync(stoppingToken);

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await RunJobAsync(jobId, stoppingToken);
                    }
                    finally
                    {
                        _slots.Release();
                    }
                }, CancellationToken.None);
            }
        }

        private async Task FailInterruptedJobsAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            await db.Jobs
                .Where(j => j.Status == JobStatus.Pending || j.Status == JobStatus.Running)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(j => j.Status, JobStatus.Failed)
                    .SetProperty(j => j.Stage, "Interrupted")
                    .SetProperty(j => j.ErrorMessage, "The server was restarted before the job finished.")
                    .SetProperty(j => j.FinishedAt, DateTime.UtcNow), cancellationToken);
        }

        private async Task RunJobAsync(Guid jobId, CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var job = await db.Jobs
                .AsNoTracking()
                .Where(j => j.Id == jobId)
                .Select(j => new { j.Size, j.Seed })
                .FirstOrDefaultAsync(CancellationToken.None);

            if (job == null)
            {
                return;
            }

            var started = await db.Jobs
                .Where(j => j.Id == jobId && j.Status == JobStatus.Pending)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(j => j.Status, JobStatus.Running)
                    .SetProperty(j => j.Stage, "Starting")
                    .SetProperty(j => j.StartedAt, DateTime.UtcNow), CancellationToken.None);

            if (started == 0)
            {
                return;
            }

            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            var stopwatch = Stopwatch.StartNew();
            var lastWrite = TimeSpan.Zero;

            void ReportProgress(int percent, string stage)
            {
                if (stopwatch.Elapsed - lastWrite < ProgressWriteInterval)
                {
                    return;
                }

                lastWrite = stopwatch.Elapsed;
                var updated = db.Jobs
                    .Where(j => j.Id == jobId && j.Status == JobStatus.Running && !j.CancelRequested)
                    .ExecuteUpdate(s => s
                        .SetProperty(j => j.Progress, percent)
                        .SetProperty(j => j.Stage, stage));

                if (updated == 0)
                {
                    cancellation.Cancel();
                }
            }

            try
            {
                var result = _solver.Solve(job.Size, job.Seed, ReportProgress, cancellation.Token);
                stopwatch.Stop();

                var resultJson = JsonSerializer.Serialize(new
                {
                    firstValues = result.FirstValues,
                    maxError = result.MaxError,
                    elapsedMs = stopwatch.ElapsedMilliseconds
                });

                await db.Jobs
                    .Where(j => j.Id == jobId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(j => j.Status, JobStatus.Completed)
                        .SetProperty(j => j.Progress, 100)
                        .SetProperty(j => j.Stage, "Done")
                        .SetProperty(j => j.ResultJson, resultJson)
                        .SetProperty(j => j.FinishedAt, DateTime.UtcNow), CancellationToken.None);
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                await db.Jobs
                    .Where(j => j.Id == jobId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(j => j.Status, JobStatus.Cancelled)
                        .SetProperty(j => j.Stage, "Cancelled")
                        .SetProperty(j => j.FinishedAt, DateTime.UtcNow), CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                await db.Jobs
                    .Where(j => j.Id == jobId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(j => j.Status, JobStatus.Failed)
                        .SetProperty(j => j.Stage, "Interrupted")
                        .SetProperty(j => j.ErrorMessage, "The server was stopped before the job finished.")
                        .SetProperty(j => j.FinishedAt, DateTime.UtcNow), CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Job {JobId} failed", jobId);

                await db.Jobs
                    .Where(j => j.Id == jobId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(j => j.Status, JobStatus.Failed)
                        .SetProperty(j => j.Stage, "Failed")
                        .SetProperty(j => j.ErrorMessage, ex.Message)
                        .SetProperty(j => j.FinishedAt, DateTime.UtcNow), CancellationToken.None);
            }
        }
    }
}
