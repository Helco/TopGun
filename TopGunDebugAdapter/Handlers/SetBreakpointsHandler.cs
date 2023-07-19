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
    private readonly BreakpointMapper breakpointMapper;

    public SetBreakpointsHandler(IServiceProvider serviceProvider, SceneInfoLoader sceneInfoLoader, BreakpointMapper breakpointMapper) : base(serviceProvider)
    {
        this.sceneInfoLoader = sceneInfoLoader;
        this.breakpointMapper = breakpointMapper;
    }

    public async Task<SetBreakpointsResponse> Handle(SetBreakpointsArguments request, CancellationToken cancellationToken)
    {
        var sceneInfo = await sceneInfoLoader.TryLoadSceneInfoAtPath(request.Source.Path ?? "", cancellationToken);
        if (sceneInfo == null)
            return new() { Breakpoints = Array.Empty<Breakpoint>() };

        var textPositions = request.Breakpoints?
            .Select(bp => Server.ClientSettings.AdjustForMe(bp.Line, bp.Column))
            .ToArray() ?? Array.Empty<TextPosition>();
        var forSceneBps = await breakpointMapper.SetAllBreakpointsForScene(sceneInfo, textPositions, cancellationToken);
        await pauseService.ContinueScummDueToCommand(cancellationToken);

        return new()
        {
            Breakpoints = forSceneBps
                .Select((bp, i) => bp.HasValue ? new Breakpoint()
                {
                    Source = sceneInfo.Decompiled,
                    Line = Server.ClientSettings.AdjustForThem(bp.Value.TextPosition).Line,
                    Column = Server.ClientSettings.AdjustForThem(bp.Value.TextPosition).Column,
                    Id = bp.Value.DapId,
                    Verified = true
                } : new Breakpoint()
                {
                    Source = sceneInfo.Decompiled,
                    Line = textPositions[i].Line,
                    Column = textPositions[i].Column,
                    Id = -1,
                    Verified = false
                }).ToArray()
        };
    }


}
