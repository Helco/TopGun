using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace TopGun.DebugAdapter;

internal class AttachHandler : IAttachHandler
{
    private readonly DebugAdapterOptions options;
    private readonly ILogger<AttachHandler> logger;
    private readonly ScummVMConsoleClient client;
    private readonly ScummVMConsoleAPI api;

    public AttachHandler(DebugAdapterOptions options, ILogger<AttachHandler> logger, ScummVMConsoleClient client, ScummVMConsoleAPI api)
    {
        this.options = options;
        this.logger = logger;
        this.client = client;
        this.api = api;
    }

    public async Task<AttachResponse> Handle(AttachRequestArguments request, CancellationToken cancellationToken)
    {
        await client.Connect(options.EngineHost, options.EnginePort, cancellationToken);
        _ = Task.Run(async () =>
        {
            await Task.Delay(100);
            await api.ListPoints(CancellationToken.None);
            // TODO: Send points to client

            // by stopping and continueing as needed we know the state of ScummVM without requesting (which would stop again)
            if (!options.StopOnEntry)
                await api.Continue(CancellationToken.None);
        });
        return new();
    }
}
