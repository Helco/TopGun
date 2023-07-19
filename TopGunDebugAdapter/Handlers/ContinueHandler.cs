using System;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace TopGun.DebugAdapter.Handlers;

internal class ContinueHandler : BaseHandler<ContinueHandler>, IContinueHandler
{
    public ContinueHandler(IServiceProvider serviceProvider) : base(serviceProvider)
    {
    }

    public async Task<ContinueResponse> Handle(ContinueArguments request, CancellationToken cancellationToken)
    {
        await api.Continue(cancellationToken);
        pauseService.ContinueWithoutEvent();
        return new()
        {
            AllThreadsContinued = true
        };
    }
}
