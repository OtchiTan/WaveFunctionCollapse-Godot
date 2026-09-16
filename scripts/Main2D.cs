using System;
using System.Collections.Generic;
using Godot;

namespace WFC.scripts;

public struct AdjacencyRuleset
{
    public ushort Up = 0;
    public ushort Down = 0;
    public ushort Left = 0;
    public ushort Right = 0;

    public AdjacencyRuleset()
    {
    }
}

[GlobalClass]
public partial class Main2D : TileMapLayer
{
    [Export] public Vector2I MapSize = new(5, 5);

    private ushort[] _tilePossibilities;

    [Export] public Vector2I RedIndex;
    [Export] public Vector2I GreenIndex;
    [Export] public Vector2I BlueIndex;

    [Export] public int SourceId;

    private AdjacencyRuleset _allTilesConnecting = new();
    private RandomNumberGenerator _rng = new();

    public override void _Ready()
    {
        SetupBaseRuleset();

        InitTiles();

        PropagateWaveFunctionCollapse();

        GD.Print("Draw tiles");

        DrawTiles();
    }

    public override void _Process(double delta)
    {
    }

    private static bool IsTileCollapsed(ushort color)
    {
        var remainingPossibilities = ushort.PopCount(color);
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
        var randomTile = RandomlySelectPossibility(tile);
        SetTile(startIndex.X, startIndex.Y, randomTile);

        Queue<Vector2I> stack = new();
        stack.Enqueue(startIndex);

        while (stack.Count > 0)
        {
            var index = stack.Dequeue();

            var possibilities = GetTile(index.X, index.Y);

            var validAdjacentTiles = ComputeAdjacentRuleset(possibilities);

            if (index.X > 0)
                RecursivelyCollapse(validAdjacentTiles.Left, new Vector2I(index.X - 1, index.Y), ref stack);
            if (index.X + 1 < MapSize.X)
                RecursivelyCollapse(validAdjacentTiles.Right, new Vector2I(index.X + 1, index.Y), ref stack);
            if (index.Y > 0)
                RecursivelyCollapse(validAdjacentTiles.Up, new Vector2I(index.X, index.Y - 1), ref stack);
            if (index.Y + 1 < MapSize.Y)
                RecursivelyCollapse(validAdjacentTiles.Down, new Vector2I(index.X, index.Y + 1), ref stack);
        }
    }

    private void InitTiles()
    {
        _tilePossibilities = new ushort[MapSize.X * MapSize.Y];

        for (var i = 0; i < MapSize.X * MapSize.Y; i++)
        {
            _tilePossibilities[i] = ushort.MaxValue;
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

                var tileX = 0;
                for (var i = 0; i < 16; i++)
                {
                    if ((tile & 1 << i) > 0)
                        break;

                    tileX++;
                }

                SetCell(new Vector2I(x, y), SourceId, new Vector2I(tileX, 0));
            }
        }
    }

    private void RecursivelyCollapse(ushort adjacentTileMask, Vector2I adjacentIndex, ref Queue<Vector2I> stack)
    {
        var adjacentPossibility = GetTile(adjacentIndex.X, adjacentIndex.Y);
        var maskedTilePossibilities = adjacentPossibility & adjacentTileMask;

        if (adjacentPossibility == maskedTilePossibilities)
            return;

        SetTile(adjacentIndex.X, adjacentIndex.Y, (ushort)maskedTilePossibilities);
        stack.Enqueue(adjacentIndex);
    }

    private void SetupBaseRuleset()
    {
        for (var i = 0; i < 16; i++)
        {
            var connectsUp = (i & 0x1) != 0;
            var connectsRight = (i & 0x2) != 0;
            var connectsDown = (i & 0x4) != 0;
            var connectsLeft = (i & 0x8) != 0;

            if (connectsUp)
                _allTilesConnecting.Up = (ushort)(_allTilesConnecting.Up | 1 << i);
            if (connectsDown)
                _allTilesConnecting.Down = (ushort)(_allTilesConnecting.Down | 1 << i);
            if (connectsRight)
                _allTilesConnecting.Right = (ushort)(_allTilesConnecting.Right | 1 << i);
            if (connectsLeft)
                _allTilesConnecting.Left = (ushort)(_allTilesConnecting.Left | 1 << i);
        }
    }

    private ushort RandomlySelectPossibility(ushort possibilities)
    {
        List<int> tiles = [];

        for (var i = 0; i < 16; i++)
        {
            if ((possibilities & (1 << i)) != 0)
                tiles.Add(i);
        }

        if (tiles.Count == 0)
            return 0;

        return (ushort)(1 << tiles[_rng.RandiRange(0, tiles.Count - 1)]);
    }

    private AdjacencyRuleset ComputeAdjacentRuleset(ushort possibleTiles)
    {
        var ruleset = new AdjacencyRuleset();

        if ((possibleTiles & _allTilesConnecting.Up) != 0)
            ruleset.Up |= _allTilesConnecting.Down;
        if ((possibleTiles & ~_allTilesConnecting.Up) != 0)
            ruleset.Up |= (ushort)~_allTilesConnecting.Down;
        
        if ((possibleTiles & _allTilesConnecting.Down) != 0)
            ruleset.Down |= _allTilesConnecting.Up;
        if ((possibleTiles & ~_allTilesConnecting.Down) != 0)
            ruleset.Down |= (ushort)~_allTilesConnecting.Up;
        
        if ((possibleTiles & _allTilesConnecting.Left) != 0)
            ruleset.Left |= _allTilesConnecting.Right;
        if ((possibleTiles & ~_allTilesConnecting.Left) != 0)
            ruleset.Left |= (ushort)~_allTilesConnecting.Right;
        
        if ((possibleTiles & _allTilesConnecting.Right) != 0)
            ruleset.Right |= _allTilesConnecting.Left;
        if ((possibleTiles & ~_allTilesConnecting.Right) != 0)
            ruleset.Right |= (ushort)~_allTilesConnecting.Left;

        return ruleset;
    }

    private Vector2I GetRandomNonCollapsedTile()
    {
        List<Vector2I> availableTiles = [];

        for (var x = 0; x < MapSize.X; x++)
        {
            for (var y = 0; y < MapSize.Y; y++)
            {
                var tile = GetTile(x, y);
                if (ushort.PopCount(tile) > 1)
                    availableTiles.Add(new Vector2I(x, y));
            }
        }

        return availableTiles.Count == 0
            ? new Vector2I(-1, -1)
            : availableTiles[new Random().Next(0, availableTiles.Count - 1)];
    }

    private ushort GetTile(int x, int y)
    {
        return _tilePossibilities[y * MapSize.X + x];
    }

    private void SetTile(int x, int y, ushort possibilities)
    {
        _tilePossibilities[y * MapSize.X + x] = possibilities;
    }
}