using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace TopGun.DebugAdapter;

internal class ContinueHandler : IContinueHandler
{
    private readonly ScummVMConsoleAPI api;

    public ContinueHandler(ScummVMConsoleAPI api)
    {
        this.api = api;
    }

    public async Task<ContinueResponse> Handle(ContinueArguments request, CancellationToken cancellationToken)
    {
        await api.Continue(cancellationToken);
        return new()
        {
            AllThreadsContinued = true
        };
    }
}
