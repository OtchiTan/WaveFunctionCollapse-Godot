using System.Collections.Generic;
using System.Linq;

namespace WFC.scripts;

using System;
using System.Numerics;

public readonly struct TileBitset : IEquatable<TileBitset>
{
    private readonly ulong[] _words;

    public TileBitset(int totalTiles)
    {
        var wordCount = (totalTiles + 63) / 64;
        _words = new ulong[wordCount];
    }

    private TileBitset(ulong[] words)
    {
        _words = words;
    }

    public static TileBitset CreateFull(int totalTiles)
    {
        var bitset = new TileBitset(totalTiles);
        for (var i = 0; i < totalTiles; i++)
        {
            bitset.SetBit(i);
        }

        return bitset;
    }

    public void SetBit(int index)
    {
        _words[index >> 6] |= 1ul << (index & 63);
    }

    public bool HasBit(int index)
    {
        return (_words[index >> 6] & (1ul << (index & 63))) != 0;
    }

    public bool IsEmpty()
    {
        return _words.All(t => t == 0);
    }

    public int PopCount()
    {
        return _words.Sum(BitOperations.PopCount);
    }

    public bool IsCollapsed() => PopCount() == 1;

    public static TileBitset operator &(TileBitset a, TileBitset b)
    {
        var result = new ulong[a._words.Length];
        for (var i = 0; i < a._words.Length; i++)
        {
            result[i] = a._words[i] & b._words[i];
        }

        return new TileBitset(result);
    }

    public static TileBitset operator |(TileBitset a, TileBitset b)
    {
        var result = new ulong[a._words.Length];
        for (var i = 0; i < a._words.Length; i++)
        {
            result[i] = a._words[i] | b._words[i];
        }

        return new TileBitset(result);
    }

    public bool Equals(TileBitset other)
    {
        if (_words == null || other._words == null) return _words == other._words;
        if (_words.Length != other._words.Length) return false;
        return !_words.Where((t, i) => t != other._words[i]).Any();
    }

    public void GetActiveBits(List<int> outputList)
    {
        outputList.Clear();
        for (var w = 0; w < _words.Length; w++)
        {
            var word = _words[w];
            while (word > 0)
            {
                var tz = BitOperations.TrailingZeroCount(word);
                outputList.Add((w << 6) + tz);
                word &= ~(1ul << tz);
            }
        }
    }
}