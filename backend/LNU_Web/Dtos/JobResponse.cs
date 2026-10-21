using System.Text.Json;
using LNU_Web.Models;

namespace LNU_Web.Dtos
{
    public class JobResponse
    {
        public Guid Id { get; set; }
        public int Size { get; set; }
        public int Seed { get; set; }
        public string Status { get; set; } = string.Empty;
        public int Progress { get; set; }
        public string Stage { get; set; } = string.Empty;
        public JsonElement? Result { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? FinishedAt { get; set; }

        public static JobResponse From(Job job)
        {
            return new JobResponse
            {
                Id = job.Id,
                Size = job.Size,
                Seed = job.Seed,
                Status = job.Status.ToString(),
                Progress = job.Progress,
                Stage = job.Stage,
                Result = job.ResultJson == null ? null : JsonDocument.Parse(job.ResultJson).RootElement.Clone(),
                ErrorMessage = job.ErrorMessage,
                CreatedAt = job.CreatedAt,
                StartedAt = job.StartedAt,
                FinishedAt = job.FinishedAt
            };
        }
    }
}
