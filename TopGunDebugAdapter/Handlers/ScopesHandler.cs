using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace TopGun.DebugAdapter.Handlers;

internal class ScopesHandler : BaseHandler<ScopesHandler>, IScopesHandler
{
    private readonly SceneInfoLoader sceneInfoLoader;

    public ScopesHandler(IServiceProvider serviceProvider, SceneInfoLoader sceneInfoLoader) : base(serviceProvider)
    {
        this.sceneInfoLoader = sceneInfoLoader;
    }

    public async Task<ScopesResponse> Handle(ScopesArguments request, CancellationToken cancellationToken)
    {
        var sceneInfo = await sceneInfoLoader.LoadCurrentSceneInfo(cancellationToken);
        var gameInfo = await api.GameInfo(cancellationToken);

        var stacktrace = await api.Stacktrace(cancellationToken);
        if (request.FrameId < 0 || request.FrameId >= stacktrace.Count)
            return new();

        Scope? localScope = null;
        var frame = stacktrace[(int)request.FrameId];
        if (frame.Type is ScummVMCallType.Root or ScummVMCallType.Calc && frame.Locals > 0)
        {
            localScope = new Scope()
            {
                Name = "Locals",
                PresentationHint = "locals",
                VariablesReference = frame.Id + 1,
                NamedVariables = frame.Locals,
                Expensive = false
            };
        }

        return new()
        {
            Scopes = new[]
            {
                localScope
            }.Where(s => s != null).ToArray()!
        };
    }
}
