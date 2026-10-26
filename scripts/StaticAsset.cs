using Godot;

namespace WFC.scripts;

[GlobalClass]
public partial class StaticAsset : Resource
{
    [Export] public string Name { get; set; } = "";


    [Export] public Vector2I Min { get; set; } = Vector2I.Zero;


    [Export] public Vector2I Max { get; set; } = Vector2I.Zero;
}