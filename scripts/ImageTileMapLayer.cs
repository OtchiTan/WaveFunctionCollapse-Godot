using Godot;

namespace WFC.scripts;

[GlobalClass]
public partial class ImageTileMapLayer : TileMapLayer
{
    public override void _Ready() => GenerateImage();

    public void GenerateAndSave(string filename)
    {
        GenerateImage();

        SaveTileMap(filename);
    }

    protected virtual void GenerateImage()
    {
    }

    private void SaveTileMap(string filename)
    {
        Rect2I used = GetUsedRect();
        if (used.Size.X == 0 || used.Size.Y == 0)
        {
            GD.PrintErr("SaveTileMap : le TileMapLayer est vide.");
            return;
        }

        Vector2I tileSize = TileSet.TileSize;
        var result = Image.CreateEmpty(
            used.Size.X * tileSize.X,
            used.Size.Y * tileSize.Y,
            false,
            Image.Format.Rgba8);

        for (int y = 0; y < used.Size.Y; y++)
        {
            for (int x = 0; x < used.Size.X; x++)
            {
                Vector2I cell = used.Position + new Vector2I(x, y);

                int sourceId = GetCellSourceId(cell);
                if (sourceId == -1)
                    continue; // cellule vide

                if (TileSet.GetSource(sourceId) is not TileSetAtlasSource atlas || atlas.Texture == null)
                    continue;

                Vector2I atlasCoords = GetCellAtlasCoords(cell);
                int alternative = GetCellAlternativeTile(cell);

                // Région de la tuile dans l'atlas
                Rect2I region = atlas.GetTileTextureRegion(atlasCoords);

                Image atlasImage = atlas.Texture.GetImage();
                if (atlasImage.IsCompressed())
                    atlasImage.Decompress();
                atlasImage.Convert(Image.Format.Rgba8);

                Image tileImage = atlasImage.GetRegion(region);

                // Gestion des transformations (flip / transpose) de la tuile alternative
                TileData data = atlas.GetTileData(atlasCoords, alternative);
                if (data != null)
                {
                    if (data.Transpose)
                    {
                        // Transposition = rotation de 90° + flip horizontal
                        tileImage.Rotate90(ClockDirection.Clockwise);
                        tileImage.FlipX();
                    }

                    if (data.FlipH) tileImage.FlipX();
                    if (data.FlipV) tileImage.FlipY();
                }

                result.BlitRect(
                    tileImage,
                    new Rect2I(Vector2I.Zero, tileImage.GetSize()),
                    new Vector2I(x * tileSize.X, y * tileSize.Y));
            }
        }

        Error err = result.SavePng(filename);
        if (err != Error.Ok)
            GD.PrintErr($"SaveTileMap : échec de l'enregistrement ({err}).");
    }
}