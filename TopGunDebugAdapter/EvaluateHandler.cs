using System;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace TopGun.DebugAdapter;

internal class EvaluateHandler : BaseHandler<EvaluateHandler>, IEvaluateHandler
{
    private readonly ScummVMConsoleClient client;

    protected EvaluateHandler(IServiceProvider serviceProvider, ScummVMConsoleClient client) : base(serviceProvider)
    {
        this.client = client;
    }

    public async Task<EvaluateResponse> Handle(EvaluateArguments request, CancellationToken cancellationToken)
    {
        if (request.Context != EvaluateArgumentsContext.Repl)
            throw new NotSupportedException("Only REPL evaluate is currently supported");

        var response = await client.SendCommand(request.Expression, cancellationToken);
        return new()
        {
            Result = string.Join("\n", response)
        };
    }
}
