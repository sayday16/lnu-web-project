using System.ComponentModel.DataAnnotations;

namespace LNU_Web.Dtos
{
    public class CreateJobRequest
    {
        public int Size { get; set; }

        [Range(0, int.MaxValue, ErrorMessage = "Seed must be an integer between 0 and 2147483647.")]
        public int? Seed { get; set; }
    }
}
