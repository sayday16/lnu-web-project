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

            var job = new Job
            {
                Id = Guid.NewGuid(),
                UserId = GetUserId(),
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

            return AcceptedAtAction(nameof(Get), new { id = job.Id }, JobResponse.From(job));
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> Get(Guid id)
        {
            var userId = GetUserId();
            var job = await _db.Jobs
                .AsNoTracking()
                .FirstOrDefaultAsync(j => j.Id == id && j.UserId == userId);

            if (job == null)
            {
                return NotFound();
            }

            return Ok(JobResponse.From(job));
        }

        private int GetUserId()
        {
            return int.Parse(User.FindFirst("sub")!.Value);
        }
    }
}
