using System;
using System.Collections.Generic;
using Godot;

namespace WFC.scripts;

[Flags]
public enum TileColor
{
    Red = 1 << 0,
    Green = 1 << 1,
    Blue = 1 << 2,
}

[GlobalClass]
public partial class Main2D : TileMapLayer
{
    [Export] public Vector2I MapSize = new(5, 5);

    private TileColor[] _tilePossibilities;

    [Export] public Vector2I RedIndex;
    [Export] public Vector2I GreenIndex;
    [Export] public Vector2I BlueIndex;

    public override void _Ready()
    {
        InitTiles();

        PropagateWaveFunctionCollapse();

        DrawTiles();
    }

    public override void _Process(double delta)
    {
    }

    private static bool IsTileCollapsed(TileColor color)
    {
        var remainingPossibilities = int.PopCount((int)color);
        return remainingPossibilities == 1;
    }

    private void PropagateWaveFunctionCollapse()
    {
        for (var x = 0; x < MapSize.X; x++)
        {
            for (var y = 0; y < MapSize.Y; y++)
            {
                if (!IsTileCollapsed(GetTile(x, y)))
                    WaveFunctionCollapse(new Vector2I(x, y));
            }
        }
    }

    private void WaveFunctionCollapse(Vector2I startIndex)
    {
        var tile = GetTile(startIndex.X, startIndex.Y);

        var rng = new RandomNumberGenerator();
        while (!IsTileCollapsed(tile))
        {
            switch (rng.RandiRange(0, 2))
            {
                case 0:
                    tile &= ~TileColor.Blue;
                    break;
                case 1:
                    tile &= ~TileColor.Green;
                    break;
                case 2:
                    tile &= ~TileColor.Red;
                    break;
            }
        }

        SetTile(startIndex.X, startIndex.Y, tile);

        Queue<Vector2I> stack = new();
        stack.Enqueue(startIndex);

        while (stack.Count > 0)
        {
            var index = stack.Dequeue();

            var possibilities = GetTile(index.X, index.Y);

            if (index.X > 0)
                RecursivelyCollapse(possibilities, new Vector2I(index.X - 1, index.Y), ref stack);
            if (index.X + 1 < MapSize.X)
                RecursivelyCollapse(possibilities, new Vector2I(index.X + 1, index.Y), ref stack);
            if (index.Y > 0)
                RecursivelyCollapse(possibilities, new Vector2I(index.X, index.Y - 1), ref stack);
            if (index.Y + 1 < MapSize.Y)
                RecursivelyCollapse(possibilities, new Vector2I(index.X, index.Y + 1), ref stack);
        }
    }

    private void InitTiles()
    {
        _tilePossibilities = new TileColor[MapSize.X * MapSize.Y];

        for (var i = 0; i < MapSize.X * MapSize.Y; i++)
        {
            _tilePossibilities[i] = TileColor.Red | TileColor.Green | TileColor.Blue;
        }
    }

    private void DrawTiles()
    {
        for (var x = 0; x < MapSize.X; x++)
        {
            for (var y = 0; y < MapSize.Y; y++)
            {
                var tile = GetTile(x, y);

                if (!IsTileCollapsed(tile))
                    continue;

                switch (tile)
                {
                    case TileColor.Blue:
                        SetCell(new Vector2I(x, y), 1, BlueIndex);
                        break;
                    case TileColor.Red:
                        SetCell(new Vector2I(x, y), 1, RedIndex);
                        break;
                    case TileColor.Green:
                    default:
                        SetCell(new Vector2I(x, y), 1, GreenIndex);
                        break;
                }
            }
        }
    }

    private void RecursivelyCollapse(TileColor possibilities, Vector2I adjacentIndex, ref Queue<Vector2I> stack)
    {
        var adjacentPossibility = GetTile(adjacentIndex.X, adjacentIndex.Y);

        var wasCollapsed = IsTileCollapsed(adjacentPossibility);

        if (possibilities == TileColor.Red)
            adjacentPossibility &= ~TileColor.Red;
        if (possibilities == TileColor.Green)
            adjacentPossibility &= ~TileColor.Green;
        if (possibilities == TileColor.Blue)
            adjacentPossibility &= ~TileColor.Blue;

        SetTile(adjacentIndex.X, adjacentIndex.Y, adjacentPossibility);

        var isCollapsed = IsTileCollapsed(adjacentPossibility);
        if (!wasCollapsed && isCollapsed)
            stack.Enqueue(adjacentIndex);
    }

    private TileColor GetTile(int x, int y)
    {
        return _tilePossibilities[y * MapSize.X + x];
    }

    private void SetTile(int x, int y, TileColor possibilities)
    {
        _tilePossibilities[y * MapSize.X + x] = possibilities;
    }
}