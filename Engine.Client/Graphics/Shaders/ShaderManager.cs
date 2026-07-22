using Engine.Shared.IoC;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Engine.Client.Assets;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Engine.Shared.Assets;

namespace Engine.Client.Graphics.Shaders;

/// <summary>
/// Manages all shaders that are compiled in the Monogame Content Pipiline (Content/Shaders).
/// </summary>
public sealed class ShaderManager
{
    private ContentManager _content; 
    private Dictionary<string, Effect> _effects = new();
    public ResPath ShadersResPath = new("Shaders");

    internal void Init()
    {
        IoCManager.ResolveDependencies(this);
        _content = GameClient.Content;
        Scan(Path.Combine(_content.RootDirectory, "Shaders"));
    }

    private void Scan(string root)
    {
        if (!Directory.Exists(root))
            return;

        var dir = Directory.GetFiles(root, "*.xnb", SearchOption.AllDirectories);
        foreach (var file in dir)
        {
            var relativePath = Path.GetRelativePath(_content.RootDirectory, file);
            var assetName = Path.ChangeExtension(relativePath, null);
            assetName = assetName.Replace("\\", "/");
            var name = Path.GetFileNameWithoutExtension(file);

            var shader = _content.Load<Effect>(assetName);
            _effects.Add(name, shader);
        }
    }

    /// <summary>
    /// Returns the ORIGINAL instance of that shader. Please use Effect.Copy() if u are going to use it.
    /// </summary>
    public Effect? GetShader(string? name)
    {
        if (name is null)
            return null;

        _effects.TryGetValue(name, out var effect);
        
        return effect;
    }

    public bool HasShader(string? shaderName)
    {
        if (shaderName == null)
            return false;

        return _effects.ContainsKey(shaderName);
    }

    /// <summary>
    /// Get the name of all shaders that are loaded.
    /// </summary>
    public List<string> GetShaderList()
        => _effects.Keys.ToList();
}
