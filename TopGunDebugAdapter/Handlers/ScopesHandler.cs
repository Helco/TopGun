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
    private readonly FetchVariablesRegistry fetchVariablesRegistry;

    public ScopesHandler(IServiceProvider serviceProvider, SceneInfoLoader sceneInfoLoader, FetchVariablesRegistry fetchVariablesRegistry) : base(serviceProvider)
    {
        this.sceneInfoLoader = sceneInfoLoader;
        this.fetchVariablesRegistry = fetchVariablesRegistry;
    }

    public async Task<ScopesResponse> Handle(ScopesArguments request, CancellationToken cancellationToken)
    {
        var sceneInfo = await sceneInfoLoader.LoadCurrentSceneInfo(cancellationToken);
        var stacktrace = await api.Stacktrace(cancellationToken);
        if (request.FrameId < 0 || request.FrameId >= stacktrace.Count)
            return new();
        var frame = stacktrace[(int)request.FrameId];
        var scriptDebugInfo = sceneInfo.DebugInfo?.Scripts?.GetValueOrDefault(frame.Index);

        var scopes = new List<Scope>(3);
        if (frame.Type is ScummVMCallType.Root or ScummVMCallType.Calc && frame.Locals > 0)
        {
            var localScopeRefId = fetchVariablesRegistry.AddLocalsFetcher(frame.Id, frame.Index);
            scopes.Add(new Scope()
            {
                Name = "Locals",
                PresentationHint = "locals",
                VariablesReference = localScopeRefId,
                NamedVariables = frame.Locals,
                Expensive = false
            });
        }
        if (scriptDebugInfo?.SceneVarRefs.Count > 0)
        {
            var sceneScopeRefId = fetchVariablesRegistry.AddSceneRefFetcher(sceneInfo, scriptDebugInfo.SceneVarRefs);
            scopes.Add(new Scope()
            {
                Name = "Scene",
                PresentationHint = "globals",
                VariablesReference = sceneScopeRefId,
                NamedVariables = scriptDebugInfo.SceneVarRefs.Count
            });
        }
        if (scriptDebugInfo?.SystemVarRefs.Count > 0)
        {
            var systemScopeRefId = fetchVariablesRegistry.AddSystemRefFetcher(sceneInfo, scriptDebugInfo.SystemVarRefs);
            scopes.Add(new Scope()
            {
                Name = "System",
                PresentationHint = "globals",
                VariablesReference = systemScopeRefId,
                NamedVariables = scriptDebugInfo.SystemVarRefs.Count
            });
        }

        return new()
        {
            Scopes = scopes
        };
    }
}
