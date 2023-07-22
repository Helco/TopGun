using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace TopGun.DebugAdapter.Handlers;

internal class VariablesHandler : BaseHandler<VariablesHandler>, IVariablesHandler
{
    private readonly FetchVariablesRegistry fetchVariablesRegistry;

    public VariablesHandler(IServiceProvider serviceProvider, FetchVariablesRegistry fetchVariablesRegistry) : base(serviceProvider)
    {
        this.fetchVariablesRegistry = fetchVariablesRegistry;
    }

    public async Task<VariablesResponse> Handle(VariablesArguments request, CancellationToken cancellationToken)
    {
        var variables = await fetchVariablesRegistry.Fetch(
            request.VariablesReference,
            request.Start,
            request.Count,
            cancellationToken);
        return new() { Variables = variables };
    }
}
