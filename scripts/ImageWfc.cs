using System;
using System.Collections.Generic;
using System.IO.Hashing;
using Godot;

namespace WFC.scripts;

struct ImageAdjacencyRuleSet
{
    public TileBitset Up;
    public TileBitset Down;
    public TileBitset Right;
    public TileBitset Left;
}

[GlobalClass]
public partial class ImageWfc : TileMapLayer
{
    [Export] public Vector2I MapSize = new(5, 5);
    [Export] public Vector2I TileSize = new(32, 32);
    [Export] public Texture2D BaseTexture;

    private TileBitset[] _tilePossibilities;
    private ImageAdjacencyRuleSet[] _constraints;
    private readonly RandomNumberGenerator _rng = new();
    private Dictionary<int, Vector2I> _tilesSet = new();
    private int _uniqueTileCount;

    public override void _Ready()
    {
        _rng.Randomize();

        if (BaseTexture == null)
        {
            GD.Print("No base texture defined");
            return;
        }

        GD.Print("Setup Tile Set");
        SetupTileSet();

        DrawMap();
    }

    private void DrawMap()
    {
        InitTiles();

        GD.Print("Propagate WFC");
        PropagateWaveFunctionCollapse();

        GD.Print("Draw tiles");
        DrawTiles();
    }

    public override void _Process(double delta)
    {
        if (Input.IsActionJustPressed("ui_reroll"))
        {
            DrawMap();
        }
    }

    private void SetupTileSet()
    {
        TileSet = new TileSet();
        TileSet.SetTileSize(TileSize);

        var tileSetSource = new TileSetAtlasSource();
        tileSetSource.SetTexture(CreateAtlasTexture());
        tileSetSource.SetTextureRegionSize(TileSize);
        tileSetSource.SetUseTexturePadding(true);

        for (var i = 0; i < _uniqueTileCount; i++)
        {
            tileSetSource.CreateTile(new Vector2I(i, 0));
        }

        TileSet.AddSource(tileSetSource);
    }

    private ImageTexture CreateAtlasTexture()
    {
        var baseImage = BaseTexture.GetImage();

        if (baseImage.GetFormat() != Image.Format.Rgba8)
        {
            baseImage = (Image)baseImage.Duplicate();
            baseImage.Convert(Image.Format.Rgba8);
        }

        var baseTextureSize = new Vector2I(baseImage.GetWidth(), baseImage.GetHeight());
        var tiles = baseTextureSize / TileSize;
        var totalTiles = tiles.X * tiles.Y;
        var inputTilesIds = new int[totalTiles];

        var tilesMap = new Dictionary<ulong, int>();
        var tileIdToRect = new Dictionary<int, Rect2I>();
        _tilesSet = new Dictionary<int, Vector2I>();

        var basePixelData = baseImage.GetData();
        var imgWidth = baseImage.GetWidth();

        GD.Print($"Tiles : {tiles}");
        var tileByteSize = TileSize.X * TileSize.Y * 4;
        Span<byte> tileBytes = stackalloc byte[tileByteSize];

        for (var y = 0; y < tiles.Y; ++y)
        {
            for (var x = 0; x < tiles.X; ++x)
            {
                var regionLeft = x * TileSize.X;
                var regionTop = y * TileSize.Y;
                var bufferIndex = 0;

                for (var localY = 0; localY < TileSize.Y; ++localY)
                {
                    var absoluteY = regionTop + localY;
                    var rowOffset = absoluteY * imgWidth * 4;

                    for (var localX = 0; localX < TileSize.X; ++localX)
                    {
                        var absoluteX = regionLeft + localX;
                        var pixelIndex = rowOffset + absoluteX * 4;

                        var r = basePixelData[pixelIndex];
                        var g = basePixelData[pixelIndex + 1];
                        var b = basePixelData[pixelIndex + 2];
                        var a = basePixelData[pixelIndex + 3];

                        if (a == 0) { r = 0; g = 0; b = 0; }

                        tileBytes[bufferIndex++] = r;
                        tileBytes[bufferIndex++] = g;
                        tileBytes[bufferIndex++] = b;
                        tileBytes[bufferIndex++] = a;
                    }
                }

                var hash = XxHash3.HashToUInt64(tileBytes);

                if (!tilesMap.ContainsKey(hash))
                {
                    var newId = tilesMap.Count;
                    tilesMap[hash] = newId;
                    tileIdToRect[newId] = new Rect2I(regionLeft, regionTop, TileSize.X, TileSize.Y);
                }

                inputTilesIds[y * tiles.X + x] = tilesMap[hash];
            }
        }

        _uniqueTileCount = tilesMap.Count;
        _constraints = new ImageAdjacencyRuleSet[_uniqueTileCount];

        for (var i = 0; i < _uniqueTileCount; i++)
        {
            _constraints[i] = new ImageAdjacencyRuleSet
            {
                Up = new TileBitset(_uniqueTileCount),
                Down = new TileBitset(_uniqueTileCount),
                Left = new TileBitset(_uniqueTileCount),
                Right = new TileBitset(_uniqueTileCount)
            };
        }

        for (var y = 0; y < tiles.Y; ++y)
        {
            for (var x = 0; x < tiles.X; ++x)
            {
                var tileId = inputTilesIds[y * tiles.X + x];

                var yUp = (y > 0) ? y - 1 : tiles.Y - 1;
                var yDown = (y < tiles.Y - 1) ? y + 1 : 0;
                var xLeft = (x > 0) ? x - 1 : tiles.X - 1;
                var xRight = (x < tiles.X - 1) ? x + 1 : 0;

                var topTileId = inputTilesIds[yUp * tiles.X + x];
                var downTileId = inputTilesIds[yDown * tiles.X + x];
                var leftTileId = inputTilesIds[y * tiles.X + xLeft];
                var rightTileId = inputTilesIds[y * tiles.X + xRight];

                _constraints[tileId].Up.SetBit(topTileId);
                _constraints[tileId].Down.SetBit(downTileId);
                _constraints[tileId].Left.SetBit(leftTileId);
                _constraints[tileId].Right.SetBit(rightTileId);
            }
        }

        GD.Print($"Tiles uniques : {_uniqueTileCount}");

        var completeImage = Image.CreateEmpty(TileSize.X * _uniqueTileCount, TileSize.Y, false, Image.Format.Rgba8);

        for (var tileId = 0; tileId < _uniqueTileCount; tileId++)
        {
            var srcRect = tileIdToRect[tileId];
            var dstPos = new Vector2I(tileId * TileSize.X, 0);

            completeImage.BlitRect(baseImage, srcRect, dstPos);
            _tilesSet[tileId] = new Vector2I(tileId, 0);
        }

        return ImageTexture.CreateFromImage(completeImage);
    }

    private void PropagateWaveFunctionCollapse()
    {
        for (var x = 0; x < MapSize.X; x++)
        {
            for (var y = 0; y < MapSize.Y; y++)
            {
                if (!GetTile(x, y).IsCollapsed())
                    WaveFunctionCollapse(new Vector2I(x, y));
            }
        }
    }

    private void WaveFunctionCollapse(Vector2I startIndex)
    {
        var tile = GetTile(startIndex.X, startIndex.Y);
        if (tile.IsEmpty()) return;

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
        _tilePossibilities = new TileBitset[MapSize.X * MapSize.Y];

        var fullBitset = TileBitset.CreateFull(_uniqueTileCount);

        for (var i = 0; i < MapSize.X * MapSize.Y; i++)
        {
            _tilePossibilities[i] = fullBitset;
        }
    }

    private void DrawTiles()
    {
        List<int> activeBits = new();

        for (var x = 0; x < MapSize.X; x++)
        {
            for (var y = 0; y < MapSize.Y; y++)
            {
                var tile = GetTile(x, y);

                if (!tile.IsCollapsed())
                    continue;

                tile.GetActiveBits(activeBits);
                if (activeBits.Count > 0 && _tilesSet.TryGetValue(activeBits[0], out var tilePos))
                {
                    SetCell(new Vector2I(x, y), 0, tilePos);
                }
            }
        }
    }

    private void RecursivelyCollapse(TileBitset adjacentTileMask, Vector2I adjacentIndex, ref Queue<Vector2I> stack)
    {
        var adjacentPossibility = GetTile(adjacentIndex.X, adjacentIndex.Y);
        var maskedTilePossibilities = adjacentPossibility & adjacentTileMask;

        if (adjacentPossibility.Equals(maskedTilePossibilities) || maskedTilePossibilities.IsEmpty())
            return;

        SetTile(adjacentIndex.X, adjacentIndex.Y, maskedTilePossibilities);
        stack.Enqueue(adjacentIndex);
    }

    private readonly List<int> _bitsBuffer = [];

    private TileBitset RandomlySelectPossibility(TileBitset possibilities)
    {
        possibilities.GetActiveBits(_bitsBuffer);

        if (_bitsBuffer.Count == 0) return new TileBitset(_uniqueTileCount);

        var randomIndex = _rng.RandiRange(0, _bitsBuffer.Count - 1);
        var chosenTileId = _bitsBuffer[randomIndex];

        var result = new TileBitset(_uniqueTileCount);
        result.SetBit(chosenTileId);
        return result;
    }

    private ImageAdjacencyRuleSet ComputeAdjacentRuleset(TileBitset possibilities)
    {
        var ruleset = new ImageAdjacencyRuleSet
        {
            Up = new TileBitset(_uniqueTileCount),
            Down = new TileBitset(_uniqueTileCount),
            Left = new TileBitset(_uniqueTileCount),
            Right = new TileBitset(_uniqueTileCount)
        };

        possibilities.GetActiveBits(_bitsBuffer);

        for (var i = 0; i < _bitsBuffer.Count; i++)
        {
            var tileId = _bitsBuffer[i];
            var validNeighbors = _constraints[tileId];

            ruleset.Up |= validNeighbors.Up;
            ruleset.Down |= validNeighbors.Down;
            ruleset.Left |= validNeighbors.Left;
            ruleset.Right |= validNeighbors.Right;
        }

        return ruleset;
    }

    private TileBitset GetTile(int x, int y)
    {
        return _tilePossibilities[y * MapSize.X + x];
    }

    private void SetTile(int x, int y, TileBitset possibilities)
    {
        _tilePossibilities[y * MapSize.X + x] = possibilities;
    }
}