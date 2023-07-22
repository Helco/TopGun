using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace TopGun.DebugAdapter.Handlers;

internal class VariablesHandler : BaseHandler<VariablesHandler>, IVariablesHandler
{
    private readonly SceneInfoLoader sceneInfoLoader;

    public VariablesHandler(IServiceProvider serviceProvider, SceneInfoLoader sceneInfoLoader) : base(serviceProvider)
    {
        this.sceneInfoLoader = sceneInfoLoader;
    }

    public async Task<VariablesResponse> Handle(VariablesArguments request, CancellationToken cancellationToken)
    {
        var sceneInfo = await sceneInfoLoader.LoadCurrentSceneInfo(cancellationToken);
        return await HandleLocalVariables(sceneInfo, request, cancellationToken);
    }

    private async Task<VariablesResponse> HandleLocalVariables(SceneInfo sceneInfo, VariablesArguments request, CancellationToken cancellationToken)
    {
        if (request.Filter == VariablesArgumentsFilter.Indexed)
            return new();
        var stacktrace = await api.Stacktrace(cancellationToken);
        if (request.VariablesReference < 1 || request.VariablesReference > stacktrace.Count)
            return new();
        var frame = stacktrace[(int)request.VariablesReference - 1]; // 1-based index to prevent 0 being ignored
        if (frame.Type is not (ScummVMCallType.Root or ScummVMCallType.Calc) || frame.Locals == 0)
            return new();

        var index = Math.Max(0, request.Start ?? 0);
        var count = Math.Min(request.Count ?? long.MaxValue, frame.Locals - index);
        var scriptSymbolMap = sceneInfo.SymbolMap?.Scripts?.GetValueOrDefault(frame.Index);
        var locals = await api.LocalVariables(frame.Id, cancellationToken);
        return new()
        {
            Variables = Enumerable
                .Range((int)index, (int)count)
                .Where(locals.ContainsKey)
                .Select(i => new Variable()
                {
                    Name = scriptSymbolMap?.Locals.GetValueOrDefault(i) ?? $"local{i}",
                    Value = locals[i].ToString()
                }).ToArray()
        };
    }
}
