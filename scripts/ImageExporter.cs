using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace WFC.scripts;

[GlobalClass]
public partial class ImageExporter : Node2D
{
    [Export] public int ImageCount { get; set; } = 100;
    [Export] public PackedScene[] ScenesToExport = [];

    /// <summary>Nombre de threads. 0 = tous les cœurs logiques.</summary>
    [Export] public int MaxThreads { get; set; } = 0;

    public override void _Ready()
    {
        var dirAccess = DirAccess.Open("res://");
        int threads = MaxThreads > 0 ? MaxThreads : Math.Max(1, OS.GetProcessorCount());
        threads = Math.Min(threads, Math.Max(1, ImageCount));

        foreach (var scene in ScenesToExport)
        {
            var name = scene.ResourcePath.Split("/").Last().Split('.').First();
            var folder = $"exports/{name}";
            dirAccess.MakeDirRecursive(folder);

            // Une instance par thread, créée ici sur le thread principal.
            // Chaque instance est utilisée par un seul thread à la fois.
            var workers = Enumerable.Range(0, threads)
                .Select(_ => scene.Instantiate<ImageTileMapLayer>())
                .ToArray();

            int next = -1;   // prochain index d'image à produire (partagé, atomique)
            int done = 0;

            try
            {
                Parallel.For(
                    0, workers.Length,
                    new ParallelOptions { MaxDegreeOfParallelism = threads },
                    w =>
                    {
                        var level = workers[w];
                        int i;
                        // Distribution dynamique : un thread qui finit tôt prend l'image suivante,
                        // utile quand le temps de génération varie d'une image à l'autre.
                        while ((i = Interlocked.Increment(ref next)) < ImageCount)
                        {
                            level.GenerateAndSave($"{folder}/map_{i}.png");
                            int count = Interlocked.Increment(ref done);
                            if (count % 10 == 0)
                                GD.Print($"{name} : {count}/{ImageCount}");
                        }
                    });
            }
            catch (AggregateException e)
            {
                foreach (var inner in e.InnerExceptions)
                    GD.PushError($"Export de {name} : {inner}");
            }
            finally
            {
                foreach (var w in workers)
                    w.Free(); // nœuds hors arbre : Free() direct
            }

            GD.Print($"{name} : {done}/{ImageCount} images exportées.");
        }
    }
}