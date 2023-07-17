using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace TopGun.DebugAdapter.Handlers;

internal class SetBreakpointsHandler : BaseHandler<SetBreakpointsHandler>, ISetBreakpointsHandler
{
    private readonly SceneInfoLoader sceneInfoLoader;

    public SetBreakpointsHandler(IServiceProvider serviceProvider, SceneInfoLoader sceneInfoLoader) : base(serviceProvider)
    {
        this.sceneInfoLoader = sceneInfoLoader;
    }

    public async Task<SetBreakpointsResponse> Handle(SetBreakpointsArguments request, CancellationToken cancellationToken)
    {
        var sceneInfo = await sceneInfoLoader.TryLoadSceneInfoAtPath(request.Source.Path ?? "", cancellationToken);
        if (sceneInfo == null)
            return new() { Breakpoints = Array.Empty<Breakpoint>() };

        var setBreakpoints = new List<Breakpoint>();
        foreach (var sourceBp in request.Breakpoints)
        {
            var setBp = await TrySetSourceBreakpoint(sourceBp, cancellationToken);
            if (setBp != null)
                setBreakpoints.Add(setBp);
        }
        return new()
        {
            Breakpoints = setBreakpoints,
        };
    }


}
