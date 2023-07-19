using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.DebugAdapter.Protocol.Events;

namespace TopGun.DebugAdapter;

internal class UnknownConsoleMessageException : Exception
{
    public string? Command { get; }
    public IReadOnlyList<string> ConsoleMessage { get; }

    public UnknownConsoleMessageException(string? command, IReadOnlyList<string> message) : base(string.Join("\n", message.Prepend("Unknown message from ScummVM console:")))
    {
        Command = command;
        ConsoleMessage = message;
    }
}

internal enum ScummVMPointType
{
    Script,
    Procedure,
    VariableRead,
    VariableWrite,
    VariableAccess,
    ResourceLoad,
    ResourceAccess,
    SceneChanging,
    SceneChanged
}

internal enum ScummVMCallType
{
    Root,
    Calc,
    Proc
}

internal readonly record struct ScummVMPoint(int Id, bool Breaks, ScummVMPointType Type, int Index, int Offset);

internal readonly record struct ScummVMFrame(int Id, ScummVMCallType Type, int Index, int Offset, int Args, int Locals);

internal partial class ScummVMConsoleAPI
{
    private static readonly IReadOnlyDictionary<ScummVMPointType, string> pointTypeToString = new Dictionary<ScummVMPointType, string>()
    {
        { ScummVMPointType.Script, "script" },
        { ScummVMPointType.Procedure, "procedure" },
        { ScummVMPointType.VariableRead, "variable-read" },
        { ScummVMPointType.VariableWrite, "variable-write" },
        { ScummVMPointType.VariableAccess, "variable-access" },
        { ScummVMPointType.ResourceLoad, "resource-load" },
        { ScummVMPointType.ResourceAccess, "resource-access" },
        { ScummVMPointType.SceneChanging, "scene-changing" },
        { ScummVMPointType.SceneChanged, "scene-changed" }
    };
    private static readonly IReadOnlyDictionary<string, ScummVMPointType> stringToPointType =
        pointTypeToString.ToDictionary(kv => kv.Value, kv => kv.Key);

    private static readonly IReadOnlyDictionary<ScummVMCallType, string> callTypeToString = new Dictionary<ScummVMCallType, string>()
    {
        { ScummVMCallType.Root, "root" },
        { ScummVMCallType.Calc, "calc" },
        { ScummVMCallType.Proc, "proc" }
    };
    private static readonly IReadOnlyDictionary<string, ScummVMCallType> stringToCallType =
        callTypeToString.ToDictionary(kv => kv.Value, kv => kv.Key);

    private readonly ScummVMConsoleClient client;
    private readonly List<Predicate<IReadOnlyList<string>>> onceMessageHandlers = new();
    private readonly List<Predicate<IReadOnlyList<string>>> alwaysMessageHandlers = new();

    public ScummVMConsoleAPI(ScummVMConsoleClient client)
    {
        this.client = client;
        client.OnMessage += HandleMessage;
    }

    public void AddOnceMessageHandler(Predicate<IReadOnlyList<string>> handler) =>
        onceMessageHandlers.Add(handler);

    public void AddAlwaysMessageHandler(Predicate<IReadOnlyList<string>> handler) =>
        alwaysMessageHandlers.Add(handler);

    public Predicate<IReadOnlyList<string>>? LastMessageHandler { get; set; }

    private void HandleMessage(IReadOnlyList<string> message)
    {
        for (int i = 0; i < onceMessageHandlers.Count; i++)
        {
            if (onceMessageHandlers[i](message))
            {
                onceMessageHandlers.RemoveAt(i);
                return;
            }
        }

        foreach (var handler in (alwaysMessageHandlers as IEnumerable<Predicate<IReadOnlyList<string>>>).Reverse())
        {
            if (handler(message))
                return;
        }

        if (LastMessageHandler?.Invoke(message) != true)
            throw new UnknownConsoleMessageException(null, message);
    }

    [GeneratedRegex(@"(break|trace) (\d+) created")]
    private static partial Regex PatternPointCreated();
    public async Task<ScummVMPoint> AddBreakpoint(ScummVMPointType type, int index, int offset, CancellationToken cancel)
    {
        var command = $"break {pointTypeToString[type]} {index} {offset}";
        var response = await client.SendCommand(command, cancel);
        if (response.Count != 1)
            throw new UnknownConsoleMessageException(command, response);
        var match = PatternPointCreated().Match(response.Single());
        if (!match.Success)
            throw new UnknownConsoleMessageException(command, response);
        var pointId = int.Parse(match.Groups[2].Value);
        return new(pointId, true, type, index, offset);
    }

    [GeneratedRegex(@"Point \d+ deleted")]
    private static partial Regex PatternPointDeleted();
    public async Task DeletePoint(int id, CancellationToken cancel)
    {
        var command = "delete " + id;
        var response = await client.SendCommand(command, cancel);
        if (response.Count != 1 || !PatternPointDeleted().IsMatch(response.Single()))
            throw new UnknownConsoleMessageException(command, response);
    }

    [GeneratedRegex(@"(\d+): (break for|trace) ([\w-]+) (\d+) @ (\d+)")]
    private static partial Regex PatternListedPoint();
    public async Task<IReadOnlyList<ScummVMPoint>> ListPoints(CancellationToken cancel)
    {
        var command = "list-breaks";
        var response = await client.SendCommand(command, cancel);
        var result = response.Select(line =>
        {
            var match = PatternListedPoint().Match(line);
            if (!match.Success || !stringToPointType.TryGetValue(match.Groups[3].Value, out var pointType))
                throw new UnknownConsoleMessageException(command, response);
            return new ScummVMPoint(
               int.Parse(match.Groups[1].Value),
               match.Groups[2].Value[0] == 'b',
               pointType,
               int.Parse(match.Groups[4].Value),
               int.Parse(match.Groups[5].Value));
        }).ToArray();
        return result;
    }

    [GeneratedRegex(@"\d+: (\w+) (\d+) @ (\d+)(?: (\d+) args)?(?: (\d+) local variables)?")]
    private static partial Regex PatternStackFrame();
    public async Task<IReadOnlyList<ScummVMFrame>> Stacktrace(CancellationToken cancel)
    {
        var command = "stacktrace";
        var response = await client.SendCommand(command, cancel);
        var result = response.Select((line, i) =>
        {
            var match = PatternStackFrame().Match(line);
            if (!match.Success || !stringToCallType.TryGetValue(match.Groups[1].Value, out var callType))
                throw new UnknownConsoleMessageException(command, response);
            return new ScummVMFrame(
                i,
                callType,
                int.Parse(match.Groups[2].Value),
                int.Parse(match.Groups[3].Value),
                match.Groups[4].Success ? int.Parse(match.Groups[3].Value) : 0,
                match.Groups[5].Success ? int.Parse(match.Groups[4].Value) : 0);
        }).ToArray();
        return result;
    }

    [GeneratedRegex(@"^(\> )?(\w+(?:\.bin)?)$", RegexOptions.IgnoreCase)]
    private static partial Regex PatternSceneName();
    public async Task<(IReadOnlyList<string> scenes, int? curSceneI)> SceneStack(CancellationToken cancel)
    {
        var command = "scenestack";
        var response = await client.SendCommand(command, cancel);
        int? curSceneI = null;
        var scenes = response.Select((line, i) =>
        {
            var match = PatternSceneName().Match(line);
            if (!match.Success)
                throw new UnknownConsoleMessageException(command, response);
            if (match.Groups[1].Success)
                curSceneI = i;
            return match.Groups[2].Value;
        }).ToArray();
        return (scenes, curSceneI);
    }

    [GeneratedRegex(@"(\d+) = (\d+)")]
    private static partial Regex PatternVariable();
    private async Task<IReadOnlyDictionary<int, int>> VariableCommand(string command, CancellationToken cancel)
    {
        var response = await client.SendCommand(command, cancel);
        var result = response.Select(line =>
        {
            var match = PatternVariable().Match(line);
            if (!match.Success)
                throw new UnknownConsoleMessageException(command, response);
            return (int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value));
        }).ToDictionary(t => t.Item1, t => t.Item2);
        return result;
    }

    public Task<IReadOnlyDictionary<int, int>> LocalVariables(int scopeIndex, CancellationToken cancel) =>
        VariableCommand($"localVars {scopeIndex}", cancel);

    public Task<IReadOnlyDictionary<int, int>> GlobalVariables(int offset, int count, CancellationToken cancel) =>
        VariableCommand($"globalVars {offset} {count}", cancel);
    
    private async Task SimpleCommandIgnoringOutput(string command, int expectLinesToIgnore, CancellationToken cancel)
    {
        var response = await client.SendCommand(command, cancel);
        if (response.Count != expectLinesToIgnore)
            throw new UnknownConsoleMessageException(command, response);
    }

    public Task DeleteAllPoints(CancellationToken cancel) => SimpleCommandIgnoringOutput("delete-all", 0, cancel);
    public Task Continue(CancellationToken cancel) => SimpleCommandIgnoringOutput("exit", 0, cancel);
    public Task Pause(CancellationToken cancel) => SimpleCommandIgnoringOutput("break", 2, cancel);
    public Task Step(CancellationToken cancel) => SimpleCommandIgnoringOutput("step", 0, cancel);
    public Task StepOver(CancellationToken cancel) => SimpleCommandIgnoringOutput("stepOver", 0, cancel);
    public Task StepOut(CancellationToken cancel) => SimpleCommandIgnoringOutput("stepOut", 0, cancel);
}
