using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.DebugAdapter.Server;

namespace TopGun.DebugAdapter;

internal class BaseHandler<T> where T : BaseHandler<T>
{
    protected readonly ILogger<ScummVMConsoleClient> logger;
    protected readonly DebugAdapterOptions options;
    protected readonly ScummVMConsoleAPI api;
    private readonly Lazy<DebugAdapterServer> lazyServer;
    private readonly Lazy<PauseHandler> lazyPauseHandler;

    protected PauseHandler PauseHandler => lazyPauseHandler.Value;
    protected DebugAdapterServer Server => lazyServer.Value;

    protected BaseHandler(IServiceProvider serviceProvider)
    {
        //logger = serviceProvider.GetRequiredService<ILogger<ScummVMConsoleClient>>();
        logger = serviceProvider.GetRequiredService<ScummVMConsoleClient>().logger; // how about a logging framework that actually logs or provides some info on why it does not log?!
        options = serviceProvider.GetRequiredService<DebugAdapterOptions>();
        api = serviceProvider.GetRequiredService<ScummVMConsoleAPI>();
        lazyServer = new Lazy<DebugAdapterServer>(serviceProvider.GetRequiredService<DebugAdapterServer>);
        lazyPauseHandler = new Lazy<PauseHandler>(serviceProvider.GetRequiredService<PauseHandler>);
    }
}
