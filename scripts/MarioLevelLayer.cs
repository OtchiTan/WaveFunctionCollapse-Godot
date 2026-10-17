using Godot;

namespace WFC.scripts;

/// <summary>
/// TileMapLayer unique qui génère un niveau façon Super Mario Bros.
/// L'atlas fait 6 x 6 tuiles de 16 px (voir la classe Tiles), ciel compris.
/// </summary>
public partial class MarioLevelLayer : ImageTileMapLayer
{
    [Export] public int AtlasSourceId { get; set; } = 0;
    [Export] public int LevelWidth { get; set; } = 224;
    [Export] public int LevelHeight { get; set; } = 15;

    /// <summary>0 = graine aléatoire.</summary>
    [Export]
    public int Seed { get; set; } = 0;

    [Export(PropertyHint.Range, "0,10,1")] public int Difficulty { get; set; } = 1;

    public Level CurrentLevel { get; private set; }


    protected override void GenerateImage()
    {
        int seed = Seed != 0 ? Seed : System.Random.Shared.Next();
        Generate(LevelWidth, LevelHeight, seed, Difficulty);
    }

    public void Generate(int width, int height, int seed, int difficulty)
    {
        Clear();
        CurrentLevel = LevelGenerator.CreateLevel(width, height, seed, difficulty);

        // Toutes les cases sont posées, y compris le ciel (tuile 0 de l'atlas)
        for (int x = 0; x < CurrentLevel.Width; x++)
        {
            for (int y = 0; y < CurrentLevel.Height; y++)
            {
                byte tile = CurrentLevel.Get(x, y);
                var atlasCoords = new Vector2I(Tiles.Column(tile), Tiles.Row(tile));
                SetCell(new Vector2I(x, y), AtlasSourceId, atlasCoords);
            }
        }
    }
}