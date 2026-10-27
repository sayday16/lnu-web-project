namespace LNU_Web.Options
{
    public class NodeEndpoint
    {
        public string Id { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
    }

    public class NodesOptions
    {
        public const string SectionName = "Nodes";

        public List<NodeEndpoint> Endpoints { get; set; } = new();
    }
}
