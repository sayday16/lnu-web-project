namespace LNU_Web.Models
{
    public class Job
    {
        public Guid Id { get; set; }
        public int UserId { get; set; }
        public User User { get; set; } = null!;
        public int Size { get; set; }
        public int Seed { get; set; }
        public JobStatus Status { get; set; }
        public int Progress { get; set; }
        public bool CancelRequested { get; set; }
        public string Stage { get; set; } = string.Empty;
        public string? ResultJson { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? FinishedAt { get; set; }
    }
}
