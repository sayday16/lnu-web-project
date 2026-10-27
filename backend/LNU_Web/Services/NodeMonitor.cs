namespace LNU_Web.Services
{
    public class NodeMonitor : BackgroundService
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

        private readonly NodeRegistry _registry;
        private readonly NodeClient _client;

        public NodeMonitor(NodeRegistry registry, NodeClient client)
        {
            _registry = registry;
            _client = client;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(PollInterval);

            do
            {
                await Task.WhenAll(_registry.GetAll().Select(node => PollAsync(node, stoppingToken)));
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }

        private async Task PollAsync(NodeState node, CancellationToken cancellationToken)
        {
            var status = await _client.GetStatusAsync(node.Url, cancellationToken);

            _registry.Update(status == null
                ? node with { Reachable = false }
                : node with
                {
                    Reachable = true,
                    RunningJobs = status.RunningJobs,
                    MaxConcurrentJobs = status.MaxConcurrentJobs,
                    LastSeenAt = DateTime.UtcNow
                });
        }
    }
}
