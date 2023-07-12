using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json.Serialization;

namespace TopGun;

public class RangeDebugInfo
{
    [JsonPropertyName("l")]
    public IReadOnlyList<int> Lengths { get; }
    [JsonPropertyName("i")]
    public IReadOnlyList<int> Infos { get; }

    public RangeDebugInfo(IReadOnlyList<int> lengths, IReadOnlyList<int> infos)
    {
        Lengths = lengths;
        Infos = infos;
        if (Lengths.Any() && (Infos.Count % Lengths.Count) != 0)
            throw new ArgumentException("Infos should have exactly some multiple elements as the range lengths");
    }
}

public class ScriptDebugInfo
{
    public int BaseLine { get; }
    public IReadOnlyList<RangeDebugInfo?> LineToByteOffsets { get; }
    public RangeDebugInfo ByteOffsetToLines { get; }

    public ScriptDebugInfo(
        int baseLine,
        IReadOnlyList<RangeDebugInfo?> lineToByteOffsets,
        RangeDebugInfo byteOffsetToLines)
    {
        BaseLine = baseLine;
        LineToByteOffsets = lineToByteOffsets;
        ByteOffsetToLines = byteOffsetToLines;
    }
}

public class SceneDebugInfo
{
    public SceneDebugInfo(SortedDictionary<int, ScriptDebugInfo> scripts)
    {
        Scripts = scripts;
    }

    public SortedDictionary<int, ScriptDebugInfo> Scripts { get; }
}
