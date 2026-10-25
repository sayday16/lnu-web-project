using LNU_Web.Data;
using LNU_Web.Dtos;
using LNU_Web.Models;
using LNU_Web.Options;
using LNU_Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LNU_Web.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/[controller]")]
    public class JobsController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly JobQueue _queue;
        private readonly JobLimitsOptions _limits;

        public JobsController(AppDbContext db, JobQueue queue, IOptions<JobLimitsOptions> limits)
        {
            _db = db;
            _queue = queue;
            _limits = limits.Value;
        }

        [HttpGet]
        public async Task<IActionResult> List()
        {
            var userId = GetUserId();
            var jobs = await _db.Jobs
                .AsNoTracking()
                .Where(j => j.UserId == userId)
                .OrderByDescending(j => j.CreatedAt)
                .Take(_limits.MaxStoredJobsPerUser)
                .ToListAsync();

            return Ok(jobs.Select(JobResponse.From));
        }

        [HttpGet("limits")]
        public IActionResult Limits()
        {
            return Ok(new
            {
                _limits.MinMatrixSize,
                _limits.MaxMatrixSize,
                _limits.MaxActiveJobsPerUser,
                _limits.MaxStoredJobsPerUser
            });
        }

        [HttpPost]
        public async Task<IActionResult> Create(CreateJobRequest request)
        {
            if (request.Size < _limits.MinMatrixSize || request.Size > _limits.MaxMatrixSize)
            {
                return BadRequest(new
                {
                    message = $"Size must be between {_limits.MinMatrixSize} and {_limits.MaxMatrixSize}."
                });
            }

            var userId = GetUserId();
            var activeJobs = await _db.Jobs.CountAsync(j =>
                j.UserId == userId && (j.Status == JobStatus.Pending || j.Status == JobStatus.Running));

            if (activeJobs >= _limits.MaxActiveJobsPerUser)
            {
                return Conflict(new
                {
                    message = $"You can have at most {_limits.MaxActiveJobsPerUser} active jobs. Wait for one to finish or cancel it."
                });
            }

            var job = new Job
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Size = request.Size,
                Seed = request.Seed ?? Random.Shared.Next(),
                Status = JobStatus.Pending,
                Progress = 0,
                Stage = "Queued",
                CreatedAt = DateTime.UtcNow
            };

            _db.Jobs.Add(job);
            await _db.SaveChangesAsync();

            _queue.Enqueue(job.Id);

            await RemoveOldJobsAsync(userId);

            return AcceptedAtAction(nameof(Get), new { id = job.Id }, JobResponse.From(job));
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> Get(Guid id)
        {
            var job = await FindOwnJobAsync(id);
            if (job == null)
            {
                return NotFound();
            }

            return Ok(JobResponse.From(job));
        }

        [HttpPost("{id:guid}/cancel")]
        public async Task<IActionResult> Cancel(Guid id)
        {
            var userId = GetUserId();

            var cancelledBeforeStart = await _db.Jobs
                .Where(j => j.Id == id && j.UserId == userId && j.Status == JobStatus.Pending)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(j => j.Status, JobStatus.Cancelled)
                    .SetProperty(j => j.CancelRequested, true)
                    .SetProperty(j => j.Stage, "Cancelled")
                    .SetProperty(j => j.FinishedAt, DateTime.UtcNow));

            if (cancelledBeforeStart == 0)
            {
                await _db.Jobs
                    .Where(j => j.Id == id && j.UserId == userId && j.Status == JobStatus.Running)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(j => j.CancelRequested, true)
                        .SetProperty(j => j.Stage, "Cancelling"));
            }

            var job = await FindOwnJobAsync(id);
            if (job == null)
            {
                return NotFound();
            }

            if (job.Status == JobStatus.Completed || job.Status == JobStatus.Failed)
            {
                return Conflict(new { message = "The job has already finished." });
            }

            return Ok(JobResponse.From(job));
        }

        private async Task RemoveOldJobsAsync(int userId)
        {
            var oldJobIds = await _db.Jobs
                .Where(j => j.UserId == userId)
                .OrderByDescending(j => j.CreatedAt)
                .Skip(_limits.MaxStoredJobsPerUser)
                .Where(j => j.Status != JobStatus.Pending && j.Status != JobStatus.Running)
                .Select(j => j.Id)
                .ToListAsync();

            if (oldJobIds.Count == 0)
            {
                return;
            }

            await _db.Jobs
                .Where(j => oldJobIds.Contains(j.Id))
                .ExecuteDeleteAsync();
        }

        private Task<Job?> FindOwnJobAsync(Guid id)
        {
            var userId = GetUserId();
            return _db.Jobs
                .AsNoTracking()
                .FirstOrDefaultAsync(j => j.Id == id && j.UserId == userId);
        }

        private int GetUserId()
        {
            return int.Parse(User.FindFirst("sub")!.Value);
        }
    }
}
