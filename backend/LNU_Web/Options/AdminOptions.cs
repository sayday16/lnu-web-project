namespace LNU_Web.Options
{
    public class AdminOptions
    {
        public const string SectionName = "Admin";
        public const string RoleName = "admin";

        public List<string> Emails { get; set; } = new();
    }
}
