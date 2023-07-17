using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.DebugAdapter.Protocol.Events;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace TopGun.DebugAdapter.Handlers;

internal class PauseHandler : BaseHandler<PauseHandler>, IPauseHandler
{
    private bool isPaused = false;

    public PauseHandler(IServiceProvider serviceProvider) : base(serviceProvider)
    {
    }

    public async Task<PauseResponse> Handle(PauseArguments request, CancellationToken cancellationToken)
    {
        await api.Pause(cancellationToken);
        SendPauseBy(StoppedEventReason.Pause);
        return new();
    }

    public void SendContinueIfNecessary()
    {
        if (!isPaused)
            return;
        isPaused = false;
        Server.SendContinued(new()
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
        Server.SendStopped(new()
        {
            AllThreadsStopped = false,
            Reason = reason,
            PreserveFocusHint = false,
            ThreadId = 0
        });
        logger.LogTrace("Send pause by " + reason);
    }

    public void ContinueWithoutEvent() => isPaused = false;
}
