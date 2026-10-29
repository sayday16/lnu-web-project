using System.Text.Json;
using LNU_Web.Models;
using LNU_Web.Services;

namespace LNU_Web.Dtos
{
    public class JobResponse
    {
        public Guid Id { get; set; }
        public int Size { get; set; }
        public int Seed { get; set; }
        public string? NodeId { get; set; }
        public string Status { get; set; } = string.Empty;
        public int Progress { get; set; }
        public bool CancelRequested { get; set; }
        public string Stage { get; set; } = string.Empty;
        public JsonElement? Result { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? FinishedAt { get; set; }
        public int? QueuePosition { get; set; }
        public int? QueueLength { get; set; }
        public double? EstimatedWaitSeconds { get; set; }
        public double? EstimatedRemainingSeconds { get; set; }

        public static JobResponse From(Job job, QueueSnapshot? snapshot = null)
        {
            var response = Map(job);
            if (snapshot == null)
            {
                return response;
            }

            var queued = snapshot.FindQueued(job.Id);
            if (queued != null)
            {
                response.QueuePosition = queued.Position;
                response.QueueLength = snapshot.Queue.Count;
                response.EstimatedWaitSeconds = queued.EstimatedWaitSeconds;
            }

            response.EstimatedRemainingSeconds = snapshot.FindRemainingSeconds(job.Id);
            return response;
        }

        private static JobResponse Map(Job job)
        {
            return new JobResponse
            {
                Id = job.Id,
                Size = job.Size,
                Seed = job.Seed,
                NodeId = job.NodeId,
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
