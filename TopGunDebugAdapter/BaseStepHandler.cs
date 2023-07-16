﻿using System;
using System.Collections.Generic;
using OmniSharp.Extensions.DebugAdapter.Protocol.Events;

namespace TopGun.DebugAdapter;

internal class BaseStepHandler<T> : BaseHandler<T> where T : BaseHandler<T>
{
    protected BaseStepHandler(IServiceProvider serviceProvider) : base(serviceProvider)
    {
    }

    protected void ListenForStepMessage()
    {
        api.AddOnceMessageHandler(HandleMessage);
    }

    private bool HandleMessage(IReadOnlyList<string> message)
    {
        if (message.Count != 0)
            return false;
        PauseHandler.SendPauseBy(StoppedEventReason.Step);
        return true;
    }
}
