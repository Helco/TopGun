using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TopGun;

partial class ScriptDecompiler
{
    public ScriptDebugInfo CreateDebugInfo()
    {
        if (blocksByOffset.Count == 0)
            throw new InvalidOperationException("Script was not decompiled yet, cannot create debug info");
        if (ASTEntry.EndTextPosition == default)
            WriteTo(TextWriter.Null);


        MapBytesToText(out var byteRangeSet, out var lineRangeSets);
        int minLine = lineRangeSets.Keys.First(), maxLine = lineRangeSets.Keys.Last();
        int lineCount = maxLine - minLine + 1;
        var lineToByteOffsets = Enumerable
            .Range(0, lineCount)
            .Select(i => lineRangeSets.TryGetValue(i + minLine, out var markedRangeSet) ? markedRangeSet : null)
            .Select(set => set == null ? null : new RangeDebugInfo(set.Lengths, set.Infos))
            .ToArray();
        var byteOffsetToLines = new RangeDebugInfo(byteRangeSet.Lengths, byteRangeSet.Infos
            .SelectMany(tp => new[] { tp.Line, tp.Column })
            .ToArray());

        FindGlobalVarReferences(out var sceneVarRefs, out var systemVarRefs);

        return new ScriptDebugInfo(minLine, lineToByteOffsets, byteOffsetToLines, sceneVarRefs, systemVarRefs);
    }

    private void MapBytesToText(out MarkedRangeSet<TextPosition> byteRangeSet, out SortedDictionary<int, MarkedRangeSet<int>> lineRangeSets)
    {
        var validNodes = ASTEntry.AllChildren.Where(node =>
            node.StartTotalOffset >= 0 && node.EndTotalOffset >= 0 &&
            node.EndTotalOffset - node.StartTotalOffset > 0 &&
            node.StartTextPosition != default && node.EndTextPosition != default &&
            node.StartTextPosition != node.EndTextPosition);

        var maxColumnInLine = FindMaxColumnInLine(validNodes);
        byteRangeSet = new();
        lineRangeSets = new();

        foreach (var node in validNodes)
        {
            for (int line = node.StartTextPosition.Line; line <= node.EndTextPosition.Line; line++)
            {
                int startColumn = line == node.StartTextPosition.Line ? node.StartTextPosition.Column : 0;
                int endColumn = line == node.EndTextPosition.Line ? node.EndTextPosition.Column
                    : line < lineLengths.Length ? lineLengths[line] - 1
                    : maxColumnInLine.TryGetValue(line, out var maxColumn) ? maxColumn
                    : startColumn + 1;

                if (!lineRangeSets.TryGetValue(line, out var lineRangeSet))
                    lineRangeSets.Add(line, lineRangeSet = new());
                if (line < lineLengths.Length)
                    lineRangeSet.TotalLength = lineLengths[line];

                lineRangeSet.Mark(startColumn, endColumn - startColumn + 1, node.StartOwnOffset);
            }

            byteRangeSet.Mark(node.StartTotalOffset, node.EndTotalOffset - node.StartTotalOffset, node.StartTextPosition);
        }
    }

    private static Dictionary<int, int> FindMaxColumnInLine(IEnumerable<ASTNode> validNodes)
    {
        var maxColumnInLine = new Dictionary<int, int>();
        foreach (var node in validNodes)
        {
            maxColumnInLine.TryAdd(node.StartTextPosition.Line, 0);
            maxColumnInLine.TryAdd(node.EndTextPosition.Line, 0);
            maxColumnInLine[node.StartTextPosition.Line] = Math.Max(maxColumnInLine[node.StartTextPosition.Line], node.StartTextPosition.Column);
            maxColumnInLine[node.EndTextPosition.Line] = Math.Max(maxColumnInLine[node.EndTextPosition.Line], node.EndTextPosition.Column);
        }

        return maxColumnInLine;
    }

    private void FindGlobalVarReferences(out SortedSet<int> sceneVarRefs, out SortedSet<int> systemVarRefs)
    {
        sceneVarRefs = new();
        systemVarRefs = new();

        foreach (var variableRef in ASTEntry.AllChildren.OfType<ASTVarReference>())
        {
            if (variableRef is ASTSceneVarAddress or ASTSceneVarValue)
                sceneVarRefs.Add(variableRef.Index);
            if (variableRef is ASTSystemVarAddress or ASTSystemVarValue)
                systemVarRefs.Add(variableRef.Index);
        }
    }
}
