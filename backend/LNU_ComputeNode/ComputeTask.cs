namespace LNU_ComputeNode
{
    public enum ComputeStatus
    {
        Running,
        Completed,
        Failed,
        Cancelled
    }

    public record ComputeResultResponse(double[] FirstValues, double MaxError, long ElapsedMs);

    public record ComputeTaskResponse(
        Guid Id,
        string NodeId,
        string Status,
        int Progress,
        string Stage,
        ComputeResultResponse? Result,
        string? ErrorMessage);

    public class ComputeTask
    {
        private readonly object _sync = new();
        private int _progress;
        private string _stage = "Starting";
        private GaussResult? _result;
        private long _elapsedMs;
        private string? _errorMessage;

        public ComputeTask(Guid id, int size, int seed)
        {
            Id = id;
            Size = size;
            Seed = seed;
        }

        public Guid Id { get; }
        public int Size { get; }
        public int Seed { get; }
        public CancellationTokenSource Cancellation { get; } = new();
        public ComputeStatus Status { get; private set; } = ComputeStatus.Running;
        public DateTime? FinishedAt { get; private set; }

        public void ReportProgress(int progress, string stage)
        {
            lock (_sync)
            {
                _progress = progress;
                _stage = stage;
            }
        }

        public void Complete(GaussResult result, long elapsedMs)
        {
            lock (_sync)
            {
                Status = ComputeStatus.Completed;
                _progress = 100;
                _stage = "Done";
                _result = result;
                _elapsedMs = elapsedMs;
                FinishedAt = DateTime.UtcNow;
            }
        }

        public void MarkCancelled()
        {
            lock (_sync)
            {
                Status = ComputeStatus.Cancelled;
                _stage = "Cancelled";
                FinishedAt = DateTime.UtcNow;
            }
        }

        public void Fail(string errorMessage)
        {
            lock (_sync)
            {
                Status = ComputeStatus.Failed;
                _stage = "Failed";
                _errorMessage = errorMessage;
                FinishedAt = DateTime.UtcNow;
            }
        }

        public ComputeTaskResponse ToResponse(string nodeId)
        {
            lock (_sync)
            {
                var result = _result == null
                    ? null
                    : new ComputeResultResponse(_result.FirstValues, _result.MaxError, _elapsedMs);

                return new ComputeTaskResponse(Id, nodeId, Status.ToString(), _progress, _stage, result, _errorMessage);
            }
        }
    }
}
