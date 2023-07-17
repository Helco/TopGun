using System;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace TopGun.DebugAdapter.Handlers;

internal class StepOutHandler : BaseStepHandler<StepOutHandler>, IStepOutHandler
{
    public StepOutHandler(IServiceProvider serviceProvider) : base(serviceProvider)
    {
    }

    public async Task<StepOutResponse> Handle(StepOutArguments request, CancellationToken cancellationToken)
    {
        ListenForStepMessage();
        await api.StepOut(cancellationToken);
        return new();
    }
}
