using LNU_Web.Data;
using LNU_Web.Models;
using LNU_Web.Options;
using LNU_Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LNU_Web.Controllers
{
    [ApiController]
    [Authorize(Roles = AdminOptions.RoleName)]
    [Route("api/[controller]")]
    public class AdminController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly NodeRegistry _nodes;
        private readonly JobQueuePlanner _planner;

        public AdminController(AppDbContext db, NodeRegistry nodes, JobQueuePlanner planner)
        {
            _db = db;
            _nodes = nodes;
            _planner = planner;
        }

        [HttpGet("overview")]
        public async Task<IActionResult> Overview()
        {
            var cancellationToken = HttpContext.RequestAborted;
            var snapshot = await _planner.BuildAsync(_db, cancellationToken);

            var runningJobs = await _db.Jobs
                .AsNoTracking()
                .Where(j => j.Status == JobStatus.Running)
                .OrderBy(j => j.StartedAt)
                .Select(j => new
                {
                    j.Id,
                    j.User.Email,
                    j.Size,
                    j.NodeId,
                    j.Progress,
                    j.Stage,
                    j.StartedAt,
                    j.CancelRequested
                })
                .ToListAsync(cancellationToken);

            var queuedUserIds = snapshot.Queue.Select(q => q.UserId).Distinct().ToList();
            var emails = await _db.Users
                .AsNoTracking()
                .Where(u => queuedUserIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.Email, cancellationToken);

            var today = DateTime.UtcNow.Date;
            var todayByStatus = await _db.Jobs
                .AsNoTracking()
                .Where(j => j.CreatedAt >= today)
                .GroupBy(j => j.Status)
                .Select(group => new { Status = group.Key, Count = group.Count() })
                .ToDictionaryAsync(item => item.Status, item => item.Count, cancellationToken);

            var nodes = _nodes.GetAll();

            return Ok(new
            {
                nodes = nodes.Select(node => new
                {
                    node.Id,
                    node.Url,
                    node.Reachable,
                    node.RunningJobs,
                    node.MaxConcurrentJobs,
                    node.FreeSlots,
                    LastSeenAt = AsUtc(node.LastSeenAt)
                }),
                running = runningJobs.Select(job => new
                {
                    job.Id,
                    job.Email,
                    job.Size,
                    job.NodeId,
                    job.Progress,
                    job.Stage,
                    StartedAt = AsUtc(job.StartedAt),
                    job.CancelRequested,
                    EstimatedRemainingSeconds = snapshot.FindRemainingSeconds(job.Id)
                }),
                queue = snapshot.Queue.Select(job => new
                {
                    job.Id,
                    Email = emails.GetValueOrDefault(job.UserId),
                    job.Size,
                    job.Position,
                    job.EstimatedWaitSeconds
                }),
                stats = new
                {
                    Users = await _db.Users.CountAsync(cancellationToken),
                    ReachableNodes = nodes.Count(n => n.Reachable),
                    TotalNodes = nodes.Count,
                    BusySlots = nodes.Where(n => n.Reachable).Sum(n => n.RunningJobs),
                    TotalSlots = nodes.Where(n => n.Reachable).Sum(n => n.MaxConcurrentJobs),
                    JobsToday = todayByStatus.Values.Sum(),
                    CompletedToday = todayByStatus.GetValueOrDefault(JobStatus.Completed),
                    FailedToday = todayByStatus.GetValueOrDefault(JobStatus.Failed),
                    CancelledToday = todayByStatus.GetValueOrDefault(JobStatus.Cancelled)
                }
            });
        }

        private static DateTime? AsUtc(DateTime? value)
        {
            return value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null;
        }
    }
}
