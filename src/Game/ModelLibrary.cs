using System.Collections.Generic;
using DreamGame.Core;
using Godot;

namespace DreamGame.Game;

/// <summary>
/// The detailed part meshes made in Blender (models/parts.glb, built by tools/blender/parts_library.py), loaded
/// once. A block model's part tagged with one of them (<see cref="ModelPart.Model"/>) is drawn with it, fitted to
/// the part's simple shape; without the library the simple shapes are drawn. Godot makes the meshes' detail
/// levels (fewer triangles further away) when it imports the file.
/// </summary>
public static class ModelLibrary
{
    public const string FilePath = "res://models/parts.glb";
    private static Dictionary<string, Mesh> _meshes;

    public static bool Loaded => Meshes.Count > 0;

    public static bool TryGet(string name, out Mesh mesh) => Meshes.TryGetValue(name, out mesh);

    private static Dictionary<string, Mesh> Meshes
    {
        get
        {
            if (_meshes != null) return _meshes;
            _meshes = new Dictionary<string, Mesh>();
            if (!ResourceLoader.Exists(FilePath))
            {
                GD.Print("[parts] no part library imported: drawing the simple shapes");
                return _meshes;
            }

            Node root = ResourceLoader.Load<PackedScene>(FilePath)?.Instantiate();
            if (root == null) return _meshes;
            Collect(root);
            root.Free();
            foreach (string n in PartLibrary.Names)
                if (!_meshes.ContainsKey(n)) GD.PushWarning($"[parts] the part library has no '{n}'");
            GD.Print($"[parts] part library: {_meshes.Count} meshes");
            return _meshes;
        }
    }

    private static void Collect(Node n)
    {
        if (n is MeshInstance3D { Mesh: not null } mi) _meshes[mi.Name.ToString()] = mi.Mesh;
        foreach (Node c in n.GetChildren()) Collect(c);
    }
}
