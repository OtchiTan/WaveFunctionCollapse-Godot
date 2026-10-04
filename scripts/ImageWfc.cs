using System;
using System.Collections.Generic;
using System.IO.Hashing;
using Godot;

namespace WFC.scripts;

struct ImageAdjacencyRuleSet
{
    public ulong Up;
    public ulong Down;
    public ulong Right;
    public ulong Left;
}

[GlobalClass]
public partial class ImageWfc : TileMapLayer
{
    [Export] public Vector2I MapSize = new(5, 5);
    [Export] public Vector2I TileSize = new(32, 32);
    [Export] public Texture2D BaseTexture;

    private ulong[] _tilePossibilities;
    private ImageAdjacencyRuleSet[] _constraints;
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
        var inputTilesIds = new int[totalTiles];

        var tilesMap = new Dictionary<ulong, int>();

        var tilesRatio = new Dictionary<int, int>();
        _tilesSet = new Dictionary<ulong, Vector2I>();

        // On conserve le Rect2I de chaque tile unique pour la blitter plus tard sans créer d'Image temporaire
        var uniqueTileRects = new Dictionary<ulong, Rect2I>();

        var basePixelData = baseImage.GetData();
        var imgWidth = baseImage.GetWidth();

        GD.Print($"Tiles : {tiles}");
        int tileByteSize = TileSize.X * TileSize.Y * 4;
        Span<byte> tileBytes = stackalloc byte[tileByteSize];

        // 1. Calcul des hashes directement via le buffer d'octets
        for (var y = 0; y < tiles.Y; ++y)
        {
            for (var x = 0; x < tiles.X; ++x)
            {
                var regionLeft = x * TileSize.X;
                var regionTop = y * TileSize.Y;


                int bufferIndex = 0;

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

                        // Normalisation des pixels 100% transparents
                        if (a == 0)
                        {
                            r = 0;
                            g = 0;
                            b = 0;
                        }

                        // Copie des 4 octets dans le tampon
                        tileBytes[bufferIndex++] = r;
                        tileBytes[bufferIndex++] = g;
                        tileBytes[bufferIndex++] = b;
                        tileBytes[bufferIndex++] = a;
                    }
                }

                // Calcul instantané du hash SIMD via XxHash3
                ulong hash = XxHash3.HashToUInt64(tileBytes);

                // Suite de ton code (dictionnaires, IDs...)
                tilesMap.TryAdd(hash, tilesMap.Count);

                tilesRatio.TryAdd(tilesMap[hash], 0);
                tilesRatio[tilesMap[hash]]++;

                inputTilesIds[y * tiles.X + x] = tilesMap[hash];
                uniqueTileRects.TryAdd(hash, new Rect2I(regionLeft, regionTop, TileSize.X, TileSize.Y));
            }
        }

        var maxTileCount = 64;
        _constraints = new ImageAdjacencyRuleSet[maxTileCount];

        for (var y = 0; y < tiles.Y; ++y)
        {
            for (var x = 0; x < tiles.X; ++x)
            {
                var tileId = inputTilesIds[y * tiles.X + x];

                // Calcul des coordonnées voisines avec enroulement toroïdal (Wrap-around)
                var yUp = (y > 0) ? y - 1 : tiles.Y - 1;
                var yDown = (y < tiles.Y - 1) ? y + 1 : 0;
                var xLeft = (x > 0) ? x - 1 : tiles.X - 1;
                var xRight = (x < tiles.X - 1) ? x + 1 : 0;

                // Récupération des IDs des voisins
                var topTileId = inputTilesIds[yUp * tiles.X + x];
                var downTileId = inputTilesIds[yDown * tiles.X + x];
                var leftTileId = inputTilesIds[y * tiles.X + xLeft];
                var rightTileId = inputTilesIds[y * tiles.X + xRight];

                // Application des masques de bits pour la tile courante
                _constraints[tileId].Up |= 1ul << topTileId;
                _constraints[tileId].Down |= 1ul << downTileId;
                _constraints[tileId].Left |= 1ul << leftTileId;
                _constraints[tileId].Right |= 1ul << rightTileId;
            }
        }

        GD.Print($"Tiles uniques : {tilesRatio.Count}");

        // 2. Assemblage de l'Atlas avec BlitRect (copie rapide bloc par bloc côté C++)
        var completeImage = Image.CreateEmpty(TileSize.X * tilesRatio.Count, TileSize.Y, false, Image.Format.Rgba8);

        var atlasX = 0;
        foreach (var (_, srcRect) in uniqueTileRects)
        {
            var dstPos = new Vector2I(atlasX * TileSize.X, 0);

            // Transfert direct de la région mémoire sans boucler pixel par pixel
            completeImage.BlitRect(baseImage, srcRect, dstPos);

            _tilesSet.TryAdd(1ul << atlasX, new Vector2I(atlasX, 0));
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

    private static bool IsTileCollapsed(ulong possibilities)
    {
        return ulong.PopCount(possibilities) == 1;
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
        _tilePossibilities = new ulong[MapSize.X * MapSize.Y];

        var value = 1ul;
        for (var i = 0; i < _tilesSet.Count; i++)
        {
            value |= 1ul << i;
        }

        for (var i = 0; i < MapSize.X * MapSize.Y; i++)
        {
            _tilePossibilities[i] = value;
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

                SetCell(new Vector2I(x, y), 0, _tilesSet[tile]);
            }
        }
    }

    private void RecursivelyCollapse(ulong adjacentTileMask, Vector2I adjacentIndex, ref Queue<Vector2I> stack)
    {
        var adjacentPossibility = GetTile(adjacentIndex.X, adjacentIndex.Y);
        var maskedTilePossibilities = adjacentPossibility & adjacentTileMask;

        if (adjacentPossibility == maskedTilePossibilities || maskedTilePossibilities == 0)
            return;

        SetTile(adjacentIndex.X, adjacentIndex.Y, maskedTilePossibilities);
        stack.Enqueue(adjacentIndex);
    }

    private ulong RandomlySelectPossibility(ulong possibilities)
    {
        List<int> tiles = [];

        for (var i = 0; i < _tilesSet.Count; i++)
        {
            if ((possibilities & (1ul << i)) != 0)
                tiles.Add(i);
        }

        var randomIndex = (int)(_rng.Randi() % (tiles.Count - 1));

        return (ulong)(1 << tiles[randomIndex]);
    }

    private ImageAdjacencyRuleSet ComputeAdjacentRuleset(ulong possibilities)
    {
        var ruleset = new ImageAdjacencyRuleSet();

        while (possibilities > 0)
        {
            var tileId = (int)ulong.TrailingZeroCount(possibilities);

            var validNeighbors = _constraints[tileId];

            ruleset.Up |= validNeighbors.Up;
            ruleset.Down |= validNeighbors.Down;
            ruleset.Left |= validNeighbors.Left;
            ruleset.Right |= validNeighbors.Right;

            possibilities &= ~(1ul << tileId);
        }

        return ruleset;
    }

    private ulong GetTile(int x, int y)
    {
        return _tilePossibilities[y * MapSize.X + x];
    }

    private void SetTile(int x, int y, ulong possibilities)
    {
        _tilePossibilities[y * MapSize.X + x] = possibilities;
    }
}