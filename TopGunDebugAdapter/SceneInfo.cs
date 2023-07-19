﻿using System.Collections.Generic;
using System.Linq;
using System.IO;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using System;

namespace TopGun.DebugAdapter;

internal class SceneInfo
{
    public required string Name { get; init; }
    public ResourceFile? ResourceFile { get; init; }
    public Source? Decompiled { get; init; }
    public Source? Disassembly { get; init; }
    public SceneDebugInfo? DebugInfo { get; init; }
    public SymbolMap? SymbolMap { get; init; }

    public (string plugin, string procedure)? FindProcedureName(int id)
    {
        if (id < 0 || ResourceFile == null)
            return null;
        else if (id <= ResourceFile.MaxScrMsg)
            return ("TopGun", "" + (ScriptOp)id);
        id -= (int)ResourceFile.MaxScrMsg + 1;
        if (id >= ResourceFile.PluginProcs.Count)
            return null;
        
        var (plugin, pluginProcI) = ResourceFile.PluginProcs[id];
        return (plugin.Name, plugin.Procs[pluginProcI]);
    }

    public (string script, TextPosition? textPos) FindScript(int index, int offset)
    {
        var isValid = IsValidScriptIndex(index);
        if (isValid == false)
            return ($"Invalid {index}", null);
        var name = SymbolMap?.Scripts?.GetValueOrDefault(index)?.Name ?? $"Script {index}";

        TextPosition? textPos = null;
        if (DebugInfo?.Scripts?.TryGetValue(index, out var scriptDebugInfo) == true)
        {
            textPos = scriptDebugInfo.GetTextPosByOffset(offset);
        }
        return (name, textPos);
    }

    public bool? IsValidScriptIndex(int index)
    {
        if (index < 0)
            return false;
        if (ResourceFile != null)
        {
            return index < ResourceFile.Resources.Count &&
                ResourceFile.Resources[index].Type == ResourceType.Script;
        }
        if (DebugInfo != null)
            return DebugInfo.Scripts.ContainsKey(index);
        if (SymbolMap != null && SymbolMap.Scripts.ContainsKey(index))
            return true;
        return null;
    }

    public (int Index, int Offset)? FindScript(TextPosition position)
    {
        if (DebugInfo == null)
            return null;

        var (index, offset) = DebugInfo.Scripts
            .Select(kv => (index: kv.Key, offset: kv.Value.GetOffsetByTextPos(position)))
            .FirstOrDefault(t => t.offset.HasValue);
        if (!offset.HasValue)
            return null;

        return (index, offset.Value);
    }
}
