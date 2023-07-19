using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.DebugAdapter.Protocol.Events;
using OmniSharp.Extensions.DebugAdapter.Server;

namespace TopGun.DebugAdapter;

internal class PauseService
{
    private readonly ScummVMConsoleAPI api;
    private readonly ILogger<PauseService> logger;
    private readonly Lazy<DebugAdapterServer> server;

    public PauseService(Lazy<DebugAdapterServer> server, ILogger<PauseService> logger, ScummVMConsoleAPI api)
    {
        this.server = server;
        this.logger = logger;
        this.api = api;
    }

    private bool isPaused = false;

    public void SendContinueIfNecessary()
    {
        if (!isPaused)
            return;
        isPaused = false;
        server.Value.SendContinued(new()
        {
            AllThreadsContinued = false,
            ThreadId = 0
        });
        logger.LogTrace("Send continue");
    }

    public void SendPauseByCommand()
    {
        if (isPaused)
            return;
        isPaused = true;
        SendPauseBy(StoppedEventReason.Entry);
    }

    public void SendPauseBy(StoppedEventReason reason)
    {
        isPaused = true;
        server.Value.SendStopped(new()
        {
            AllThreadsStopped = false,
            Reason = reason,
            PreserveFocusHint = false,
            ThreadId = 0
        });
        logger.LogTrace("Send pause by " + reason);
    }

    public async Task ContinueScummDueToCommand(CancellationToken cancel)
    {
        if (isPaused)
            return; // if we are paused we probably want to keep it that way
        await api.Continue(cancel);
        logger.LogTrace("Continue ScummVM due to command that should not break");
    }

    public void ContinueWithoutEvent() => isPaused = false;
}
