using System;

namespace WFC.scripts;

/// <summary>
/// Atlas dense de 6 x 6 tuiles de 16 x 16 px (96 x 96 px), ciel compris.
/// Index d'une tuile = colonne + ligne * 6. Chaque case de l'atlas est utilisée.
/// </summary>
public static class Tiles
{
    public const int AtlasColumns = 6;
    public const int AtlasRows = 6;
    public const int Count = AtlasColumns * AtlasRows; // 36

    // Ligne 0
    public const byte Sky = 0;
    public const byte Ground = 1;
    public const byte Brick = 2;
    public const byte QuestionCoin = 3;
    public const byte QuestionPowerUp = 4;

    public const byte UsedBlock = 5;

    // Ligne 1
    public const byte Hard = 6;
    public const byte PipeTopL = 7;
    public const byte PipeTopR = 8;
    public const byte PipeBodyL = 9;
    public const byte PipeBodyR = 10;

    public const byte Coin = 11;

    // Ligne 2
    public const byte Goomba = 12;
    public const byte Koopa = 13;
    public const byte Piranha = 14;
    public const byte FlagTop = 15;
    public const byte FlagPole = 16;

    public const byte Flag = 17;

    // Ligne 3
    public const byte CastleTop = 18;
    public const byte CastleWindow = 19;
    public const byte CastleWall = 20;
    public const byte CastleDoorTop = 21;
    public const byte CastleDoorBottom = 22;

    public const byte HillTop = 23;

    // Ligne 4
    public const byte HillSlopeL = 24;
    public const byte HillSlopeR = 25;
    public const byte HillBody = 26;
    public const byte BushL = 27;
    public const byte BushM = 28;

    public const byte BushR = 29;

    // Ligne 5
    public const byte CloudTL = 30;
    public const byte CloudTM = 31;
    public const byte CloudTR = 32;
    public const byte CloudBL = 33;
    public const byte CloudBM = 34;
    public const byte CloudBR = 35;

    public static int Column(byte tile) => tile % AtlasColumns;
    public static int Row(byte tile) => tile / AtlasColumns;
}

/// <summary>Grille unique de tuiles. Toute case non remplie est du ciel (Tiles.Sky = 0).</summary>
public class Level
{
    public readonly int Width;
    public readonly int Height;

    /// <summary>Ligne de la surface du sol (première ligne de sol).</summary>
    public int FloorY;

    public int SpawnX = 2;

    /// <summary>Case du socle du mât du drapeau.</summary>
    public int XExit;

    public int YExit;

    private readonly byte[,] tiles;

    public Level(int width, int height)
    {
        Width = width;
        Height = height;
        tiles = new byte[width, height]; // 0 = Sky
    }

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    public byte Get(int x, int y) => InBounds(x, y) ? tiles[x, y] : Tiles.Sky;

    public void Set(int x, int y, byte tile)
    {
        if (InBounds(x, y)) tiles[x, y] = tile;
    }

    /// <summary>Pose la tuile seulement si la case est du ciel.</summary>
    public bool SetIfSky(int x, int y, byte tile)
    {
        if (!InBounds(x, y) || tiles[x, y] != Tiles.Sky) return false;
        tiles[x, y] = tile;
        return true;
    }
}

/// <summary>
/// Générateur de niveaux dans l'esprit de Super Mario Bros. (NES), en une seule grille :
/// tuiles pleines uniquement, sol sur 2 lignes, fosses, tuyaux, escaliers,
/// rangées de blocs sur deux étages, ennemis et pièces posés comme tuiles,
/// drapeau, château et décor. Chaque tuile de l'atlas est garantie présente.
/// </summary>
public class LevelGenerator
{
    public static int LastSeed;

    public static Level CreateLevel(int width, int height, int seed, int difficulty)
        => new LevelGenerator(width, height).Build(seed, difficulty);

    private const int StartLength = 16; // départ plat et sûr
    private const int EndLength = 30; // espace réservé à la fin (escalier, mât, château)
    private const int GroundRows = 2;

    private readonly int width;
    private readonly int height;
    private int floorY;
    private int difficulty;
    private Level level;
    private Random random;

    private LevelGenerator(int width, int height)
    {
        this.width = width;
        this.height = height;
    }

    private Level Build(int seed, int difficulty)
    {
        this.difficulty = Math.Max(0, difficulty);

        LastSeed = seed;
        random = new Random(seed);
        level = new Level(width, height);
        floorY = height - GroundRows;
        level.FloorY = floorY;

        // Départ
        int x = BuildFlat(0, StartLength, safe: true);
        level.Set(8, floorY - 4, Tiles.QuestionCoin);

        // Corps du niveau
        int limit = Math.Max(x, width - EndLength);
        while (x < limit)
            x += BuildZone(x, limit - x);

        // Fin (escalier, mât, drapeau, château)
        BuildEnd(x);

        // Structures, blocs, ennemis, pièces : rien ne doit manquer
        EnsureUsed(Tiles.Brick, Tiles.Piranha);

        // Décor (uniquement sur des cases de ciel), puis garantie du décor
        PlaceScenery();
        EnsureUsed(Tiles.HillTop, Tiles.CloudBR);

        return level;
    }

    // ------------------------------------------------------------------
    // Zones
    // ------------------------------------------------------------------

    private int BuildZone(int xo, int maxLength)
    {
        int wFlat = 30;
        int wPit = 10 + 3 * difficulty;
        int wPipes = 14;
        int wStairs = 6 + 2 * difficulty;
        int total = wFlat + wPit + wPipes + wStairs;

        int t = random.Next(total);
        if (t < wFlat) return BuildFlat(xo, maxLength);
        t -= wFlat;
        if (t < wPit) return BuildPit(xo, maxLength);
        t -= wPit;
        if (t < wPipes) return BuildPipes(xo, maxLength);
        return BuildStairs(xo, maxLength);
    }

    private int BuildFlat(int xo, int maxLength, bool safe = false)
    {
        int length = safe ? maxLength : Math.Min(maxLength, 4 + random.Next(9)); // 4..12
        FillGround(xo, xo + length);

        if (!safe)
        {
            AddEnemies(xo + 1, xo + length - 1);
            if (length >= 6 && random.Next(2) == 0)
                BuildBlockRows(xo + 1, xo + length - 1);
        }

        return length;
    }

    private int BuildPit(int xo, int maxLength)
    {
        int pit = 2 + random.Next(2 + Math.Min(difficulty, 1)); // 2..3 (2..4 si difficulté >= 1)
        int landing = 3 + random.Next(3); // 3..5
        int length = pit + landing;
        if (length > maxLength) return BuildFlat(xo, maxLength);

        FillGround(xo + pit, xo + length);

        // Arc de pièces au-dessus du trou
        if (random.Next(2) == 0)
            for (int i = 0; i < pit; i++)
                level.Set(xo + i, floorY - 3, Tiles.Coin);

        AddEnemies(xo + pit + 1, xo + length - 1);
        return length;
    }

    private int BuildPipes(int xo, int maxLength)
    {
        int count = 1 + random.Next(3); // 1..3 tuyaux
        int x = xo + 3 + random.Next(2); // élan avant le premier tuyau
        int placed = 0;

        while (placed < count && x + 2 + 3 <= xo + maxLength)
        {
            int h = 2 + random.Next(3); // hauteur 2..4
            PlacePipe(x, h, allowPiranha: true);
            placed++;
            x += 2 + 3 + random.Next(3); // largeur du tuyau + écart 3..5
        }

        if (placed == 0) return BuildFlat(xo, maxLength);

        int length = Math.Min(x - xo, maxLength);
        FillGround(xo, xo + length);
        return length;
    }

    private void PlacePipe(int x, int h, bool allowPiranha)
    {
        int top = floorY - h;
        level.Set(x, top, Tiles.PipeTopL);
        level.Set(x + 1, top, Tiles.PipeTopR);
        for (int y = top + 1; y < floorY; y++)
        {
            level.Set(x, y, Tiles.PipeBodyL);
            level.Set(x + 1, y, Tiles.PipeBodyR);
        }

        // Plante piranha posée juste au-dessus de la case de gauche de l'embouchure
        if (allowPiranha && difficulty >= 1 && random.Next(10) < 2 * difficulty)
            level.SetIfSky(x, top - 1, Tiles.Piranha);
    }

    private int BuildStairs(int xo, int maxLength)
    {
        int n = 3 + random.Next(2); // 3..4 marches
        int mode = random.Next(3); // 0 montant, 1 descendant, 2 pyramide
        int steps = mode == 2 ? n * 2 : n;
        const int lead = 2;
        const int tail = 2;
        int length = lead + steps + tail;
        if (length > maxLength) return BuildFlat(xo, maxLength);

        FillGround(xo, xo + length);
        for (int i = 0; i < steps; i++)
        {
            int h = mode == 0 ? i + 1
                : mode == 1 ? n - i
                : (i < n ? i + 1 : 2 * n - i);
            for (int k = 1; k <= h; k++)
                level.Set(xo + lead + i, floorY - k, Tiles.Hard);
        }

        return length;
    }

    // ------------------------------------------------------------------
    // Éléments réutilisables
    // ------------------------------------------------------------------

    private void FillGround(int x0, int x1)
    {
        for (int x = x0; x < x1; x++)
        for (int y = floorY; y < height; y++)
            level.Set(x, y, Tiles.Ground);
    }

    /// <summary>Rangée de blocs à 4 cases du sol, parfois un second étage à 8 cases.</summary>
    private void BuildBlockRows(int x0, int x1)
    {
        int span = x1 - x0;
        if (span < 3) return;

        int len = Math.Min(span, 3 + random.Next(5)); // 3..7
        int start = x0 + random.Next(span - len + 1);
        int y = floorY - 4;
        bool powerUp = false;

        for (int i = 0; i < len; i++)
        {
            byte tile = Tiles.Brick;
            int r = random.Next(8);
            if (r == 0)
            {
                tile = Tiles.QuestionCoin;
            }
            else if (r == 1 && !powerUp)
            {
                tile = Tiles.QuestionPowerUp;
                powerUp = true;
            }
            else if (r == 2)
            {
                tile = Tiles.UsedBlock;
            }

            level.Set(start + i, y, tile);
        }

        // Second étage
        int y2 = floorY - 8;
        if (len >= 4 && y2 > 2 && random.Next(3) == 0)
        {
            for (int i = 1; i < len - 1; i++)
            {
                byte tile = random.Next(4) == 0 ? Tiles.QuestionCoin : Tiles.Brick;
                level.Set(start + i, y2, tile);
            }
        }
    }

    private void AddEnemies(int x0, int x1)
    {
        for (int x = x0; x < x1; x++)
        {
            if (random.Next(30) < 2 + 2 * difficulty)
            {
                bool koopa = difficulty >= 1 && random.Next(3) == 0;
                level.Set(x, floorY - 1, koopa ? Tiles.Koopa : Tiles.Goomba);
                x += 2; // évite les paquets d'ennemis
            }
        }
    }

    // ------------------------------------------------------------------
    // Fin de niveau
    // ------------------------------------------------------------------

    private void BuildEnd(int xo)
    {
        FillGround(xo, width);

        // Grand escalier de 8 marches
        int stairX = xo + 2;
        for (int i = 0; i < 8; i++)
        for (int k = 1; k <= i + 1; k++)
            level.Set(stairX + i, floorY - k, Tiles.Hard);

        // Mât du drapeau : socle plein, mât, boule et drapeau
        int poleX = stairX + 8 + 3;
        level.Set(poleX, floorY - 1, Tiles.Hard);
        int top = Math.Max(0, floorY - 10);
        level.Set(poleX, top, Tiles.FlagTop);
        for (int y = top + 1; y <= floorY - 2; y++)
            level.Set(poleX, y, Tiles.FlagPole);
        level.Set(poleX - 1, top + 1, Tiles.Flag);

        level.XExit = poleX;
        level.YExit = floorY - 1;

        BuildCastle(poleX + 4, floorY - 5);
    }

    /// <summary>Château de 5 x 5 cases ; y0 = ligne du haut.</summary>
    private void BuildCastle(int x0, int y0)
    {
        // Tour du haut
        for (int c = 1; c <= 3; c++)
            level.Set(x0 + c, y0, Tiles.CastleTop);

        level.Set(x0 + 1, y0 + 1, Tiles.CastleWindow);
        level.Set(x0 + 2, y0 + 1, Tiles.CastleWall);
        level.Set(x0 + 3, y0 + 1, Tiles.CastleWindow);

        // Corps large
        for (int c = 0; c < 5; c++)
            level.Set(x0 + c, y0 + 2, Tiles.CastleTop);

        for (int c = 0; c < 5; c++)
        {
            if (c == 2)
            {
                level.Set(x0 + c, y0 + 3, Tiles.CastleDoorTop);
                level.Set(x0 + c, y0 + 4, Tiles.CastleDoorBottom);
            }
            else
            {
                level.Set(x0 + c, y0 + 3, Tiles.CastleWall);
                level.Set(x0 + c, y0 + 4, Tiles.CastleWall);
            }
        }
    }

    // ------------------------------------------------------------------
    // Décor (une seule grille : on ne pose que sur des cases de ciel)
    // ------------------------------------------------------------------

    private void PlaceScenery()
    {
        // Nuages
        for (int x = random.Next(4); x < width; x += 6 + random.Next(10))
        {
            int w = 2 + random.Next(3); // 2..4
            int y = 1 + random.Next(Math.Max(1, floorY - 7));
            PlaceCloud(x, y, w);
        }

        // Collines
        for (int x = 4 + random.Next(8); x < width - EndLength; x += 14 + random.Next(16))
            PlaceHill(x, 2 + random.Next(2)); // petite (2) ou grande (3)

        // Buissons
        for (int x = 8 + random.Next(8); x < width - EndLength; x += 10 + random.Next(20))
            PlaceBush(x, 3 + random.Next(3)); // 3..5
    }

    private bool PlaceCloud(int x0, int y0, int w)
    {
        if (!AreaIsSky(x0, y0, w, 2)) return false;

        for (int c = 0; c < w; c++)
        {
            byte top = c == 0 ? Tiles.CloudTL : c == w - 1 ? Tiles.CloudTR : Tiles.CloudTM;
            byte bottom = c == 0 ? Tiles.CloudBL : c == w - 1 ? Tiles.CloudBR : Tiles.CloudBM;
            level.Set(x0 + c, y0, top);
            level.Set(x0 + c, y0 + 1, bottom);
        }

        return true;
    }

    /// <summary>Colline en pyramide : h lignes, base de 2h - 1 cases, posée sur le sol.</summary>
    private bool PlaceHill(int x0, int h)
    {
        int cx = x0 + h - 1;

        for (int c = 0; c < 2 * h - 1; c++)
            if (level.Get(x0 + c, floorY) != Tiles.Ground)
                return false;

        for (int r = 0; r < h; r++)
        for (int x = cx - r; x <= cx + r; x++)
            if (level.Get(x, floorY - h + r) != Tiles.Sky)
                return false;

        for (int r = 0; r < h; r++)
        {
            int y = floorY - h + r;
            for (int x = cx - r; x <= cx + r; x++)
            {
                byte tile = r == 0 ? Tiles.HillTop
                    : x == cx - r ? Tiles.HillSlopeL
                    : x == cx + r ? Tiles.HillSlopeR
                    : Tiles.HillBody;
                level.Set(x, y, tile);
            }
        }

        return true;
    }

    private bool PlaceBush(int x0, int w)
    {
        for (int c = 0; c < w; c++)
        {
            if (level.Get(x0 + c, floorY) != Tiles.Ground) return false;
            if (level.Get(x0 + c, floorY - 1) != Tiles.Sky) return false;
        }

        for (int c = 0; c < w; c++)
        {
            byte tile = c == 0 ? Tiles.BushL : c == w - 1 ? Tiles.BushR : Tiles.BushM;
            level.Set(x0 + c, floorY - 1, tile);
        }

        return true;
    }

    // ------------------------------------------------------------------
    // Garantie : chaque tuile de l'atlas apparaît au moins une fois
    // ------------------------------------------------------------------

    private bool AreaIsSky(int x0, int y0, int w, int h)
    {
        for (int x = x0; x < x0 + w; x++)
        for (int y = y0; y < y0 + h; y++)
            if (!level.InBounds(x, y) || level.Get(x, y) != Tiles.Sky)
                return false;
        return true;
    }

    private bool HasGround(int x0, int w)
    {
        for (int x = x0; x < x0 + w; x++)
            if (level.Get(x, floorY) != Tiles.Ground)
                return false;
        return true;
    }

    private bool IsUsed(byte tile)
    {
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
            if (level.Get(x, y) == tile)
                return true;
        return false;
    }

    private void EnsureUsed(byte first, byte last)
    {
        for (int t = first; t <= last; t++)
        {
            if (!IsUsed((byte)t))
                Force((byte)t);
        }
    }

    private void Force(byte tile)
    {
        switch (tile)
        {
            case Tiles.PipeTopL:
            case Tiles.PipeTopR:
            case Tiles.PipeBodyL:
            case Tiles.PipeBodyR:
                ForcePipe();
                break;
            case Tiles.Piranha:
                if (!TryAddPiranha())
                {
                    ForcePipe();
                    TryAddPiranha();
                }

                break;
            case Tiles.Brick:
            case Tiles.QuestionCoin:
            case Tiles.QuestionPowerUp:
            case Tiles.UsedBlock:
            case Tiles.Coin:
                ForceFloating(tile);
                break;
            case Tiles.Goomba:
            case Tiles.Koopa:
                ForceGroundEnemy(tile);
                break;
            case Tiles.CloudTL:
            case Tiles.CloudTM:
            case Tiles.CloudTR:
            case Tiles.CloudBL:
            case Tiles.CloudBM:
            case Tiles.CloudBR:
                ForceCloud();
                break;
            case Tiles.HillTop:
            case Tiles.HillSlopeL:
            case Tiles.HillSlopeR:
            case Tiles.HillBody:
                ForceHill();
                break;
            case Tiles.BushL:
            case Tiles.BushM:
            case Tiles.BushR:
                ForceBush();
                break;
            // Sky, Ground, Hard, drapeau et château sont toujours posés par la génération.
        }
    }

    private void ForcePipe()
    {
        // 3 lignes libres : 2 pour le tuyau + 1 pour une éventuelle plante piranha
        for (int x = StartLength; x + 1 < width - EndLength; x++)
        {
            if (HasGround(x, 2) && AreaIsSky(x, floorY - 3, 2, 3))
            {
                PlacePipe(x, 2, allowPiranha: false);
                return;
            }
        }
    }

    private bool TryAddPiranha()
    {
        for (int x = 0; x < width; x++)
        for (int y = 1; y < height; y++)
            if (level.Get(x, y) == Tiles.PipeTopL && level.SetIfSky(x, y - 1, Tiles.Piranha))
                return true;
        return false;
    }

    private void ForceFloating(byte tile)
    {
        int y = tile == Tiles.Coin ? floorY - 3 : floorY - 4;
        for (int x = StartLength; x < width - EndLength; x++)
        {
            if (HasGround(x, 1) && AreaIsSky(x, floorY - 4, 1, 4))
            {
                level.Set(x, y, tile);
                return;
            }
        }
    }

    private void ForceGroundEnemy(byte tile)
    {
        for (int x = StartLength; x < width - EndLength; x++)
        {
            if (HasGround(x, 1) && level.Get(x, floorY - 1) == Tiles.Sky)
            {
                level.Set(x, floorY - 1, tile);
                return;
            }
        }
    }

    private void ForceCloud()
    {
        for (int y = 1; y <= floorY - 7; y++)
        for (int x = 0; x < width; x++)
            if (PlaceCloud(x, y, 3))
                return;
    }

    private void ForceHill()
    {
        for (int x = 0; x < width; x++)
            if (PlaceHill(x, 3))
                return;
    }

    private void ForceBush()
    {
        for (int x = 0; x < width; x++)
            if (PlaceBush(x, 3))
                return;
    }
}