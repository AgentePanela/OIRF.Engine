using Engine.Client.Assets.Animation;
using Engine.Client.Assets.Atlas;
using Engine.Client.Graphics;
using Engine.Shared.Assets;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Engine.Client.Assets;

public interface IAssetManager
{
    public ResPath TexturesResPath { get; }
    public event Action? OnLoadingCompleted;

    /// <summary>
    /// Init asset manager and load some assets.
    /// </summary>
    internal bool Init(GraphicsDevice device, SpriteBatch spriteBatch);

    internal void Update(GameTime? dt);
    internal void UpdateLoading(GameTime? dt);

    /// <summary>
    /// Make a Sprite2D based on a texture key from the AtlasBuilder.
    /// </summary>
    public bool GetSprite(string key, [NotNullWhen(true)]out Sprite2D sprite);

    public bool HasSprite(string key);

    /// <summary>
    /// Try to get an animation definition (frame count, speed, loop) loaded from a folder's info.yml.
    /// </summary>
    /// <param name="key">Animation key, e.g. "Player/walk-anim".</param>
    public bool TryGetAnimation(string key, [NotNullWhen(true)] out AnimationDef? def);

    /// <summary>
    /// Add a texture rect to the texture atlas, usefull when u have to dynamic add sprites to the atlas.
    /// </summary>
    public Sprite2D AddSprite(TextureRect texture, string key);

    /// <summary>
    /// Remove a sprite reference from the texture atlas, the real sprite continues in the atlas until it a new sprite covers its old place.
    /// </summary>
    public void RemoveSprite(string key);

    /// <summary>
    /// Get a sprite from the sprite atlas.
    /// </summary>
    /// <param name="key">Sprite path in Resources/Textures/</param>
    /// <returns></returns>
    public bool GetTexture(string key, [NotNullWhen(true)]out AtlasSprite sprite, [NotNullWhen(true)]out AtlasPage page);

    public List<AtlasPage> GetAllAtlasses();

    public string NormalizeKey(string root, string fullPath);

    [Obsolete]
    public string GetResourcesFolder();
}
