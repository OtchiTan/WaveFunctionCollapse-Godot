using System.Linq;
using Godot;

namespace WFC.scripts;

[GlobalClass]
public partial class ImageExporter : Node2D
{
    [Export] public int ImageCount { get; set; } = 100;
    [Export] public PackedScene[] ScenesToExport = [];

    public override void _Ready()
    {
        var dirAccess = DirAccess.Open("res://");

        foreach (var scene in ScenesToExport)
        {
            var level = scene.Instantiate<ImageTileMapLayer>();

            var name = scene.ResourcePath.Split("/").Last().Split('.').First();

            dirAccess.MakeDirRecursive($"exports/{name}");

            for (var i = 0; i < ImageCount; i++)
            {
                level.GenerateAndSave($"exports/{name}/map_{i}.png");
            }
        }
    }
}