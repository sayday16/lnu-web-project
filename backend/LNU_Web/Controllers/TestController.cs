using Microsoft.AspNetCore.Mvc;

namespace LNU_Web.Controllers
{
    [ApiController]
    [Route("api/[controller]")]

    public class TestController : ControllerBase
    {
        [HttpGet]
        public IActionResult Index()
        {
            return Ok(new { message = "бекенд!", date = DateTime.Now });
        }
    }
}
