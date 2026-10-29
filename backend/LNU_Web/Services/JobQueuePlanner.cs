using System.Text.Json;
using LNU_Web.Data;
using LNU_Web.Models;
using LNU_Web.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LNU_Web.Services
{
    public record QueuedJob(Guid Id, int UserId, int Size, int Seed, int Position, double? EstimatedWaitSeconds);

    public record QueueProbe(int UserId, int Size)
    {
        public static readonly Guid JobId = Guid.Empty;
    }

    public class QueueSnapshot
    {
        private readonly Dictionary<Guid, QueuedJob> _queued;
        private readonly Dictionary<Guid, double> _remaining;

        public QueueSnapshot(IReadOnlyList<QueuedJob> queue, Dictionary<Guid, double> remaining, double secondsPerCubedSize)
        {
            Queue = queue;
            _queued = queue.ToDictionary(j => j.Id);
            _remaining = remaining;
            SecondsPerCubedSize = secondsPerCubedSize;
        }

        public IReadOnlyList<QueuedJob> Queue { get; }
        public double SecondsPerCubedSize { get; }

        public double EstimateDurationSeconds(int size)
        {
            return Math.Pow(size, 3) * SecondsPerCubedSize;
        }

        public QueuedJob? FindQueued(Guid id)
        {
            return _queued.GetValueOrDefault(id);
        }

        public double? FindRemainingSeconds(Guid id)
        {
            return _remaining.TryGetValue(id, out var seconds) ? seconds : null;
        }
    }

    public class JobQueuePlanner
    {
        private const int CalibrationSampleSize = 20;
        private const int MinCalibrationSize = 200;
        private const int MinProgressForExtrapolation = 5;
        private static readonly TimeSpan FairnessWindow = TimeSpan.FromMinutes(30);

        private readonly NodeRegistry _nodes;
        private readonly double _defaultSecondsPerCubedSize;

        public JobQueuePlanner(NodeRegistry nodes, IOptions<JobLimitsOptions> limits)
        {
            _nodes = nodes;
            _defaultSecondsPerCubedSize = limits.Value.SecondsPerCubedSize;
        }

        public async Task<QueueSnapshot> BuildAsync(AppDbContext db, CancellationToken cancellationToken, QueueProbe? probe = null)
        {
            var pendingJobs = (await db.Jobs
                .AsNoTracking()
                .Where(j => j.Status == JobStatus.Pending)
                .Select(j => new { j.Id, j.UserId, j.Size, j.Seed, j.CreatedAt })
                .ToListAsync(cancellationToken))
                .Select(j => (j.Id, j.UserId, j.Size, j.Seed, j.CreatedAt))
                .ToList();

            if (probe != null)
            {
                pendingJobs.Add((QueueProbe.JobId, probe.UserId, probe.Size, 0, DateTime.UtcNow));
            }

            var runningJobs = await db.Jobs
                .AsNoTracking()
                .Where(j => j.Status == JobStatus.Running)
                .Select(j => new { j.Id, j.UserId, j.Size, j.Progress, j.StartedAt })
                .ToListAsync(cancellationToken);

            var runningPerUser = runningJobs
                .GroupBy(j => j.UserId)
                .ToDictionary(group => group.Key, group => group.Count());

            var waitingUserIds = pendingJobs.Select(j => j.UserId).Distinct().ToList();
            var fairnessWindowStart = DateTime.UtcNow - FairnessWindow;
            var recentStartsPerUser = await db.Jobs
                .AsNoTracking()
                .Where(j => j.StartedAt >= fairnessWindowStart && waitingUserIds.Contains(j.UserId))
                .GroupBy(j => j.UserId)
                .Select(group => new { UserId = group.Key, Count = group.Count() })
                .ToDictionaryAsync(item => item.UserId, item => item.Count, cancellationToken);

            var secondsPerCubedSize = await GetSecondsPerCubedSizeAsync(db, cancellationToken);
            var now = DateTime.UtcNow;

            var remaining = runningJobs.ToDictionary(
                j => j.Id,
                j => EstimateRemaining(j.Size, j.Progress, j.StartedAt, now, secondsPerCubedSize));

            var ordered = pendingJobs
                .GroupBy(j => j.UserId)
                .SelectMany(group => group
                    .OrderBy(j => j.CreatedAt)
                    .Select((job, index) => new { job, round = runningPerUser.GetValueOrDefault(group.Key) + index }))
                .OrderBy(item => item.round)
                .ThenBy(item => recentStartsPerUser.GetValueOrDefault(item.job.UserId))
                .ThenBy(item => item.job.CreatedAt)
                .Select(item => item.job)
                .ToList();

            var capacity = _nodes.GetAll().Where(n => n.Reachable).Sum(n => n.MaxConcurrentJobs);
            var slots = new PriorityQueue<double, double>();
            if (capacity > 0)
            {
                foreach (var seconds in remaining.Values.OrderBy(s => s).Take(capacity))
                {
                    slots.Enqueue(seconds, seconds);
                }
                for (var i = slots.Count; i < capacity; i++)
                {
                    slots.Enqueue(0, 0);
                }
            }

            var queue = new List<QueuedJob>(ordered.Count);
            for (var index = 0; index < ordered.Count; index++)
            {
                var job = ordered[index];
                double? wait = null;

                if (slots.Count > 0)
                {
                    var start = slots.Dequeue();
                    var finish = start + EstimateDuration(job.Size, secondsPerCubedSize);
                    slots.Enqueue(finish, finish);
                    wait = start;
                }

                queue.Add(new QueuedJob(job.Id, job.UserId, job.Size, job.Seed, index + 1, wait));
            }

            return new QueueSnapshot(queue, remaining, secondsPerCubedSize);
        }

        private async Task<double> GetSecondsPerCubedSizeAsync(AppDbContext db, CancellationToken cancellationToken)
        {
            var samples = await db.Jobs
                .AsNoTracking()
                .Where(j => j.Status == JobStatus.Completed && j.Size >= MinCalibrationSize && j.ResultJson != null)
                .OrderByDescending(j => j.FinishedAt)
                .Take(CalibrationSampleSize)
                .Select(j => new { j.Size, j.ResultJson })
                .ToListAsync(cancellationToken);

            var coefficients = samples
                .Select(s => ReadElapsedSeconds(s.ResultJson!) / Math.Pow(s.Size, 3))
                .Where(c => c > 0)
                .ToList();

            return coefficients.Count > 0 ? coefficients.Average() : _defaultSecondsPerCubedSize;
        }

        private static double EstimateRemaining(int size, int progress, DateTime? startedAt, DateTime now, double secondsPerCubedSize)
        {
            if (startedAt.HasValue && progress >= MinProgressForExtrapolation)
            {
                var elapsed = (now - DateTime.SpecifyKind(startedAt.Value, DateTimeKind.Utc)).TotalSeconds;
                return Math.Max(0, elapsed * (100 - progress) / progress);
            }

            return EstimateDuration(size, secondsPerCubedSize) * (100 - progress) / 100.0;
        }

        private static double EstimateDuration(int size, double secondsPerCubedSize)
        {
            return Math.Pow(size, 3) * secondsPerCubedSize;
        }

        private static double ReadElapsedSeconds(string resultJson)
        {
            try
            {
                using var document = JsonDocument.Parse(resultJson);
                return document.RootElement.TryGetProperty("elapsedMs", out var elapsed)
                    ? elapsed.GetDouble() / 1000
                    : 0;
            }
            catch (JsonException)
            {
                return 0;
            }
        }
    }
}
