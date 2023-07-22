using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
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
        Lengths = lengths ?? Array.Empty<int>();
        Infos = infos ?? Array.Empty<int>();
        if (Lengths.Any() && (Infos.Count % Lengths.Count) != 0)
            throw new ArgumentException("Infos should have exactly some multiple elements as the range lengths");
    }

    public int IndexOfContaining(int offset)
    {
        if (offset < 0)
            return -1;
        int curOffset = 0, i;
        for (i = 0; i < Lengths.Count; i++)
        {
            if (curOffset + Lengths[i] > offset)
                return i;
            curOffset += Lengths[i];
        }
        return -1;
    }
}

public class ScriptDebugInfo
{
    public int BaseLine { get; }
    public IReadOnlyList<RangeDebugInfo?> LineToByteOffsets { get; }
    public RangeDebugInfo ByteOffsetToLines { get; }
    public SortedSet<int> SceneVarRefs { get; }
    public SortedSet<int> SystemVarRefs { get; }

    public ScriptDebugInfo(
        int baseLine,
        IReadOnlyList<RangeDebugInfo?> lineToByteOffsets,
        RangeDebugInfo byteOffsetToLines,
        SortedSet<int> sceneVarRefs,
        SortedSet<int> systemVarRefs)
    {
        BaseLine = baseLine;
        LineToByteOffsets = lineToByteOffsets ?? Array.Empty<RangeDebugInfo?>();
        ByteOffsetToLines = byteOffsetToLines ?? new(null!, null!);
        SceneVarRefs = sceneVarRefs ?? new();
        SystemVarRefs = systemVarRefs ?? new();
    }

    public TextPosition? GetTextPosByOffset(int offset)
    {
        var infos = ByteOffsetToLines.Infos;
        var i = ByteOffsetToLines.IndexOfContaining(offset);
        return i >= 0 && i * 2 + 1 < infos.Count
            ? new(infos[i * 2 + 0], infos[i * 2 + 1])
            : null;
    }

    public int? GetOffsetByTextPos(TextPosition pos)
    {
        if (pos.Line < BaseLine || pos.Line >= BaseLine + LineToByteOffsets.Count)
            return null;
        var line = LineToByteOffsets[pos.Line - BaseLine];
        if (line == null)
            return null;

        var columnRangeI = line.IndexOfContaining(pos.Column);
        if (columnRangeI < 0 || columnRangeI >= line.Infos.Count)
            columnRangeI = 0;
        return line.Infos[columnRangeI];
    }
}

public class SceneDebugInfo
{
    public SceneDebugInfo(SortedDictionary<int, ScriptDebugInfo> scripts)
    {
        Scripts = scripts ?? new();
    }

    public SortedDictionary<int, ScriptDebugInfo> Scripts { get; }
}
