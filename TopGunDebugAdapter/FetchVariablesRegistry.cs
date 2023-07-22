using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;

namespace TopGun.DebugAdapter;

internal delegate Task<Variable[]> VariableFetcher(int index, int count, CancellationToken cancel);

internal class FetchVariablesRegistry
{
    private readonly ScummVMConsoleAPI api;
    private readonly SceneInfoLoader sceneInfoLoader;
    private readonly PauseService pauseService;
    private readonly Dictionary<long, VariableFetcher> variableFetchers = new();
    private long nextVariableRef = 1;

    public FetchVariablesRegistry(PauseService pauseService, ScummVMConsoleAPI api, SceneInfoLoader sceneInfoLoader)
    {
        this.pauseService = pauseService;
        this.api = api;
        this.sceneInfoLoader = sceneInfoLoader;
        pauseService.OnIsPausedChanged += HandlePauseChanged;
    }

    private void HandlePauseChanged(bool isPaused)
    {
        if (!isPaused)
            variableFetchers.Clear();
    }

    public long AddVariableFetcher(VariableFetcher fetcher)
    {
        var refId = nextVariableRef++;
        variableFetchers.Add(refId, fetcher);
        return refId;
    }

    public Task<Variable[]> Fetch(long refId, long? index, long? count, CancellationToken cancel)
    {
        return variableFetchers[refId]((int)(index ?? 0), (int)(count ?? int.MaxValue), cancel);
    }

    public long AddLocalsFetcher(int frameId, int scriptIndex) => AddVariableFetcher(async (index, count, cancel) =>
    {
        var sceneInfo = await sceneInfoLoader.LoadCurrentSceneInfo(cancel);
        var scriptSymbolMap = sceneInfo.SymbolMap?.Scripts?.GetValueOrDefault(scriptIndex);

        var locals = await api.LocalVariables(frameId, cancel);
        index = Math.Max(0, index);
        count = Math.Min(count, locals.Count - index);
        return locals
            .Skip(index)
            .Take(count)
            .Select(kv => new Variable()
            {
                Name = scriptSymbolMap?.Locals.GetValueOrDefault(kv.Key) ?? $"local{kv.Key}",
                Value = kv.Value.ToString()
            }).ToArray();
    });    

    public long AddSceneRefFetcher(SceneInfo sceneInfo, IEnumerable<int> vars) => AddGlobalRefFetcher(
        api.SceneVariables, sceneInfo.SymbolMap?.SceneVariables, "scene", vars);

    public long AddSystemRefFetcher(SceneInfo sceneInfo, IEnumerable<int> vars) => AddGlobalRefFetcher(
        api.SystemVariables, sceneInfo.SymbolMap?.SystemVariables, "system", vars);

    private delegate Task<IReadOnlyDictionary<int, int>> RangeFetcher(int offset, int count, CancellationToken cancel);
    private long AddGlobalRefFetcher(
        RangeFetcher fetchRange,
        IReadOnlyDictionary<int, string>? symbolMap,
        string fallbackPrefix,
        IEnumerable<int> vars) => AddVariableFetcher(async (index, count, cancel) =>
    {
        var varValues = new SortedDictionary<int, int>();
        foreach (var (rangeIndex, rangeCount) in FindRanges(vars, index, count))
        {
            var range = await fetchRange(rangeIndex, rangeCount, cancel);
            foreach (var kv in range)
                varValues[kv.Key] = kv.Value;
        }

        var sceneInfo = await sceneInfoLoader.LoadCurrentSceneInfo(cancel);
        return varValues.Select(kv => new Variable()
        {
            Name = symbolMap?.GetValueOrDefault(kv.Key) ?? $"{fallbackPrefix}{kv.Key}",
            Value = kv.Value.ToString()
        }).ToArray();
    });

    private static IEnumerable<(int index, int count)> FindRanges(IEnumerable<int> vars, int index, int count)
    {
        index = Math.Max(0, index);
        count = Math.Min(count, vars.Count() - index);
        var varsLeft = vars.Skip(index).Take(count).Order().ToArray();
        for (int i = 0; i < varsLeft.Length;)
        {
            int rangeCount = 1;
            for (; i + rangeCount < varsLeft.Length; rangeCount++)
            {
                if (varsLeft[i + rangeCount - 1] + 1 != varsLeft[i + rangeCount])
                    break;
            }
            yield return (varsLeft[i], rangeCount);
            i += rangeCount;
        }
    }
}
