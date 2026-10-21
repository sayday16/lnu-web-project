using System.Threading.Channels;

namespace LNU_Web.Services
{
    public class JobQueue
    {
        private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>();

        public void Enqueue(Guid jobId)
        {
            _channel.Writer.TryWrite(jobId);
        }

        public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken)
        {
            return _channel.Reader.ReadAllAsync(cancellationToken);
        }
    }
}
