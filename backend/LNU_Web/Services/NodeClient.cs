using System.Net;

namespace LNU_Web.Services
{
    public record NodeStatus(string NodeId, int RunningJobs, int MaxConcurrentJobs);

    public record NodeComputeResult(double[] FirstValues, double MaxError, long ElapsedMs);

    public record NodeTaskState(
        Guid Id,
        string NodeId,
        string Status,
        int Progress,
        string Stage,
        NodeComputeResult? Result,
        string? ErrorMessage);

    public record NodeTaskLookup(bool Reachable, NodeTaskState? State);

    public enum NodeStartOutcome
    {
        Started,
        Busy,
        Rejected,
        Unreachable
    }

    public class NodeClient
    {
        public const string HttpClientName = "nodes";

        private readonly IHttpClientFactory _httpClientFactory;

        public NodeClient(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        public async Task<NodeStatus?> GetStatusAsync(string nodeUrl, CancellationToken cancellationToken)
        {
            try
            {
                return await CreateClient(nodeUrl).GetFromJsonAsync<NodeStatus>("status", cancellationToken);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                return null;
            }
        }

        public async Task<NodeStartOutcome> StartAsync(string nodeUrl, Guid id, int size, int seed, CancellationToken cancellationToken)
        {
            try
            {
                using var response = await CreateClient(nodeUrl)
                    .PostAsJsonAsync("compute", new { id, size, seed }, cancellationToken);

                return response.StatusCode switch
                {
                    HttpStatusCode.Accepted or HttpStatusCode.Conflict => NodeStartOutcome.Started,
                    HttpStatusCode.ServiceUnavailable => NodeStartOutcome.Busy,
                    _ => NodeStartOutcome.Rejected
                };
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                return NodeStartOutcome.Unreachable;
            }
        }

        public async Task<NodeTaskLookup> GetTaskAsync(string nodeUrl, Guid id, CancellationToken cancellationToken)
        {
            try
            {
                using var response = await CreateClient(nodeUrl).GetAsync($"compute/{id}", cancellationToken);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return new NodeTaskLookup(true, null);
                }

                response.EnsureSuccessStatusCode();
                var state = await response.Content.ReadFromJsonAsync<NodeTaskState>(cancellationToken);
                return new NodeTaskLookup(true, state);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                return new NodeTaskLookup(false, null);
            }
        }

        public async Task CancelAsync(string nodeUrl, Guid id, CancellationToken cancellationToken)
        {
            try
            {
                using var response = await CreateClient(nodeUrl).DeleteAsync($"compute/{id}", cancellationToken);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
            }
        }

        private HttpClient CreateClient(string nodeUrl)
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            client.BaseAddress = new Uri(nodeUrl.EndsWith('/') ? nodeUrl : nodeUrl + "/");
            return client;
        }
    }
}
