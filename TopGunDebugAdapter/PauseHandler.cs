using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace TopGun.DebugAdapter;

internal class PauseHandler : IPauseHandler
{
    private readonly ScummVMConsoleAPI api;

    public PauseHandler(ScummVMConsoleAPI api)
    {
        this.api = api;
    }

    public async Task<PauseResponse> Handle(PauseArguments request, CancellationToken cancellationToken)
    {
        await api.Pause(cancellationToken);
        return new();
    }
}
