namespace LNU_Web.Options
{
    public class JobLimitsOptions
    {
        public const string SectionName = "Jobs";

        public int MinMatrixSize { get; set; } = 2;
        public int MaxMatrixSize { get; set; } = 3000;
        public int MaxConcurrentJobsPerServer { get; set; } = 2;
    }
}
