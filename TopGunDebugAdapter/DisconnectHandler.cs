using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace TopGun.DebugAdapter;

internal class DisconnectHandler : IDisconnectHandler
{
    private readonly TaskCompletionSource completionSource = new();

    public Task DisconnectTask => completionSource.Task;

    public Task<DisconnectResponse> Handle(DisconnectArguments request, CancellationToken cancellationToken)
    {
        completionSource.TrySetResult();
        return Task.FromResult(new DisconnectResponse());
    }
}
