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
        public bool CancelRequested { get; set; }
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
                CancelRequested = job.CancelRequested,
                Stage = job.Stage,
                Result = ParseResult(job.ResultJson),
                ErrorMessage = job.ErrorMessage,
                CreatedAt = AsUtc(job.CreatedAt),
                StartedAt = job.StartedAt.HasValue ? AsUtc(job.StartedAt.Value) : null,
                FinishedAt = job.FinishedAt.HasValue ? AsUtc(job.FinishedAt.Value) : null
            };
        }

        private static DateTime AsUtc(DateTime value)
        {
            return DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }

        private static JsonElement? ParseResult(string? resultJson)
        {
            if (resultJson == null)
            {
                return null;
            }

            using var document = JsonDocument.Parse(resultJson);
            return document.RootElement.Clone();
        }
    }
}
