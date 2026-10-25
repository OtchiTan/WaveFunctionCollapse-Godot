using System;
using System.Collections.Generic;
using System.IO.Hashing;
using System.Linq;
using Godot;

namespace WFC.scripts;

struct ImageAdjacencyRuleSet
{
    public TileBitset Up;
    public TileBitset Down;
    public TileBitset Right;
    public TileBitset Left;
}

struct ProbabilityWeight
{
    public int Low;
    public int High;
    public int Tile;
}

[GlobalClass]
public partial class ImageWfc : ImageTileMapLayer
{
    private const int Wildcard = -1;

    [Export] public Vector2I TileSize = new(32, 32);
    [Export] public Texture2D BaseTexture;


    [Export] public Texture2D[] StaticAssets = [];


    private Vector2I MapSize { get; set; }

    private TileBitset[] _tilePossibilities;
    private ImageAdjacencyRuleSet[] _constraints;
    private readonly RandomNumberGenerator _rng = new();
    private Dictionary<int, Vector2I> _tilesSet = new();
    private Dictionary<int, int> _tileRatio = new();
    private int _uniqueTileCount;


    private Dictionary<Vector2I, int> _pinnedTiles = new();


    private HashSet<int> _exclusiveTiles = new();

    protected override void GenerateImage()
    {
        _rng.Randomize();

        if (BaseTexture == null)
        {
            GD.Print("No base texture defined");
            return;
        }

        MapSize = new Vector2I(BaseTexture.GetWidth(), BaseTexture.GetHeight()) / TileSize;
        GD.Print($"Map size : {MapSize}");

        GD.Print("Setup Tile Set");
        SetupTileSet();

        DrawMap();
    }

    private void DrawMap()
    {
        InitTiles();

        GD.Print("Place static assets");
        ApplyStaticAssets();

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

    private static Image GetRgba8Image(Texture2D texture)
    {
        var image = texture.GetImage();

        if (image.GetFormat() != Image.Format.Rgba8)
        {
            image = (Image)image.Duplicate();
            image.Convert(Image.Format.Rgba8);
        }

        return image;
    }


    private ulong HashTile(byte[] pixels, int imageWidth, int left, int top, Span<byte> buffer,
        out bool fullyTransparent)
    {
        var bufferIndex = 0;
        fullyTransparent = true;

        for (var localY = 0; localY < TileSize.Y; ++localY)
        {
            var rowOffset = (top + localY) * imageWidth * 4;

            for (var localX = 0; localX < TileSize.X; ++localX)
            {
                var pixelIndex = rowOffset + (left + localX) * 4;

                var r = pixels[pixelIndex];
                var g = pixels[pixelIndex + 1];
                var b = pixels[pixelIndex + 2];
                var a = pixels[pixelIndex + 3];

                if (a == 0)
                {
                    r = 0;
                    g = 0;
                    b = 0;
                }
                else
                {
                    fullyTransparent = false;
                }

                buffer[bufferIndex++] = r;
                buffer[bufferIndex++] = g;
                buffer[bufferIndex++] = b;
                buffer[bufferIndex++] = a;
            }
        }

        return XxHash3.HashToUInt64(buffer);
    }

    private ImageTexture CreateAtlasTexture()
    {
        var baseImage = GetRgba8Image(BaseTexture);

        var baseTextureSize = new Vector2I(baseImage.GetWidth(), baseImage.GetHeight());
        var tiles = baseTextureSize / TileSize;
        var totalTiles = tiles.X * tiles.Y;
        var inputTilesIds = new int[totalTiles];

        var tilesMap = new Dictionary<ulong, int>();
        var tileIdToRect = new Dictionary<int, Rect2I>();
        _tilesSet = new Dictionary<int, Vector2I>();
        _tileRatio = new Dictionary<int, int>();

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

                var hash = HashTile(basePixelData, imgWidth, regionLeft, regionTop, tileBytes, out _);

                if (!tilesMap.TryGetValue(hash, out var value))
                {
                    var newId = tilesMap.Count;
                    value = newId;
                    tilesMap[hash] = value;
                    tileIdToRect[newId] = new Rect2I(regionLeft, regionTop, TileSize.X, TileSize.Y);
                }

                _tileRatio.TryAdd(value, 0);
                _tileRatio[value] += 1;
                inputTilesIds[y * tiles.X + x] = value;
            }
        }

        _uniqueTileCount = tilesMap.Count;


        _pinnedTiles = FindStaticAssets(tilesMap, inputTilesIds, tiles);
        _exclusiveTiles = ComputeExclusiveTiles(inputTilesIds, tiles);
        GD.Print($"Static tiles : {_pinnedTiles.Count} cells, {_exclusiveTiles.Count} exclusive tile(s)");

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

                if (x > 0)
                {
                    var leftTileId = inputTilesIds[y * tiles.X + (x - 1)];
                    _constraints[tileId].Left.SetBit(leftTileId);
                }

                if (x < tiles.X - 1)
                {
                    var rightTileId = inputTilesIds[y * tiles.X + (x + 1)];
                    _constraints[tileId].Right.SetBit(rightTileId);
                }

                if (y > 0)
                {
                    var topTileId = inputTilesIds[(y - 1) * tiles.X + x];
                    _constraints[tileId].Up.SetBit(topTileId);
                }

                if (y < tiles.Y - 1)
                {
                    var downTileId = inputTilesIds[(y + 1) * tiles.X + x];
                    _constraints[tileId].Down.SetBit(downTileId);
                }
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


    private Dictionary<Vector2I, int> FindStaticAssets(Dictionary<ulong, int> tilesMap, int[] baseIds,
        Vector2I baseTiles)
    {
        var pinned = new Dictionary<Vector2I, int>();

        if (StaticAssets == null || StaticAssets.Length == 0)
            return pinned;

        Span<byte> tileBytes = stackalloc byte[TileSize.X * TileSize.Y * 4];

        for (var assetIndex = 0; assetIndex < StaticAssets.Length; assetIndex++)
        {
            var texture = StaticAssets[assetIndex];
            if (texture == null) continue;

            var assetName = string.IsNullOrEmpty(texture.ResourcePath)
                ? $"#{assetIndex}"
                : texture.ResourcePath;

            var image = GetRgba8Image(texture);
            var cols = image.GetWidth() / TileSize.X;
            var rows = image.GetHeight() / TileSize.Y;

            if (cols == 0 || rows == 0)
            {
                GD.PushWarning($"Static asset {assetName} is smaller than one tile, ignored");
                continue;
            }

            if (image.GetWidth() % TileSize.X != 0 || image.GetHeight() % TileSize.Y != 0)
            {
                GD.PushWarning($"Static asset {assetName} size is not a multiple of TileSize, extra pixels ignored");
            }

            var pixels = image.GetData();
            var pattern = new int[cols * rows];
            var valid = true;
            var hasContent = false;

            for (var ty = 0; ty < rows && valid; ty++)
            {
                for (var tx = 0; tx < cols; tx++)
                {
                    var hash = HashTile(pixels, image.GetWidth(), tx * TileSize.X, ty * TileSize.Y, tileBytes,
                        out var transparent);

                    if (transparent)
                    {
                        pattern[ty * cols + tx] = Wildcard;
                        continue;
                    }

                    hasContent = true;

                    if (!tilesMap.TryGetValue(hash, out var tileId))
                    {
                        valid = false;
                        break;
                    }

                    pattern[ty * cols + tx] = tileId;
                }
            }

            if (!valid || !hasContent)
            {
                GD.PushWarning($"Static asset {assetName} contains tiles that are not in the base image, ignored");
                continue;
            }

            var matches = 0;

            for (var by = 0; by <= baseTiles.Y - rows; by++)
            {
                for (var bx = 0; bx <= baseTiles.X - cols; bx++)
                {
                    if (!MatchesAt(baseIds, baseTiles.X, pattern, cols, rows, bx, by))
                        continue;

                    matches++;

                    for (var ty = 0; ty < rows; ty++)
                    {
                        for (var tx = 0; tx < cols; tx++)
                        {
                            var id = pattern[ty * cols + tx];
                            if (id == Wildcard) continue;

                            pinned[new Vector2I(bx + tx, by + ty)] = id;
                        }
                    }
                }
            }

            if (matches == 0)
                GD.PushWarning($"Static asset {assetName} not found in the base image");
            else
                GD.Print($"Static asset {assetName} : {matches} occurrence(s)");
        }

        return pinned;
    }

    private static bool MatchesAt(int[] baseIds, int baseWidth, int[] pattern, int cols, int rows, int bx, int by)
    {
        for (var ty = 0; ty < rows; ty++)
        {
            for (var tx = 0; tx < cols; tx++)
            {
                var expected = pattern[ty * cols + tx];
                if (expected == Wildcard) continue;

                if (baseIds[(by + ty) * baseWidth + bx + tx] != expected)
                    return false;
            }
        }

        return true;
    }


    private HashSet<int> ComputeExclusiveTiles(int[] baseIds, Vector2I baseTiles)
    {
        var exclusive = new HashSet<int>(_pinnedTiles.Values);
        if (exclusive.Count == 0) return exclusive;

        for (var y = 0; y < baseTiles.Y; y++)
        {
            for (var x = 0; x < baseTiles.X; x++)
            {
                if (_pinnedTiles.ContainsKey(new Vector2I(x, y))) continue;
                exclusive.Remove(baseIds[y * baseTiles.X + x]);
            }
        }

        return exclusive;
    }

    private void InitTiles()
    {
        _tilePossibilities = new TileBitset[MapSize.X * MapSize.Y];


        var freeMask = new TileBitset(_uniqueTileCount);
        for (var i = 0; i < _uniqueTileCount; i++)
        {
            if (!_exclusiveTiles.Contains(i))
                freeMask.SetBit(i);
        }

        for (var y = 0; y < MapSize.Y; y++)
        {
            for (var x = 0; x < MapSize.X; x++)
            {
                var index = y * MapSize.X + x;

                if (_pinnedTiles.TryGetValue(new Vector2I(x, y), out var tileId))
                {
                    var pinned = new TileBitset(_uniqueTileCount);
                    pinned.SetBit(tileId);
                    _tilePossibilities[index] = pinned;
                }
                else
                {
                    _tilePossibilities[index] = freeMask;
                }
            }
        }
    }


    private void ApplyStaticAssets()
    {
        if (_pinnedTiles.Count == 0) return;

        var queue = new Queue<Vector2I>();

        for (var y = 0; y < MapSize.Y; y++)
        {
            for (var x = 0; x < MapSize.X; x++)
            {
                queue.Enqueue(new Vector2I(x, y));
            }
        }

        PropagateConstraints(queue);
    }

    private void DrawTiles()
    {
        Clear();

        List<int> activeBits = [];

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

        var queue = new Queue<Vector2I>();
        queue.Enqueue(startIndex);

        PropagateConstraints(queue);
    }

    private void PropagateConstraints(Queue<Vector2I> queue)
    {
        while (queue.Count > 0)
        {
            var index = queue.Dequeue();
            var possibilities = GetTile(index.X, index.Y);

            var validAdjacentTiles = ComputeAdjacentRuleset(possibilities);

            if (index.X > 0)
                RestrictNeighbor(validAdjacentTiles.Left, new Vector2I(index.X - 1, index.Y), queue);
            if (index.X + 1 < MapSize.X)
                RestrictNeighbor(validAdjacentTiles.Right, new Vector2I(index.X + 1, index.Y), queue);
            if (index.Y > 0)
                RestrictNeighbor(validAdjacentTiles.Up, new Vector2I(index.X, index.Y - 1), queue);
            if (index.Y + 1 < MapSize.Y)
                RestrictNeighbor(validAdjacentTiles.Down, new Vector2I(index.X, index.Y + 1), queue);
        }
    }

    private void RestrictNeighbor(TileBitset adjacentTileMask, Vector2I adjacentIndex, Queue<Vector2I> queue)
    {
        var adjacentPossibility = GetTile(adjacentIndex.X, adjacentIndex.Y);
        var maskedTilePossibilities = adjacentPossibility & adjacentTileMask;

        if (adjacentPossibility.Equals(maskedTilePossibilities) || maskedTilePossibilities.IsEmpty())
            return;

        SetTile(adjacentIndex.X, adjacentIndex.Y, maskedTilePossibilities);
        queue.Enqueue(adjacentIndex);
    }

    private TileBitset RandomlySelectPossibility(TileBitset possibilities)
    {
        var probabilities = new List<ProbabilityWeight>
        {
            Capacity = possibilities.PopCount()
        };

        var currentCount = 0;

        for (var i = 0; i < _uniqueTileCount; i++)
        {
            var tileBit = new TileBitset(_uniqueTileCount);
            tileBit.SetBit(i);

            if ((possibilities & tileBit).IsEmpty()) continue;

            var weight = _tileRatio[i];
            var w = new ProbabilityWeight
            {
                Low = currentCount,
                High = currentCount + weight,
                Tile = i
            };
            probabilities.Add(w);
            currentCount += weight;
        }

        var randomValue = _rng.RandiRange(0, currentCount - 1);

        var collapsedTile =
            (from weight in probabilities
                where randomValue >= weight.Low && randomValue < weight.High
                select weight.Tile).FirstOrDefault();

        var result = new TileBitset(_uniqueTileCount);
        result.SetBit(collapsedTile);

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

        List<int> bitsBuffer = [];

        possibilities.GetActiveBits(bitsBuffer);

        foreach (var validNeighbors in bitsBuffer.Select(tileId => _constraints[tileId]))
        {
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