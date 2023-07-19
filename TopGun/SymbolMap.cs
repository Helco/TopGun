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
    public SymbolMap(
        SortedDictionary<int, string> globals,
        SortedDictionary<int, ScriptSymbolMap> scripts)
    {
        Globals = globals;
        Scripts = scripts;
    }

    public SortedDictionary<int, string> Globals { get; }
    public SortedDictionary<int, ScriptSymbolMap> Scripts { get; }
}
