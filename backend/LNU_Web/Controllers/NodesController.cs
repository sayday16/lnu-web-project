using LNU_Web.Options;
using LNU_Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LNU_Web.Controllers
{
    [ApiController]
    [Authorize(Roles = AdminOptions.RoleName)]
    [Route("api/[controller]")]
    public class NodesController : ControllerBase
    {
        private readonly NodeRegistry _registry;

        public NodesController(NodeRegistry registry)
        {
            _registry = registry;
        }

        [HttpGet]
        public IActionResult List()
        {
            return Ok(_registry.GetAll().Select(node => new
            {
                node.Id,
                node.Url,
                node.Reachable,
                node.RunningJobs,
                node.MaxConcurrentJobs,
                node.FreeSlots,
                LastSeenAt = node.LastSeenAt.HasValue ? DateTime.SpecifyKind(node.LastSeenAt.Value, DateTimeKind.Utc) : (DateTime?)null
            }));
        }
    }
}
