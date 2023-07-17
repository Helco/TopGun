using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.DebugAdapter.Protocol.Events;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace TopGun.DebugAdapter.Handlers;

internal class AttachHandler : BaseHandler<AttachHandler>, IAttachHandler
{
    private readonly ScummVMConsoleClient client;

    public AttachHandler(IServiceProvider serviceProvider, ScummVMConsoleClient client) : base(serviceProvider)
    {
        this.client = client;
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
            if (options.StopOnEntry)
                PauseHandler.SendPauseByCommand();
            else
                await api.Continue(CancellationToken.None);
        });
        return new();
    }
}
