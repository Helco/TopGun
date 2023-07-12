using System;
using System.Collections.Generic;
using System.Linq;

namespace TopGun;

// The proper way would be to track ranges, instead we just have a mask and determine ranges later

public class MarkedRangeSet<TInfo>
{
    private TInfo?[] mask = Array.Empty<TInfo?>();
    private readonly List<int> lengths = new();
    private readonly List<TInfo?> infos = new();
    private bool areRangesDirty = false;

    public int TotalLength
    {
        get => mask.Length;
        set
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value));
            Array.Resize(ref mask, value);
            areRangesDirty = true;
        }
    }

    public IReadOnlyList<int> Lengths
    {
        get
        {
            DetermineRanges();
            return lengths;
        }
    }

    public IReadOnlyList<TInfo?> Infos
    {
        get
        {
            DetermineRanges();
            return infos;
        }
    }

    public MarkedRangeSet(int startTotalLength = 0)
    {
        if (startTotalLength < 0)
            throw new ArgumentOutOfRangeException(nameof(startTotalLength));
        else if (startTotalLength > 0)
            mask = new TInfo?[startTotalLength];
    }

    public void Mark(int offset, int length, TInfo info)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
        if (length == 0) return;
        if (offset + length > mask.Length)
            Array.Resize(ref mask, offset + length);
        Array.Fill(mask, info, offset, length);
        areRangesDirty = true;
    }

    private void DetermineRanges()
    {
        if (!areRangesDirty)
            return;
        areRangesDirty = false;
        lengths.Clear();
        infos.Clear();
        if (mask.Length == 0)
            return;

        int startI = 0;
        TInfo? info = mask[0];
        for (int i = 1; i < mask.Length; i++)
        {
            if (!EqualityComparer<TInfo>.Default.Equals(mask[i], info))
            {
                lengths.Add(i - startI);
                infos.Add(info);
                startI = i;
                info = mask[i];
            }
        }
        if (startI < mask.Length)
        {
            lengths.Add(mask.Length - startI);
            infos.Add(info);
        }
    }
}
