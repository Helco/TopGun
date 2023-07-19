using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.DebugAdapter.Server;

namespace TopGun.DebugAdapter.Handlers;

internal class BaseHandler<T> where T : BaseHandler<T>
{
    protected readonly ILogger<ScummVMConsoleClient> logger;
    protected readonly DebugAdapterOptions options;
    protected readonly ScummVMConsoleAPI api;
    protected readonly PauseService pauseService;
    private readonly Lazy<DebugAdapterServer> lazyServer;
    protected DebugAdapterServer Server => lazyServer.Value;

    protected BaseHandler(IServiceProvider serviceProvider)
    {
        //logger = serviceProvider.GetRequiredService<ILogger<ScummVMConsoleClient>>();
        logger = serviceProvider.GetRequiredService<ScummVMConsoleClient>().logger; // how about a logging framework that actually logs or provides some info on why it does not log?!
        options = serviceProvider.GetRequiredService<DebugAdapterOptions>();
        api = serviceProvider.GetRequiredService<ScummVMConsoleAPI>();
        pauseService = serviceProvider.GetRequiredService<PauseService>();
        lazyServer = new Lazy<DebugAdapterServer>(serviceProvider.GetRequiredService<DebugAdapterServer>);
    }
}
