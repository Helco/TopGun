using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.DebugAdapter.Protocol.Events;
using OmniSharp.Extensions.DebugAdapter.Server;
using TopGun.DebugAdapter.Handlers;

namespace TopGun.DebugAdapter;

internal readonly record struct BreakpointForScene(string SceneName, int DapId, TextPosition TextPosition);

internal class MappedBreakpoint
{
    public MappedBreakpoint(ScummVMPoint scummPoint)
    {
        ScummPoint = scummPoint;
    }

    public ScummVMPoint ScummPoint { get; }
    public Dictionary<string, BreakpointForScene> RelevantScenes { get; } = new();

    public BreakpointForScene? GetForScene(string sceneName) => RelevantScenes.TryGetValue(sceneName, out var forScene)
        ? forScene
        : null;
}

internal partial class BreakpointMapper
{
    private readonly ScummVMConsoleAPI api;
    private readonly SceneInfoLoader sceneInfoLoader;
    private readonly PauseService pauseService;
    private readonly ILogger<BreakpointMapper> logger;
    private readonly SemaphoreSlim semaphore = new(1, 1);

    private readonly HashSet<MappedBreakpoint> breakpoints = new();

    private int nextDapId = 1;

    public DebugAdapterServer Server => sceneInfoLoader.Server;

    public BreakpointMapper(ScummVMConsoleAPI api, SceneInfoLoader sceneInfoLoader, PauseService pauseService, ILogger<BreakpointMapper> logger)
    {
        this.api = api;
        this.sceneInfoLoader = sceneInfoLoader;
        this.pauseService = pauseService;
        this.logger = logger;

        api.AddAlwaysMessageHandler(HandleBreakpointReached);
    }

    private MappedBreakpoint? ByScummId(int scummId) => breakpoints.FirstOrDefault(bp => bp.ScummPoint.Id == scummId);
    private IEnumerable<MappedBreakpoint> ByScene(string sceneName) => breakpoints.Where(bp => bp.RelevantScenes.ContainsKey(sceneName));
    private MappedBreakpoint? BySceneAndTextPos(string sceneName, TextPosition textPosition) => ByScene(sceneName)
        .Select(bp => bp.RelevantScenes[sceneName].TextPosition == textPosition ? bp : null)
        .FirstOrDefault(bp => bp != null);

    public async Task<BreakpointForScene?[]> SetAllBreakpointsForScene(SceneInfo sceneInfo, TextPosition[] textPositions, CancellationToken cancel)
    {
        if (sceneInfo.DebugInfo == null)
            return new BreakpointForScene?[textPositions.Length];

        await semaphore.WaitAsync(cancel);
        try
        {
            var mappedBps = textPositions.Select(tp => BySceneAndTextPos(sceneInfo.Name, tp)).ToArray();
            for (int i = 0; i < textPositions.Length; i++)
                mappedBps[i] ??= await UnsafeSetNewBreakpoint(sceneInfo, textPositions[i], cancel);

            var toBeDeleted = ByScene(sceneInfo.Name).Except(mappedBps).ToArray();
            foreach (var mapped in toBeDeleted)
                await UnsafeDeleteBreakpointForScene(sceneInfo, mapped!, cancel);

            return mappedBps.Select(bp => bp?.GetForScene(sceneInfo.Name)).ToArray();
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task<MappedBreakpoint?> UnsafeSetNewBreakpoint(SceneInfo sceneInfo, TextPosition textPosition, CancellationToken cancel)
    {
        var scriptPos = sceneInfo.FindScript(textPosition);
        if (scriptPos == null)
            return null;
        var (scriptIndex, scriptOffset) = scriptPos.Value;

        var mapped = breakpoints.FirstOrDefault(b =>
                b.ScummPoint.Type == ScummVMPointType.Script &&
                b.ScummPoint.Breaks == true &&
                b.ScummPoint.Index == scriptIndex &&
                b.ScummPoint.Offset == scriptOffset);
        if (mapped == null)
        {
            var scummPoint = await api.AddBreakpoint(ScummVMPointType.Script, scriptIndex, scriptOffset, cancel);
            mapped = new MappedBreakpoint(scummPoint);
            breakpoints.Add(mapped);
        }

        var forScene = new BreakpointForScene(sceneInfo.Name, nextDapId++, textPosition);
        mapped.RelevantScenes.Add(sceneInfo.Name, forScene);
        return mapped;
    }

    private async Task UnsafeDeleteBreakpointForScene(SceneInfo sceneInfo, MappedBreakpoint mapped, CancellationToken cancel)
    {
        var forScene = mapped.GetForScene(sceneInfo.Name);
        if (forScene == null)
            return;

        mapped.RelevantScenes.Remove(sceneInfo.Name);
        if (mapped.RelevantScenes.Count == 0)
        {
            await api.DeletePoint(mapped.ScummPoint.Id, cancel);
            breakpoints.Remove(mapped);
        }
    }

    [GeneratedRegex(@"^break point (\d+) reached:")]
    private partial Regex PatternBreakpointReached();
    private bool HandleBreakpointReached(IReadOnlyList<string> message)
    {
        if (message.Count != 1) return false;
        var match = PatternBreakpointReached().Match(message.Single());
        if (!match.Success) return false;
        var scummId = int.Parse(match.Groups[1].Value);

        _ = Task.Run(async () =>
        {
            await semaphore.WaitAsync();
            try
            {
                var mapped = ByScummId(scummId);
                // Omnisharp currently does not have a way of sending breakpoint id
                pauseService.SendPauseBy(StoppedEventReason.Breakpoint);
            }
            finally
            {
                semaphore.Release();
            }
        });
        return true;
    }
}
