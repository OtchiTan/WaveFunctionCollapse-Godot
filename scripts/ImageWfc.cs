using System.Collections.Generic;
using Godot;

namespace WFC.scripts;

[GlobalClass]
public partial class ImageWfc : TileMapLayer
{
    [Export] public Vector2I MapSize = new(5, 5);
    [Export] public Vector2I TileSize = new(32, 32);
    [Export] public Texture2D BaseTexture;

    private ushort[] _tilePossibilities;
    private ulong[] _tilesHashes;
    private AdjacencyRuleset _allTilesConnecting = new();
    private RandomNumberGenerator _rng = new();
    private Dictionary<ulong, Vector2I> _tilesSet = new();

    public override void _Ready()
    {
        if (BaseTexture == null)
        {
            GD.Print("No base texture defined");
            return;
        }

        GD.Print("Setup Tile Set");

        var watch = System.Diagnostics.Stopwatch.StartNew();

        SetupTileSet();

        watch.Stop();

        GD.Print($"Tile setup in {watch.ElapsedMilliseconds / 1000} s");

        var tiles = BaseTexture.GetSize() / TileSize;

        for (var y = 0; y < tiles.Y; y++)
        {
            for (var x = 0; x < tiles.X; x++)
            {
                var hash = _tilesHashes[(int)(y * tiles.X + x)];
                SetCell(new Vector2I(x, y), 0, _tilesSet[hash]);
            }
        }

        /*
        SetupBaseRuleset();

        InitTiles();

        GD.Print("Propagate WFC");

        PropagateWaveFunctionCollapse();

        GD.Print("Draw tiles");

        DrawTiles();
        */
    }

    public override void _Process(double delta)
    {
    }

    private void SetupTileSet()
    {
        TileSet = new TileSet();

        TileSet.SetTileSize(TileSize);

        var tileSetSource = new TileSetAtlasSource();

        tileSetSource.SetTexture(CreateAtlasTexture());
        tileSetSource.SetTextureRegionSize(TileSize);
        tileSetSource.SetUseTexturePadding(true);

        for (var i = 0; i < _tilesSet.Count; i++)
        {
            tileSetSource.CreateTile(new Vector2I(i, 0));
        }

        TileSet.AddSource(tileSetSource);
    }

    private ImageTexture CreateAtlasTexture()
    {
        var baseImage = BaseTexture.GetImage();

        // S'assurer que l'image est au bon format pour manipuler 4 octets par pixel (RGBA8)
        if (baseImage.GetFormat() != Image.Format.Rgba8)
        {
            baseImage = (Image)baseImage.Duplicate();
            baseImage.Convert(Image.Format.Rgba8);
        }

        var baseTextureSize = new Vector2I(baseImage.GetWidth(), baseImage.GetHeight());
        var tiles = baseTextureSize / TileSize;
        var totalTiles = tiles.X * tiles.Y;

        _tilesHashes = new ulong[totalTiles];

        var tilesRatio = new Dictionary<ulong, int>();
        _tilesSet = new Dictionary<ulong, Vector2I>();

        // On conserve le Rect2I de chaque tile unique pour la blitter plus tard sans créer d'Image temporaire
        var uniqueTileRects = new Dictionary<ulong, Rect2I>();

        var basePixelData = baseImage.GetData();
        var imgWidth = baseImage.GetWidth();

        GD.Print($"Tiles : {tiles}");

        // 1. Calcul des hashes directement via le buffer d'octets
        for (var y = 0; y < tiles.Y; ++y)
        {
            for (var x = 0; x < tiles.X; ++x)
            {
                ulong hash = 0x1;

                var regionLeft = x * TileSize.X;
                var regionTop = y * TileSize.Y;

                for (var localY = 0; localY < TileSize.Y; ++localY)
                {
                    var absoluteY = regionTop + localY;
                    var rowOffset = absoluteY * imgWidth * 4;

                    for (var localX = 0; localX < TileSize.X; ++localX)
                    {
                        var absoluteX = regionLeft + localX;
                        var pixelIndex = rowOffset + (absoluteX * 4);

                        var r = basePixelData[pixelIndex];
                        var g = basePixelData[pixelIndex + 1];
                        var b = basePixelData[pixelIndex + 2];

                        hash *= 0xCE2273B48A5DA531UL;
                        var pixelValue = ((ulong)r << 16) | ((ulong)g << 8) | b;
                        var combinedColor = Xorshift64(pixelValue * 0x3CEAE4A956524891UL);
                        hash ^= combinedColor;
                    }
                }

                tilesRatio.TryAdd(hash, 0);
                tilesRatio[hash]++;

                _tilesHashes[y * tiles.X + x] = hash;

                // Retient la position d'origine de la tile sur l'image source
                uniqueTileRects.TryAdd(hash, new Rect2I(regionLeft, regionTop, TileSize.X, TileSize.Y));
            }
        }

        GD.Print($"Tiles uniques : {tilesRatio.Count}");

        // 2. Assemblage de l'Atlas avec BlitRect (copie rapide bloc par bloc côté C++)
        var completeImage = Image.CreateEmpty(TileSize.X * tilesRatio.Count, TileSize.Y, false, Image.Format.Rgba8);

        var atlasX = 0;
        foreach (var (imageHash, srcRect) in uniqueTileRects)
        {
            var dstPos = new Vector2I(atlasX * TileSize.X, 0);

            // Transfert direct de la région mémoire sans boucler pixel par pixel
            completeImage.BlitRect(baseImage, srcRect, dstPos);

            _tilesSet.TryAdd(imageHash, new Vector2I(atlasX, 0));
            atlasX++;
        }

        return ImageTexture.CreateFromImage(completeImage);
    }

    private static ulong Xorshift64(ulong state)
    {
        state ^= state << 13;
        state ^= state >> 7;
        state ^= state << 17;
        return state;
    }

    private static bool IsTileCollapsed(ushort color)
    {
        return ushort.PopCount(color) == 1;
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

                SetCell(new Vector2I(x, y), 0, new Vector2I(tileX, 0));
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

    private ushort GetTile(int x, int y)
    {
        return _tilePossibilities[y * MapSize.X + x];
    }

    private void SetTile(int x, int y, ushort possibilities)
    {
        _tilePossibilities[y * MapSize.X + x] = possibilities;
    }
}