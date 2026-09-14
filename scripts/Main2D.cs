using System.Collections.Generic;
using Godot;

namespace WFC.scripts;

public struct TilePossibilities
{
    public bool IsRedPossible;
    public bool IsGreenPossible;
    public bool IsBluePossible;

    public bool IsCollapsed()
    {
        return OptionCount() == 1;
    }

    public bool HasCollapsedToRed()
    {
        return IsRedPossible && IsCollapsed();
    }

    public bool HasCollapsedToGreen()
    {
        return IsGreenPossible && IsCollapsed();
    }

    public bool HasCollapsedToBlue()
    {
        return IsBluePossible && IsCollapsed();
    }

    private int OptionCount()
    {
        var count = 0;
        if (IsRedPossible) count++;
        if (IsGreenPossible) count++;
        if (IsBluePossible) count++;
        return count;
    }
}

[GlobalClass]
public partial class Main2D : TileMapLayer
{
    [Export] public Vector2I MapSize = new(5, 5);

    private TilePossibilities[] _tilePossibilities;

    [Export] public Vector2I RedIndex;
    [Export] public Vector2I GreenIndex;
    [Export] public Vector2I BlueIndex;

    public override void _Ready()
    {
        InitTiles();

        for (var x = 0; x < MapSize.X; x++)
        {
            for (var y = 0; y < MapSize.Y; y++)
            {
                if (!GetTile(x, y).IsCollapsed())
                    WaveFunctionCollapse(new Vector2I(x, y));
            }
        }

        DrawTiles();
    }

    public override void _Process(double delta)
    {
    }

    private void WaveFunctionCollapse(Vector2I startIndex)
    {
        var tile = GetTile(startIndex.X, startIndex.Y);

        var rng = new RandomNumberGenerator();
        while (!tile.IsCollapsed())
        {
            switch (rng.RandiRange(0, 2))
            {
                case 0:
                    tile.IsBluePossible = false;
                    break;
                case 1:
                    tile.IsGreenPossible = false;
                    break;
                case 2:
                    tile.IsRedPossible = false;
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
        _tilePossibilities = new TilePossibilities[MapSize.X * MapSize.Y];

        for (var i = 0; i < MapSize.X * MapSize.Y; i++)
        {
            _tilePossibilities[i] = new TilePossibilities
            {
                IsRedPossible = true,
                IsGreenPossible = true,
                IsBluePossible = true,
            };
        }
    }

    private void DrawTiles()
    {
        for (var x = 0; x < MapSize.X; x++)
        {
            for (var y = 0; y < MapSize.Y; y++)
            {
                var tile = GetTile(x, y);

                if (!tile.IsCollapsed())
                    continue;

                if (tile.IsBluePossible)
                    SetCell(new Vector2I(x, y), 1, BlueIndex);
                else if (tile.IsRedPossible)
                    SetCell(new Vector2I(x, y), 1, RedIndex);
                else
                    SetCell(new Vector2I(x, y), 1, GreenIndex);
            }
        }
    }

    private void RecursivelyCollapse(TilePossibilities possibilities, Vector2I adjacentIndex, ref Queue<Vector2I> stack)
    {
        var adjacentPossibility = GetTile(adjacentIndex.X, adjacentIndex.Y);

        var wasCollapsed = adjacentPossibility.IsCollapsed();

        if (possibilities.HasCollapsedToRed())
            adjacentPossibility.IsRedPossible = false;
        if (possibilities.HasCollapsedToGreen())
            adjacentPossibility.IsGreenPossible = false;
        if (possibilities.HasCollapsedToBlue())
            adjacentPossibility.IsBluePossible = false;

        SetTile(adjacentIndex.X, adjacentIndex.Y, adjacentPossibility);

        var isCollapsed = adjacentPossibility.IsCollapsed();
        if (!wasCollapsed && isCollapsed)
            stack.Enqueue(adjacentIndex);
    }

    private TilePossibilities GetTile(int x, int y)
    {
        return _tilePossibilities[y * MapSize.X + x];
    }

    private void SetTile(int x, int y, TilePossibilities possibilities)
    {
        _tilePossibilities[y * MapSize.X + x] = possibilities;
    }
}