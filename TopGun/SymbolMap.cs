using System;
using System.Collections.Generic;
using System.Linq;

namespace TopGun;

public class ScriptSymbolMap
{
    public ScriptSymbolMap(string? name, SortedDictionary<int, string> locals)
    {
        Name = name;
        Locals = locals ?? new();
    }

    public string? Name { get; }
    public SortedDictionary<int, string> Locals { get; }
}

public class SymbolMap
{
    public SymbolMap(SortedDictionary<int, string> systemVariables, SortedDictionary<int, string> sceneVariables, SortedDictionary<int, ScriptSymbolMap> scripts)
    {
        SystemVariables = systemVariables ?? new();
        SceneVariables = sceneVariables ?? new();
        Scripts = scripts ?? new();
    }

    public SortedDictionary<int, string> SystemVariables { get; }
    public SortedDictionary<int, string> SceneVariables { get; }
    public SortedDictionary<int, ScriptSymbolMap> Scripts { get; }
}
