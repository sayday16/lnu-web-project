namespace LNU_Web.Options
{
    public class JobLimitsOptions
    {
        public const string SectionName = "Jobs";

        public int MinMatrixSize { get; set; } = 2;
        public int MaxMatrixSize { get; set; } = 5000;
        public int MaxActiveJobsPerUser { get; set; } = 3;
        public int MaxStoredJobsPerUser { get; set; } = 20;
    }
}
