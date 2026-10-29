namespace LNU_ComputeNode
{
    public class NodeOptions
    {
        public const string SectionName = "Node";

        public string Id { get; set; } = Environment.MachineName;
        public int MaxConcurrentJobs { get; set; } = 2;
        public int MinMatrixSize { get; set; } = 2;
        public int MaxMatrixSize { get; set; } = 5000;
        public int MaxComputeSeconds { get; set; } = 300;
    }
}
