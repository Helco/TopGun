using System;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace TopGun.DebugAdapter.Handlers;

internal class NextHandler : BaseStepHandler<NextHandler>, INextHandler
{
    public NextHandler(IServiceProvider serviceProvider) : base(serviceProvider)
    {
    }

    public async Task<NextResponse> Handle(NextArguments request, CancellationToken cancellationToken)
    {
        ListenForStepMessage();
        if (request.Granularity == SteppingGranularity.Instruction)
            await api.Step(cancellationToken);
        else
            await api.StepOver(cancellationToken);
        return new();
    }
}
