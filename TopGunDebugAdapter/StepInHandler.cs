using System;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace TopGun.DebugAdapter;

internal class StepInHandler : BaseStepHandler<StepInHandler>, IStepInHandler
{
    public StepInHandler(IServiceProvider serviceProvider) : base(serviceProvider)
    {
    }

    public async Task<StepInResponse> Handle(StepInArguments request, CancellationToken cancellationToken)
    {
        ListenForStepMessage();
        await api.Step(cancellationToken);
        return new();
    }
}
