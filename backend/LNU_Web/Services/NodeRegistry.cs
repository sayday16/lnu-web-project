using System.Collections.Concurrent;
using LNU_Web.Options;
using Microsoft.Extensions.Options;

namespace LNU_Web.Services
{
    public record NodeState(
        string Id,
        string Url,
        bool Reachable,
        int RunningJobs,
        int MaxConcurrentJobs,
        DateTime? LastSeenAt)
    {
        public int FreeSlots => Reachable ? Math.Max(0, MaxConcurrentJobs - RunningJobs) : 0;
    }

    public class NodeRegistry
    {
        private readonly ConcurrentDictionary<string, NodeState> _nodes = new();
        private readonly List<string> _order;
        private int _roundRobin;

        public NodeRegistry(IOptions<NodesOptions> options)
        {
            _order = options.Value.Endpoints.Select(e => e.Id).ToList();
            foreach (var endpoint in options.Value.Endpoints)
            {
                _nodes[endpoint.Id] = new NodeState(endpoint.Id, endpoint.Url, false, 0, 0, null);
            }
        }

        public IReadOnlyList<NodeState> GetAll()
        {
            return _order.Select(id => _nodes[id]).ToList();
        }

        public NodeState? Find(string? id)
        {
            return id == null ? null : _nodes.GetValueOrDefault(id);
        }

        public void Update(NodeState state)
        {
            _nodes[state.Id] = state;
        }

        public void MarkStarted(string id)
        {
            _nodes.AddOrUpdate(id, _ => throw new KeyNotFoundException(id), (_, node) => node with { RunningJobs = node.RunningJobs + 1 });
        }

        public void MarkFull(string id)
        {
            _nodes.AddOrUpdate(id, _ => throw new KeyNotFoundException(id), (_, node) => node with { RunningJobs = node.MaxConcurrentJobs });
        }

        public void MarkUnreachable(string id)
        {
            _nodes.AddOrUpdate(id, _ => throw new KeyNotFoundException(id), (_, node) => node with { Reachable = false });
        }

        public NodeState? PickLeastLoaded()
        {
            var candidates = GetAll().Where(n => n.FreeSlots > 0).ToList();
            if (candidates.Count == 0)
            {
                return null;
            }

            var minLoad = candidates.Min(Load);
            var leastLoaded = candidates.Where(n => Load(n) <= minLoad).ToList();
            var index = (int)((uint)Interlocked.Increment(ref _roundRobin) % (uint)leastLoaded.Count);
            return leastLoaded[index];
        }

        private static double Load(NodeState node)
        {
            return (double)node.RunningJobs / Math.Max(1, node.MaxConcurrentJobs);
        }
    }
}
