using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;
using OmniSharp.Extensions.DebugAdapter.Server;

namespace TopGun.DebugAdapter;

internal class StackTraceHandler : IStackTraceHandler
{
    private readonly ScummVMConsoleAPI api;
    private readonly SceneInfoLoader sceneInfoLoader;

    private DebugAdapterServer Server => sceneInfoLoader.Server;

    public StackTraceHandler(ScummVMConsoleAPI api, SceneInfoLoader sceneInfoLoader)
    {
        this.api = api;
        this.sceneInfoLoader = sceneInfoLoader;
    }

    public async Task<StackTraceResponse> Handle(StackTraceArguments request, CancellationToken cancellationToken)
    {
        var sceneInfo = await sceneInfoLoader.LoadCurrentSceneInfo(cancellationToken);
        
        var totalStacktrace = await api.Stacktrace(cancellationToken);
        var stacktrace = totalStacktrace as IEnumerable<ScummVMFrame>;
        if (request.StartFrame.HasValue)
            stacktrace = stacktrace.Skip((int)request.StartFrame.Value);
        if (request.Levels.HasValue)
            stacktrace = stacktrace.Take((int)request.Levels.Value);

        return new()
        {
            TotalFrames = totalStacktrace.Count,
            StackFrames = stacktrace.Select(ConvertFrame).ToArray()
        };

        StackFrame ConvertFrame(ScummVMFrame scummFrame) => scummFrame.Type switch
        {
            ScummVMCallType.Root => ConvertScriptFrame(scummFrame),
            ScummVMCallType.Calc => ConvertScriptFrame(scummFrame),
            ScummVMCallType.Proc => ConvertProcFrame(scummFrame),
            _ => new StackFrame()
            {
                Id = scummFrame.Id,
                Name = $"{scummFrame.Type} {scummFrame.Index} @ {scummFrame.Offset}",
                PresentationHint = StackFramePresentationHint.Subtle
            }
        };

        StackFrame ConvertScriptFrame(ScummVMFrame scummFrame)
        {
            var (script, textPosOpt) = sceneInfo.FindScript(scummFrame.Index, scummFrame.Offset);
            int line = 0, column = 0;
            if (textPosOpt.HasValue && sceneInfo.Decompiled != null)
            {
                (line, column) = textPosOpt.Value;
                if (Server.ClientSettings.LinesStartAt1)
                    line++;
                if (Server.ClientSettings.ColumnsStartAt1)
                    column++;
            }
            return new StackFrame()
            {
                Id = scummFrame.Id,
                Name = $"({scummFrame.Type}) {script}",
                ModuleId = sceneInfo.Name,
                Source = sceneInfo.Decompiled,
                Line = line,
                Column = column,
                InstructionPointerReference = $"{sceneInfo.Name}-{scummFrame.Index}-{scummFrame.Offset:D4}"
            };
        }

        StackFrame ConvertProcFrame(ScummVMFrame scummFrame)
        {
            var procInfo = sceneInfo.FindProcedureName(scummFrame.Index)
                ?? ("Unknown", $"Procedure {scummFrame.Index}");
            return new StackFrame()
            {
                Id = scummFrame.Id,
                Name = "(Proc) " + procInfo.procedure,
                ModuleId = procInfo.plugin,
                Source = new()
                {
                    Name = procInfo.plugin,
                },
                PresentationHint = procInfo.plugin == "Unknown"
                    ? StackFramePresentationHint.Subtle
                    : StackFramePresentationHint.Normal
            };
        }
    }
}
