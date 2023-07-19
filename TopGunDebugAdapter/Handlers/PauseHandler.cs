using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.DebugAdapter.Protocol.Events;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace TopGun.DebugAdapter.Handlers;

internal class PauseHandler : BaseHandler<PauseHandler>, IPauseHandler
{
    

    public PauseHandler(IServiceProvider serviceProvider) : base(serviceProvider)
    {
    }

    public async Task<PauseResponse> Handle(PauseArguments request, CancellationToken cancellationToken)
    {
        await api.Pause(cancellationToken);
        pauseService.SendPauseBy(StoppedEventReason.Pause);
        return new();
    }
}
